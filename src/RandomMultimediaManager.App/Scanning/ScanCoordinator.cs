using System.ComponentModel;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.Scanning;

public enum SourceAccessState { Unknown, Available, Unavailable, AccessDenied, BindingChanged, Unconfirmed }

// One app-owned admission. The lock covers registration and DB-only apply, never filesystem IO
// or an await. A cancelled native call still owns 'running' until it actually returns.
public sealed class ScanCoordinator
{
    private readonly object gate = new();
    private readonly LibraryDatabase database;
    private readonly Func<string, CancellationToken, Task<StorageObservation>> observe;
    private readonly Func<Category, CategorySource, bool, IProgress<LibraryScanProgress>?, CancellationToken, Task<SourceScanObservation>> collect;
    private readonly Func<DateTimeOffset> now;
    private readonly Dictionary<Guid, SourceState> sources = new();
    private readonly Dictionary<string, BindingVerification> bindings = new(StringComparer.Ordinal);
    private Task running = Task.CompletedTask;
    private CancellationTokenSource? cancellation;
    private Timer? timer;
    private bool closing;
    private int exclusions;
    private long generation, order;
    private SourceState? runningSource;

    private sealed class SourceState(Category category, CategorySource source, SourceRefreshPolicy policy)
    {
        public Category Category = category;
        public CategorySource Source = source;
        public SourceRefreshPolicy Policy = policy;
        public long Dirty, Captured, Order;
        public bool Startup, Discovery, Manual, Suppressed;
        public DateTimeOffset FirstSignal, LastSignal, RetryAt;
        public int Failures;
        public SourceAccessState Access;
        public FileSystemWatcher? Watcher;
        public TaskCompletionSource<LibraryScanResult>? Completion;
        public IProgress<LibraryScanProgress>? Progress;
    }

    public ScanCoordinator(LibraryDatabase database,
        Func<string, CancellationToken, Task<StorageObservation>>? observe = null,
        Func<Category, CategorySource, bool, IProgress<LibraryScanProgress>?, CancellationToken, Task<SourceScanObservation>>? collect = null,
        Func<DateTimeOffset>? now = null)
    {
        this.database = database;
        this.observe = observe ?? WindowsStorageProbe.ObserveAsync;
        this.collect = collect ?? new LibraryScanner(database).CollectAsync;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        lock (gate) Reload();
    }

    public bool IsClosing { get { lock (gate) return closing; } }
    public Task Completion { get { lock (gate) return running; } }
    public SourceAccessState GetAccess(Guid id) { lock (gate) return sources[id].Access; }

    public void Start()
    {
        lock (gate)
        {
            if (closing || timer is not null) return;
            foreach (var state in sources.Values) { state.Discovery = true; state.Startup = true; }
            timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            Pump();
        }
    }

    // Public for deterministic clock/overflow tests; production uses one bounded timer.
    public void Tick()
    {
        lock (gate)
        {
            if (closing) return;
            Pump();
        }
    }

    public void Signal(Guid sourceId)
    {
        lock (gate)
        {
            if (closing || !sources.TryGetValue(sourceId, out var state) || state.Suppressed) return;
            if (state.Dirty == state.Captured) state.FirstSignal = now();
            state.LastSignal = now();
            state.Dirty++;
        }
    }

    public async Task<LibraryScanResult> ScanCategoryAsync(Guid categoryId,
        IProgress<LibraryScanProgress>? progress = null, CancellationToken token = default)
    {
        Task<LibraryScanResult>[] tasks;
        lock (gate)
        {
            if (closing || exclusions != 0) return Cancelled();
            Reload();
            tasks = sources.Values.Where(s => s.Category.Id == categoryId && s.Source.IsEnabled)
                .Select(s => Request(s, progress)).ToArray();
            Pump();
        }
        using var registration = token.Register(CancelManual);
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return new(results.Any(r => r.Status == LibraryScanStatus.Cancelled)
                ? LibraryScanStatus.Cancelled : LibraryScanStatus.Completed,
            results.Sum(r => r.PresentCount), results.Sum(r => r.MissingCount),
            results.Sum(r => r.WarningCount), results.SelectMany(r => r.Warnings).ToArray());
    }

