using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Sessions;
using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.Deletion;

public sealed record FileDeletionResult(DeletionOutcome Outcome, string? Error = null, bool Missing = false);
public sealed record DeletionResult(DeletionRecord? Record, DeletionOutcome Outcome, bool Resolved, string? Error = null);

// One app-owned service; recovery only replays database cleanup, NEVER an OS operation.
public sealed class DeletionService(LibraryDatabase database, DeletionJournal journal,
    Func<DeletionRecord, Task<FileDeletionResult>> deleteFile)
{
    public IReadOnlyList<DeletionRecovery> Pending { get; private set; } = [];
    public bool GloballyBlocked => Pending.Any(p => p.Record is null);
    public async Task InitializeAsync()
    {
        Pending = await Task.Run(journal.ReadAll);
        database.BlockDeletionRecovery(GloballyBlocked);
        foreach (var entry in Pending)
            if (entry.Record is { } r) database.QuarantineDeletion(r.PathKey);
        foreach (var entry in Pending.ToArray())
            if (entry.Record is { Phase: DeletionPhase.Succeeded or DeletionPhase.Failed or DeletionPhase.Cancelled })
                await CompleteAsync(entry.Record);
    }
    public async Task<DeletionRecord> PrepareAsync(string pathKey, DeletionMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentException("삭제 방법을 선택하세요.");
        var targets = await Task.Run(() => database.CaptureDeletion(pathKey));
        var record = new DeletionRecord(1, Guid.NewGuid(), pathKey, targets[0].Path,
            targets.Select(i => new DeletionTarget(i.Id, i.FileSize, i.LastWriteTimeUtc)).ToArray(),
            mode, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), DeletionPhase.Prepared);
        try { await Task.Run(() => journal.Write(record)); }
        catch
        {
            // No OS call occurred. A replacement whose acknowledgement was lost must still be
            // accounted for before lifting isolation.
            Pending = await Task.Run(journal.ReadAll);
            if (!Pending.Any(p => p.Record?.PathKey == pathKey || p.Record is null)) database.ReleaseDeletion(pathKey);
            database.BlockDeletionRecovery(GloballyBlocked);
            throw;
        }
        Pending = Pending.Append(new(journal.FileFor(record), record, null)).ToArray();
        return record;
    }
    public Task<FileDeletionResult> ExecuteAsync(DeletionRecord record) => deleteFile(record);
    public async Task<DeletionResult> RecordResultAsync(DeletionRecord record, FileDeletionResult result)
    {
        var updated = record with { Phase = result.Outcome switch {
            DeletionOutcome.Succeeded => DeletionPhase.Succeeded,
            DeletionOutcome.Failed => DeletionPhase.Failed,
            DeletionOutcome.Cancelled => DeletionPhase.Cancelled,
            _ => DeletionPhase.Unknown } };
        try { await Task.Run(() => journal.Write(updated)); }
        catch (Exception ex)
        {
            // Keep Prepared durable and isolated; even a known in-process success requires
            // explicit confirmation if its durable acknowledgement could not be written.
            return new(record, result.Outcome, false, "삭제 결과 저널 저장 실패: " + ex.Message);
        }
        Pending = Pending.Select(p => p.Record?.OperationId == record.OperationId
            ? new DeletionRecovery(p.File, updated, result.Error) : p).ToArray();
        if (updated.Phase == DeletionPhase.Unknown) return new(updated, result.Outcome, false, result.Error);
        var completed = await CompleteAsync(updated);
        if (result.Missing && completed.Resolved)
            await Task.Run(() => database.ApplyObservedItems([], updated.Targets.Select(t => t.ItemId).ToArray()));
        return completed with { Error = completed.Error ?? result.Error };
    }
    public async Task<DeletionResult> CompleteAsync(DeletionRecord record)
    {
        try
        {
            if (record.Phase == DeletionPhase.Succeeded)
            {
                var applied = await Task.Run(() => database.ApplyDeletion(record.OperationId, record.PathKey,
                    record.Targets.Select(t => t.ItemId).ToArray()));
                if (applied.Status == CommitStatus.Failed) throw new InvalidOperationException(applied.Error);
            }
            else if (record.Phase is not (DeletionPhase.Failed or DeletionPhase.Cancelled))
                return new(record, DeletionOutcome.Unknown, false, "삭제 성공 여부를 확인하세요.");
            await Task.Run(() => journal.Remove(journal.FileFor(record)));
            Pending = Pending.Where(p => p.Record?.OperationId != record.OperationId).ToArray();
            if (!Pending.Any(p => p.Record?.PathKey == record.PathKey)) database.ReleaseDeletion(record.PathKey);
            // Leave AppliedDeletion markers durable. Removing one before a directory deletion
            // is persisted could replay cleanup against a later file at this path after power loss.
            return new(record, record.Phase == DeletionPhase.Succeeded ? DeletionOutcome.Succeeded :
                record.Phase == DeletionPhase.Cancelled ? DeletionOutcome.Cancelled : DeletionOutcome.Failed, true);
        }
        catch (Exception ex) { return new(record, record.Phase == DeletionPhase.Succeeded ? DeletionOutcome.Succeeded : DeletionOutcome.Unknown, false, ex.Message); }
    }
    public async Task<DeletionResult> ConfirmAsync(DeletionRecovery entry, bool succeeded)
    {
        if (entry.Record is null)
        {
            // A corrupt file cannot identify its target set. Success must not guess which
            // histories to clear. Only explicit failure/cancellation can preserve all data.
            if (succeeded) return new(null, DeletionOutcome.Unknown, false, "손상 저널은 삭제 대상을 알 수 없어 성공 정리를 실행할 수 없습니다.");
            await Task.Run(() => journal.Remove(entry.File));
            Pending = Pending.Where(p => p.File != entry.File).ToArray();
            database.BlockDeletionRecovery(GloballyBlocked);
            return new(null, DeletionOutcome.Failed, true);
        }
        return await RecordResultAsync(entry.Record, new(succeeded ? DeletionOutcome.Succeeded : DeletionOutcome.Failed));
    }
}
