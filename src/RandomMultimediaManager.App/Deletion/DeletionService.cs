using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Scanning;
using RandomMultimediaManager.App.Sessions;
using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.Deletion;

public sealed record FileDeletionResult(DeletionOutcome Outcome, string? Error = null, bool Missing = false);
public sealed record DeletionResult(DeletionRecord? Record, DeletionOutcome Outcome, bool Resolved, string? Error = null);

// Recovery consumes the durable captured set only. It NEVER reinterprets mappings or calls OS deletion.
public sealed class DeletionService(LibraryDatabase database, DeletionJournal journal,
    Func<DeletionRecord, Task<FileDeletionResult>> deleteFile,
    Func<string, CancellationToken, Task<StorageObservation>>? observe = null,
    Func<StorageBinding, RecycleCapability>? recycleCapability = null)
{
    private readonly HashSet<Guid> incompleteSuccess = [];
    private readonly HashSet<Guid> issued = [];
    private readonly HashSet<Guid> executing = [];
    private readonly Dictionary<Guid, DeletionRecord> prepared = [];
    private readonly object ioGate = new();
    private readonly HashSet<Task> io = [];
    private bool closing;
    // The scan coordinator supplies only an already confirmed process generation under its
    // exclusive viewing lease. Unknown is never authorized from persisted user confirmation.
    public Func<StorageBinding, long?>? ConfirmedGeneration { get; set; }
    public bool HasIncompleteSuccess => incompleteSuccess.Count != 0
        || Pending.Any(p => p.Record?.Phase == DeletionPhase.Succeeded);
    public IReadOnlyList<DeletionRecovery> Pending { get; private set; } = [];
    public bool GloballyBlocked => Pending.Any(p => p.PathKey is null);
    public void BeginClosing() { lock (ioGate) closing = true; }
    public Task DrainAsync() { lock (ioGate) return Task.WhenAll(io.ToArray()); }
    private Task<T> Io<T>(Func<Task<T>> action)
    {
        lock (ioGate)
        {
            if (closing) throw new InvalidOperationException("종료 중에는 새 삭제 IO를 시작하지 않습니다.");
            var task = Task.Run(action); io.Add(task);
            _ = task.ContinueWith(t => { lock (ioGate) io.Remove(t); }, TaskScheduler.Default);
            return task;
        }
    }
    private Task<StorageObservation> Observe(string path)
    {
        lock (ioGate)
        {
            if (closing) throw new InvalidOperationException("종료 중에는 새 삭제 조회를 시작하지 않습니다.");
            return (observe ?? WindowsStorageProbe.ObserveAsync)(path, CancellationToken.None);
        }
    }
    private bool RemoteScope(DeletionRecovery entry)
    {
        if (entry.Record is { Version: 2 } r) return r.QuarantineScope == DeletionQuarantineScope.RemoteAndUnknown;
        // v1 has no alias/binding proof; syntax and persisted Local proof are the only narrow case.
        try { return entry.PathKey is { } key && database.GetStorageBinding(key) is not { Kind: StorageKind.Local, RequiresConfirmation: false }; }
        catch (ArgumentException) { return true; }
    }
    private static IEnumerable<string> Keys(DeletionRecovery entry) => entry.Record is { Version: 2 } r
        ? r.Targets.Select(t => t.PathKey!) : entry.PathKey is { } key ? [key] : [];
    private void RestoreIsolation() => database.SetDeletionIsolation(Pending.SelectMany(Keys), Pending.Any(RemoteScope), GloballyBlocked);
    public async Task InitializeAsync()
    {
        Pending = await Task.Run(journal.ReadAll);
        RestoreIsolation();
        foreach (var entry in Pending.ToArray())
            if (entry.Record is { Phase: DeletionPhase.Succeeded or DeletionPhase.Failed or DeletionPhase.Cancelled })
                await CompleteAsync(entry.Record);
    }
    public Task<DeletionRecord> PrepareAsync(string pathKey, DeletionMode mode) => Io(async () =>
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentException("삭제 방법을 선택하세요.");
        var snapshot = database.GetSessionSnapshot();
        var execution = snapshot.Items.FirstOrDefault(i => i.PathKey == pathKey)
            ?? throw new InvalidOperationException("삭제 대상이 없습니다.");
        var roots = snapshot.Items.Select(i => WindowsPath.Normalize(i.Path).RootKey).Distinct().ToArray();
        var verified = new Dictionary<string, DeletionBinding>(StringComparer.Ordinal);
        var observations = new Dictionary<string, StorageObservation>(StringComparer.Ordinal);
        // Gather connection evidence outside the DB writer. Failed/changed alias roots are
        // excluded from cleanup, never guessed, while the execution root must be verified.
        foreach (var root in roots.OrderBy(r => r == WindowsPath.Normalize(execution.Path).RootKey ? 0 : 1))
        {
            if (observations.Values.FirstOrDefault()?.Kind == StorageKind.Local) break;
            try
            {
                var binding = database.GetStorageBinding(root)!;
                var evidence = await Observe(root);
                if (binding.ExpectedTarget is null && evidence.Kind == StorageKind.Local)
                    binding = database.ConfirmStorageBinding(root, binding.Revision, evidence, false);
                long? generation = ConfirmedGeneration?.Invoke(binding);
                var check = new BindingVerification(binding);
                if (!check.Observe(0, evidence, generation is not null)) throw new InvalidOperationException("삭제 대상 바인딩 확인이 필요합니다.");
                verified.Add(root, new(binding, generation ?? 0)); observations.Add(root, evidence);
            }
            catch when (root != WindowsPath.Normalize(execution.Path).RootKey) { /* no alias proof */ }
        }
        string executionRoot = WindowsPath.Normalize(execution.Path).RootKey;
        var primary = verified[executionRoot];
        var capability = (recycleCapability ?? RecycleCapability.For)(primary.Binding);
        if (mode == DeletionMode.Recycle && !capability.CanRecycle)
            throw new InvalidOperationException(capability.Reason ?? "휴지통 이동을 보장할 수 없습니다. 영구삭제는 별도로 선택해야 합니다.");
        var keys = new HashSet<string>(StringComparer.Ordinal) { pathKey };
        var alias = DeletionSafety.AliasKey(execution.Path, observations[executionRoot]);
        if (alias is not null)
            foreach (var item in snapshot.Items)
                if (observations.TryGetValue(WindowsPath.Normalize(item.Path).RootKey, out var evidence)
                    && DeletionSafety.AliasKey(item.Path, evidence) == alias) keys.Add(item.PathKey);
        var bindings = verified.Values.Where(b => keys.Any(k => WindowsPath.Normalize(k).RootKey == b.Binding.RootKey)).ToArray();
        // Recheck all captured roots after collection, before capture and durable intent.
        await VerifyAsync(bindings);
        bool remote = primary.Binding.Kind != StorageKind.Local || primary.Binding.RequiresConfirmation;
        var targets = database.CaptureDeletion(pathKey, keys, bindings, remote);
        var record = new DeletionRecord(2, Guid.NewGuid(), pathKey, execution.Path,
            targets.Select(i => new DeletionTarget(i.Id, i.FileSize, i.LastWriteTimeUtc, i.PathKey)).ToArray(),
            mode, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), DeletionPhase.Prepared, bindings,
            remote ? DeletionQuarantineScope.RemoteAndUnknown : DeletionQuarantineScope.Targets);
        try { journal.Write(record); }
        catch
        {
            Pending = journal.ReadAll(); RestoreIsolation(); throw;
        }
        Pending = Pending.Append(new(journal.FileFor(record), record, null)).ToArray();
        lock (ioGate) prepared.Add(record.OperationId, record);
        return record;
    });
    private async Task VerifyAsync(IEnumerable<DeletionBinding> bindings)
    {
        foreach (var captured in bindings)
        {
            var binding = captured.Binding;
            if (database.GetStorageBinding(binding.RootKey) != binding)
                throw new InvalidOperationException("오래된 삭제 바인딩입니다.");
            var evidence = await Observe(binding.RootKey);
            long? generation = ConfirmedGeneration?.Invoke(binding);
            if (binding.RequiresConfirmation && generation != captured.Generation)
                throw new InvalidOperationException("삭제 대상 재확인이 필요합니다.");
            if (!new BindingVerification(binding).Observe(0, evidence, generation is not null)
                || database.GetStorageBinding(binding.RootKey) != binding)
                throw new InvalidOperationException("BindingChanged: 삭제 대상이 변경되었거나 확인되지 않았습니다.");
        }
    }
    // Deletion failure/confirmation may preserve a Pending visit after a remap. Never
    // reopen the new target as that old visit; the general viewing policy belongs to T18A-4.
    public async Task<string?> ValidateReopenAsync(DeletionRecord record)
    {
        try
        {
            if (record.Version != 2) return "원래 삭제 대상의 바인딩을 확인할 수 없어 재열기를 보류합니다.";
            await Io(async () =>
            {
                await VerifyAsync(record.Bindings!.Where(b => b.Binding.RootKey == WindowsPath.Normalize(record.Path).RootKey));
                return true;
            });
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }
    public Task<FileDeletionResult> ExecuteAsync(DeletionRecord record)
    {
        lock (ioGate)
        {
            if (closing) return Task.FromResult(new FileDeletionResult(DeletionOutcome.Failed, "종료 중: OS 삭제를 시작하지 않았습니다."));
            if (!prepared.TryGetValue(record.OperationId, out var original) || original != record || !issued.Add(record.OperationId))
                throw new InvalidOperationException("이 프로세스에서 준비된 삭제는 한 번만 실행할 수 있습니다.");
            executing.Add(record.OperationId);
            return Io(async () =>
            {
                bool started = false;
                try
                {
                    await VerifyAsync(record.Bindings!);
                    Task<FileDeletionResult> work;
                    lock (ioGate)
                    {
                        if (closing) return new FileDeletionResult(DeletionOutcome.Failed, "종료 중: OS 삭제를 시작하지 않았습니다.");
                        started = true; work = deleteFile(record);
                    }
                    var result = await work;
                    // A remote absence/error cannot be transformed into successful deletion or Missing.
                    return DeletionSafety.IsRemote(record) && result.Missing
                        ? new(DeletionOutcome.Unknown, result.Error) : result;
                }
                catch (Exception ex) { return new(started ? DeletionOutcome.Unknown : DeletionOutcome.Failed, ex.Message); }
                finally { lock (ioGate) executing.Remove(record.OperationId); }
            });
        }
    }
    private bool Live(Guid id) { lock (ioGate) return executing.Contains(id); }
    public async Task<DeletionResult> RecordResultAsync(DeletionRecord record, FileDeletionResult result)
    {
        if (Live(record.OperationId)) return new(record, DeletionOutcome.Unknown, false, "실제 OS 삭제 IO 완료를 기다리세요.");
        if (result.Outcome == DeletionOutcome.Succeeded) incompleteSuccess.Add(record.OperationId);
        var updated = record with { Phase = result.Outcome switch {
            DeletionOutcome.Succeeded => DeletionPhase.Succeeded,
            DeletionOutcome.Failed => DeletionPhase.Failed,
            DeletionOutcome.Cancelled => DeletionPhase.Cancelled,
            _ => DeletionPhase.Unknown } };
        try { await Task.Run(() => journal.Write(updated)); }
        catch (Exception ex) { return new(record, result.Outcome, false, "삭제 결과 저널 저장 실패: " + ex.Message); }
        Pending = Pending.Select(p => p.Record?.OperationId == record.OperationId
            ? new DeletionRecovery(p.File, updated, result.Error) : p).ToArray();
        if (updated.Phase == DeletionPhase.Unknown) return new(updated, result.Outcome, false, result.Error);
        var completed = await CompleteAsync(updated);
        if (result.Missing && completed.Resolved && !DeletionSafety.IsRemote(updated))
            await Task.Run(() => database.ApplyObservedItems([], updated.Targets.Select(t => t.ItemId).ToArray()));
        return completed with { Error = completed.Error ?? result.Error };
    }
    public async Task<DeletionResult> CompleteAsync(DeletionRecord record)
    {
        if (Live(record.OperationId)) return new(record, DeletionOutcome.Unknown, false, "실제 OS 삭제 IO 완료를 기다리세요.");
        try
        {
            if (record.Phase == DeletionPhase.Succeeded)
            {
                var applied = await Task.Run(() => record.Version == 1
                    ? database.ApplyDeletion(record.OperationId, record.PathKey, record.Targets.Select(t => t.ItemId).ToArray())
                    : database.ApplyDeletion(record.OperationId, record.Targets));
                if (applied.Status == CommitStatus.Failed) throw new InvalidOperationException(applied.Error);
            }
            else if (record.Phase is not (DeletionPhase.Failed or DeletionPhase.Cancelled))
                return new(record, DeletionOutcome.Unknown, false, "삭제 성공 여부를 확인하세요.");
            await Task.Run(() => journal.Remove(journal.FileFor(record)));
            Pending = Pending.Where(p => p.Record?.OperationId != record.OperationId).ToArray();
            incompleteSuccess.Remove(record.OperationId);
            lock (ioGate) prepared.Remove(record.OperationId);
            RestoreIsolation();
            // AppliedDeletion remains durable, including after journal removal.
            return new(record, record.Phase == DeletionPhase.Succeeded ? DeletionOutcome.Succeeded :
                record.Phase == DeletionPhase.Cancelled ? DeletionOutcome.Cancelled : DeletionOutcome.Failed, true);
        }
        catch (Exception ex) { return new(record, record.Phase == DeletionPhase.Succeeded ? DeletionOutcome.Succeeded : DeletionOutcome.Unknown, false, ex.Message); }
    }
    public async Task<DeletionResult> ConfirmAsync(DeletionRecovery entry, bool succeeded)
    {
        if (entry.Record is { } live && Live(live.OperationId))
            return new(live, DeletionOutcome.Unknown, false, "실제 OS 삭제 IO 완료를 기다리세요.");
        if (entry.Record is null)
        {
            if (succeeded) return new(null, DeletionOutcome.Unknown, false, "손상 저널은 삭제 대상을 알 수 없어 성공 정리를 실행할 수 없습니다.");
            await Task.Run(() => journal.Remove(entry.File));
            Pending = Pending.Where(p => p.File != entry.File).ToArray(); RestoreIsolation();
            return new(null, DeletionOutcome.Failed, true);
        }
        if (entry.Record.Phase is DeletionPhase.Succeeded or DeletionPhase.Failed or DeletionPhase.Cancelled)
            return await CompleteAsync(entry.Record);
        return await RecordResultAsync(entry.Record, new(succeeded ? DeletionOutcome.Succeeded : DeletionOutcome.Failed));
    }
}