    public Task<LibraryScanResult> ScanSourceAsync(Guid sourceId, CancellationToken token = default)
    {
        Task<LibraryScanResult> task;
        lock (gate)
        {
            if (closing || exclusions != 0) return Task.FromResult(Cancelled());
            Reload();
            if (!sources.TryGetValue(sourceId, out var state) || !state.Source.IsEnabled)
                return Task.FromResult(Cancelled());
            task = Request(state, null); Pump();
        }
        return WaitManual(task, token);
    }
    private async Task<LibraryScanResult> WaitManual(Task<LibraryScanResult> task, CancellationToken token)
    { using var registration = token.Register(CancelManual); return await task.ConfigureAwait(false); }
    private Task<LibraryScanResult> Request(SourceState state, IProgress<LibraryScanProgress>? progress)
    {
        if (Quarantined(state)) return Task.FromResult(Warning("삭제 격리 분류는 스캔할 수 없습니다."));
        if (state.Completion is not null) return state.Completion.Task;
        state.Suppressed = false; state.Manual = true; state.Progress = progress;
        state.Completion ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
        return state.Completion.Task;
    }

    public void CancelManual()
    {
        lock (gate)
        {
            foreach (var state in sources.Values.Where(s => s.Manual || s.Completion is not null))
            {
                state.Manual = false; state.Suppressed = true;
                if (!ReferenceEquals(state, runningSource))
                { state.Completion?.TrySetResult(Cancelled()); state.Completion = null; }
            }
            cancellation?.Cancel();
        }
    }

    // Acquire before opening the viewing window (including Empty/SaveFailed), editing sources,
    // or entering deletion recovery. The lease lasts through final save and resource release.
    public async Task<IDisposable?> EnterExclusiveAsync()
    {
        Task pending;
        lock (gate)
        {
            if (closing || exclusions != 0) return null;
            exclusions++; generation++;
            cancellation?.Cancel(); pending = running;
        }
        await pending.ConfigureAwait(false);
        lock (gate)
        {
            if (closing) { exclusions--; return null; }
            return new Lease(this);
        }
    }
    private sealed class Lease(ScanCoordinator owner) : IDisposable
    {
        private ScanCoordinator? value = owner;
        public void Dispose() => Interlocked.Exchange(ref value, null)?.Release();
    }
    private void Release()
    {
        lock (gate)
        {
            exclusions--;
            if (!closing) { Reload(); Pump(); }
        }
    }

    // Use under an exclusive lease. Failed persistence leaves policy/watchers intact.
    public void ConfigurationChanged()
    {
        lock (gate)
        {
            if (closing) return;
            if (exclusions == 0) throw new InvalidOperationException("편집 admission이 필요합니다.");
            generation++;
            foreach (var binding in bindings.Values) binding.Invalidate();
            Reload();
        }
    }
    public async Task SavePolicyAsync(Guid sourceId, SourceRefreshPolicy policy)
    {
        using var lease = await EnterExclusiveAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("종료 중입니다.");
        lock (gate)
        {
            if (closing) throw new InvalidOperationException("종료 중입니다.");
            database.SaveSourceRefreshPolicy(sourceId, policy);
            ConfigurationChanged();
        }
    }

    // T18A-5 supplies the user confirmation UI. This API never accepts a changed target.
    public async Task ConfirmBindingAsync(Guid sourceId)
    {
        using var lease = await EnterExclusiveAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("종료 중입니다.");
        Task work;
        lock (gate)
        {
            if (closing) throw new InvalidOperationException("종료 중입니다.");
            var state = sources[sourceId];
            var expected = database.GetStorageBinding(state.Source.RootPath)!;
            long version = generation;
            cancellation = new();
            work = running = Task.Run(async () =>
            {
                var evidence = await observe(state.Source.RootPath, cancellation.Token).ConfigureAwait(false);
                lock (gate)
                {
                    if (closing || version != generation) return;
                    var updated = database.ConfirmStorageBinding(state.Source.RootPath, expected.Revision, evidence, true);
                    var verification = new BindingVerification(updated);
                    verification.Observe(verification.Generation, evidence, true);
                    bindings[updated.RootKey] = verification;
                    state.Suppressed = false;
                    Reload();
                }
            });
        }
        try { await work.ConfigureAwait(false); }
        finally { lock (gate) { cancellation?.Dispose(); cancellation = null; } }
    }

    public Task CloseAsync()
    {
        lock (gate)
        {
            if (!closing)
            {
                closing = true; generation++;
                timer?.Dispose(); timer = null;
                foreach (var binding in bindings.Values) binding.Invalidate();
                foreach (var state in sources.Values)
                {
                    state.Watcher?.Dispose(); state.Watcher = null;
                    state.Completion?.TrySetResult(Cancelled()); state.Completion = null;
                }
                cancellation?.Cancel();
            }
            return running;
        }
    }

    private void Reload()
    {
        var current = database.GetCategories().SelectMany(c => database.GetSources(c.Id)
            .Select(s => (Category: c, Source: s))).ToArray();
        foreach (var id in sources.Keys.Except(current.Select(x => x.Source.Id)).ToArray())
        {
            sources[id].Watcher?.Dispose(); sources[id].Completion?.TrySetResult(Cancelled()); sources.Remove(id);
        }
        foreach (var (category, source) in current)
        {
            var policy = database.GetEffectiveSourceRefreshPolicy(source.Id);
            if (!sources.TryGetValue(source.Id, out var state))
            {
                sources[source.Id] = state = new(category, source, policy) { Order = ++order,
                    Discovery = timer is not null, Startup = timer is not null };
            }
            else if (state.Source != source || state.Category != category || state.Policy != policy)
            {
                bool changed = state.Source != source || state.Category != category
                    || state.Policy.ScanOnStartup != policy.ScanOnStartup
                    || state.Policy.RefreshMode != policy.RefreshMode || state.Policy.IntervalHours != policy.IntervalHours;
                state.Source = source; state.Category = category; state.Policy = policy;
                if (changed)
                {
                    state.Watcher?.Dispose(); state.Watcher = null; state.Suppressed = false;
                    state.Discovery = true;
                    state.Startup = policy.RefreshMode != SourceRefreshMode.Manual;
                    state.Dirty++; state.FirstSignal = state.LastSignal = now();
                }
            }
        }
    }
    private bool Quarantined(SourceState state) => database.IsDeletionBlocked("")
        || database.GetItems(state.Category.Id).Any(i => database.IsDeletionBlocked(i.PathKey))
        || database.DeletionPaths.Any(p => database.GetSources(state.Category.Id)
            .Any(s => WindowsPath.IsSameOrDescendant(p, s.RootPathKey)));

    private bool Eligible(SourceState state)
    {
        if (!state.Source.IsEnabled || Quarantined(state)) return false;
        if (state.Manual) return true;
        if (!state.Category.IsEnabled || state.Suppressed) return false;
        if (state.Discovery) return true;
        if (state.Startup && state.Policy.ScanOnStartup) return true;
        if (state.Policy.RefreshMode == SourceRefreshMode.Manual || now() < state.RetryAt) return false;
        if (state.Failures > 0) return true;
        if (state.Dirty != state.Captured && (now() - state.LastSignal >= TimeSpan.FromSeconds(2)
            || now() - state.FirstSignal >= TimeSpan.FromSeconds(30))) return true;
        return state.Policy.IntervalHours is { } hours && (state.Policy.LastCompletedAtUtc is not { } completed
            || now() - DateTimeOffset.FromUnixTimeMilliseconds(completed) >= TimeSpan.FromHours(hours));
    }
    private void Pump()
    {
        if (closing || exclusions != 0 || !running.IsCompleted) return;
        var state = sources.Values.Where(Eligible).OrderByDescending(s => s.Manual).ThenBy(s => s.Order).FirstOrDefault();
        if (state is null) return;
        state.Order = ++order;
        runningSource = state;
        long version = generation, dirty = state.Dirty;
        bool manual = state.Manual, startup = state.Startup, discovery = state.Discovery;
        state.Manual = false; state.Startup = false; state.Discovery = false;
        var completion = state.Completion;
        var source = state.Source; var category = state.Category;
        cancellation = new(); var token = cancellation.Token;
        running = Task.Run(async () =>
        {
            LibraryScanResult result;
            try { result = await Execute(state, category, source, version, dirty, manual, startup, discovery, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { result = Cancelled(); }
            catch (Exception ex)
            {
                lock (gate)
                {
                    if (!closing)
                    {
                        state.Access = ex is UnauthorizedAccessException || ex is Win32Exception { NativeErrorCode: 5 }
                            ? SourceAccessState.AccessDenied : SourceAccessState.Unavailable;
                        if (state.Access == SourceAccessState.AccessDenied || ex is InvalidOperationException or ArgumentException)
                            state.Suppressed = true;
                        else Backoff(state);
                        if (bindings.TryGetValue(WindowsPath.Normalize(source.RootPath).RootKey, out var binding)) binding.Invalidate();
                    }
                }
                result = Warning(ex.Message);
            }
            lock (gate)
            {
                completion?.TrySetResult(result);
                if (ReferenceEquals(completion, state.Completion)) state.Completion = null;
                if (result.Status == LibraryScanStatus.Cancelled && !closing && !state.Suppressed)
                { state.Discovery |= discovery; state.Startup |= startup; }
                cancellation?.Dispose(); cancellation = null; runningSource = null;
            }
        });
        // Only scheduling continuation; running is the actual worker, never a timeout proxy.
        _ = running.ContinueWith(_ => { lock (gate) Pump(); }, CancellationToken.None,
            TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private async Task<LibraryScanResult> Execute(SourceState state, Category category, CategorySource source,
        long version, long dirty, bool manual, bool startup, bool discovery, CancellationToken token)
    {
        StorageBinding expected;
        lock (gate) expected = database.GetStorageBinding(source.RootPath)!;
        var evidence = await observe(source.RootPath, token).ConfigureAwait(false);
        BindingVerification verification;
        long bindingGeneration;
        bool shouldScan;
        lock (gate)
        {
            if (!Valid(state, source, category, version, token) || database.GetStorageBinding(source.RootPath) != expected) return Cancelled();
            if (expected.ExpectedTarget is null && evidence.Kind == StorageKind.Local)
                expected = database.ConfirmStorageBinding(source.RootPath, expected.Revision, evidence, false);
            if (!bindings.TryGetValue(expected.RootKey, out verification!) || verification.Binding != expected)
                bindings[expected.RootKey] = verification = new(expected);
            bindingGeneration = verification.Generation;
            // An unknown provider needs an explicit confirmation in this process/generation.
            bool confirmed = verification.CanAccess(source.RootPath, bindingGeneration);
            if (!verification.Observe(bindingGeneration, evidence, confirmed))
            {
                state.Access = verification.Access == BindingAccess.BindingChanged
                    ? SourceAccessState.BindingChanged : SourceAccessState.Unconfirmed;
                state.Suppressed = true;
                return Warning("연결 대상 확인이 필요합니다. 기존 상태를 보존합니다.");
            }
            state.Access = SourceAccessState.Available;
            state.Policy = database.GetEffectiveSourceRefreshPolicy(source.Id);
            shouldScan = manual || (startup && state.Policy.ScanOnStartup)
                || (!discovery && state.Policy.RefreshMode != SourceRefreshMode.Manual);
        }
        bool local = evidence.Kind == StorageKind.Local;
        // Watcher construction may itself block: it belongs to this tracked worker.
        if (local && state.Policy.RefreshMode == SourceRefreshMode.Events && state.Watcher is null)
        {
            FileSystemWatcher? watcher = null;
            try
            {
                watcher = new(source.RootPath) { IncludeSubdirectories = source.IncludeSubdirectories,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite };
                var attached = watcher;
                void Dirty()
                {
                    lock (gate)
                        if (!closing && ReferenceEquals(state.Watcher, attached) && state.Source == source)
                            Signal(source.Id);
                }
                watcher.Changed += (_, _) => Dirty(); watcher.Created += (_, _) => Dirty();
                watcher.Deleted += (_, _) => Dirty(); watcher.Renamed += (_, _) => Dirty();
                watcher.Error += (_, _) => Dirty();
                watcher.EnableRaisingEvents = true;
                lock (gate)
                {
                    if (Valid(state, source, category, version, token)) { state.Watcher = watcher; watcher = null; }
                }
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            { Signal(source.Id); }
            finally { watcher?.Dispose(); }
        }
        if (!shouldScan) return new(LibraryScanStatus.Completed, 0, 0, 0, Array.Empty<string>());
        token.ThrowIfCancellationRequested();
        var observation = await collect(category, source, local, state.Progress, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var after = await observe(source.RootPath, token).ConfigureAwait(false);
        lock (gate)
        {
            if (!Valid(state, source, category, version, token) || database.GetStorageBinding(source.RootPath) != expected
                || !verification.CanAccess(source.RootPath, bindingGeneration)) return Cancelled();
            if (after != evidence || !verification.Observe(bindingGeneration, after, expected.RequiresConfirmation))
            {
                verification.Invalidate(); state.Access = SourceAccessState.BindingChanged; state.Suppressed = true;
                return Warning("관찰 중 연결 대상이 달라져 결과 전체를 폐기했습니다.");
            }
            if (Quarantined(state)) return Warning("삭제 격리 분류의 스캔 반영을 보류합니다.");
            var presentKeys = observation.Present.Select(i => i.PathKey).ToHashSet(StringComparer.Ordinal);
            var missing = local ? observation.MissingKeys.Where(k => !presentKeys.Contains(k)).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            var ids = database.GetCategories().SelectMany(c => database.GetItems(c.Id))
                .Where(i => missing.Contains(i.PathKey)).Select(i => i.Id).ToArray();
            // Apply owns the gate until transaction completion. Cancellation cannot undo success.
            database.ApplyObservedItems(observation.Present.ToArray(), ids);
            if (observation.Warnings.Count == 0)
            {
                state.Policy = state.Policy with { LastCompletedAtUtc = now().ToUnixTimeMilliseconds() };
                database.SaveSourceRefreshPolicy(source.Id, state.Policy);
                state.Captured = dirty; state.Failures = 0;
            }
            else { Backoff(state); verification.Invalidate(); }
            return new(LibraryScanStatus.Completed, observation.Present.Count, ids.Length,
                observation.Warnings.Count, observation.Warnings);
        }
    }
    private bool Valid(SourceState state, CategorySource source, Category category, long version, CancellationToken token) =>
        !closing && exclusions == 0 && version == generation && !token.IsCancellationRequested
        && sources.TryGetValue(source.Id, out var current) && ReferenceEquals(state, current)
        && database.GetSources(category.Id).Contains(source) && database.GetCategories().Contains(category);
    private void Backoff(SourceState state)
    {
        state.Failures++;
        state.RetryAt = now() + TimeSpan.FromSeconds(state.Failures == 1 ? 30 : state.Failures == 2 ? 120 : 600);
    }
    private static LibraryScanResult Cancelled() => new(LibraryScanStatus.Cancelled, 0, 0, 0, Array.Empty<string>());
    private static LibraryScanResult Warning(string message) => new(LibraryScanStatus.Completed, 0, 0, 1, new[] { message });
}
