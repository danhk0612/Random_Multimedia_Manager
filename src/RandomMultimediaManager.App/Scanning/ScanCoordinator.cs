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
    private readonly Func<CategorySource, FileSystemWatcher> createWatcher;
    private bool runningAutomatic;
    private readonly Dictionary<Guid, SourceState> sources = new();
    private readonly Dictionary<string, BindingVerification> bindings = new(StringComparer.Ordinal);
    private Task running = Task.CompletedTask;
    private CancellationTokenSource? cancellation;
    private Timer? timer;
    private bool closing;
    private int exclusions;
    private long generation, order;
    private SourceState? runningSource;
    private long runningGeneration;
    private CategorySource? runningSourceSnapshot;
    private Category? runningCategorySnapshot;

    private sealed class SourceState(Category category, CategorySource source, SourceRefreshPolicy policy)
    {
        public Category Category = category;
        public CategorySource Source = source;
        public SourceRefreshPolicy Policy = policy;
        public long Dirty, Captured, Order;
        public bool Startup, Discovery, Baseline, Manual, Suppressed, WatcherFailed, Collecting, AcceptsManual;
        public DateTimeOffset FirstSignal, LastSignal, RetryAt, WatcherRetryAt;
        public int Failures, WatcherFailures;
        public SourceAccessState Access;
        public FileSystemWatcher? Watcher;
        public RequestGroup? Completion, ActiveCompletion;
    }

    private sealed class ManualRequest(SourceState source, IProgress<LibraryScanProgress>? progress)
    {
        public SourceState Source = source;
        public RequestGroup? Group;
        public readonly TaskCompletionSource<LibraryScanResult> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IProgress<LibraryScanProgress>? Progress = progress;
        public bool Cancelled;
    }
    private sealed class RequestGroup
    {
        public readonly List<ManualRequest> Requests = new();
        public bool HasWaiters => Requests.Any(r => !r.Cancelled && !r.Completion.Task.IsCompleted);
    }

    public ScanCoordinator(LibraryDatabase database,
        Func<string, CancellationToken, Task<StorageObservation>>? observe = null,
        Func<Category, CategorySource, bool, IProgress<LibraryScanProgress>?, CancellationToken, Task<SourceScanObservation>>? collect = null,
        Func<DateTimeOffset>? now = null,
        Func<CategorySource, FileSystemWatcher>? createWatcher = null)
    {
        this.database = database;
        this.observe = observe ?? WindowsStorageProbe.ObserveAsync;
        this.collect = collect ?? new LibraryScanner(database).CollectAsync;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        this.createWatcher = createWatcher ?? (source => new FileSystemWatcher(source.RootPath)
        {
            IncludeSubdirectories = source.IncludeSubdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite
        });
        lock (gate) Reload();
    }

    public bool IsClosing { get { lock (gate) return closing; } }
    public Task Completion { get { lock (gate) return running; } }
    public SourceAccessState GetAccess(Guid id)
    {
        lock (gate) return sources.TryGetValue(id, out var state) ? state.Access : SourceAccessState.Unknown;
    }
    public event Action<Guid, SourceAccessState>? SourceAccessChanged;
    private void NotifyAccessChanged(Guid sourceId, SourceAccessState access) => SourceAccessChanged?.Invoke(sourceId, access);

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
        ManualRequest[] requests;
        lock (gate)
        {
            if (closing || exclusions != 0 || token.IsCancellationRequested) return Cancelled();
            Reload();
            var isolation = database.GetDeletionIsolationSnapshot(includeCategories: true);
            requests = sources.Values.Where(s => s.Category.Id == categoryId && s.Source.IsEnabled)
                .Select(s => Request(s, progress, isolation)).ToArray();
            Pump();
        }
        using var registration = token.Register(() => CancelRequests(requests));
        var results = await Task.WhenAll(requests.Select(r => r.Completion.Task)).ConfigureAwait(false);
        return new(results.Any(r => r.Status == LibraryScanStatus.Cancelled)
                ? LibraryScanStatus.Cancelled : LibraryScanStatus.Completed,
            results.Sum(r => r.PresentCount), results.Sum(r => r.MissingCount),
            results.Sum(r => r.WarningCount), results.SelectMany(r => r.Warnings).ToArray());
    }

    public Task<LibraryScanResult> ScanSourceAsync(Guid sourceId, CancellationToken token = default)
    {
        ManualRequest request;
        lock (gate)
        {
            if (closing || exclusions != 0 || token.IsCancellationRequested) return Task.FromResult(Cancelled());
            Reload();
            if (!sources.TryGetValue(sourceId, out var state) || !state.Source.IsEnabled)
                return Task.FromResult(Cancelled());
            request = Request(state, null, database.GetDeletionIsolationSnapshot(includeCategories: true)); Pump();
        }
        return WaitManual(request, token);
    }
    private async Task<LibraryScanResult> WaitManual(ManualRequest request, CancellationToken token)
    {
        using var registration = token.Register(() => CancelRequests(new[] { request }));
        return await request.Completion.Task.ConfigureAwait(false);
    }
    private ManualRequest Request(SourceState state, IProgress<LibraryScanProgress>? progress, DeletionIsolationSnapshot isolation)
    {
        var request = new ManualRequest(state, progress);
        if (Quarantined(state, isolation))
        {
            request.Progress = null;
            request.Completion.SetResult(Warning("삭제 격리 분류는 스캔할 수 없습니다."));
            return request;
        }
        bool sameWorker = ReferenceEquals(state, runningSource)
            && runningGeneration == generation && runningSourceSnapshot == state.Source
            && runningCategorySnapshot == state.Category && cancellation?.IsCancellationRequested == false;
        if (state.Completion is null
            || (ReferenceEquals(state.Completion, state.ActiveCompletion) && !sameWorker))
        {
            if (!sameWorker) state.Suppressed = false;
            state.Manual = !sameWorker || (!state.AcceptsManual && !state.Collecting);
            state.Completion = new();
            if (!state.Manual) state.ActiveCompletion = state.Completion;
        }
        request.Group = state.Completion;
        state.Completion.Requests.Add(request);
        return request;
    }

    private void CancelRequests(IEnumerable<ManualRequest> requests)
    {
        lock (gate)
        {
            foreach (var request in requests)
            {
                if (request.Completion.Task.IsCompleted || request.Cancelled) continue;
                request.Cancelled = true; request.Progress = null;
                var state = request.Source; var group = request.Group!;
                bool active = ReferenceEquals(state, runningSource) && ReferenceEquals(group, state.ActiveCompletion);
                if (!group.HasWaiters && active && !runningAutomatic)
                {
                    // Last owner of a manual worker: retain its completion until actual IO returns.
                    // A separately queued/new request is not suppressed or cancelled.
                    if (ReferenceEquals(group, state.Completion)) state.Suppressed = true;
                    cancellation?.Cancel();
                }
                else
                {
                    request.Completion.TrySetResult(Cancelled());
                    if (!group.HasWaiters && ReferenceEquals(group, state.Completion))
                    {
                        state.Completion = null; state.Manual = false;
                        if (!active) state.Suppressed = true;
                    }
                }
            }
        }
    }
    public void CancelManual()
    {
        lock (gate)
            CancelRequests(sources.Values.SelectMany(s => new[] { s.Completion, s.ActiveCompletion })
                .Where(g => g is not null).Distinct().SelectMany(g => g!.Requests).ToArray());
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
            foreach (var state in sources.Values)
            {
                state.Access = SourceAccessState.Unconfirmed;
                NotifyAccessChanged(state.Source.Id, state.Access);
            }
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

    // Deletion shares the viewing admission and existing explicit process confirmation.
    public long? ConfirmedDeletionGeneration(StorageBinding binding)
    {
        lock (gate)
            return !closing && exclusions > 0 && bindings.TryGetValue(binding.RootKey, out var verification)
                && verification.Binding == binding && verification.CanAccess(binding.RootKey, verification.Generation)
                ? verification.Generation : null;
    }

    // T18A-5 supplies the user confirmation UI. This API never accepts a changed target.
    public async Task ConfirmBindingAsync(Guid sourceId)
    {
        using var lease = await EnterExclusiveAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("종료 중입니다.");
        Task work;
        long version;
        lock (gate)
        {
            if (closing) throw new InvalidOperationException("종료 중입니다.");
            Reload();
            if (!sources.TryGetValue(sourceId, out var state))
                throw new InvalidOperationException("저장된 소스를 찾을 수 없습니다.");
            var expected = database.GetStorageBinding(state.Source.RootPath)!;
            version = generation;
            cancellation = new();
            var token = cancellation.Token;
            work = Task.Run(async () =>
            {
                token.ThrowIfCancellationRequested();
                var evidence = await observe(state.Source.RootPath, token).ConfigureAwait(false);
                lock (gate)
                {
                    if (closing || version != generation) return;
                    var updated = database.ConfirmStorageBinding(state.Source.RootPath, expected.Revision, evidence, true);
                    var verification = new BindingVerification(updated);
                    verification.Observe(verification.Generation, evidence, true);
                    bindings[updated.RootKey] = verification;
                    state.Suppressed = false;
                    state.Access = verification.Access == BindingAccess.Available
                        ? SourceAccessState.Available : SourceAccessState.Unconfirmed;
                    NotifyAccessChanged(state.Source.Id, state.Access);
                    Reload();
                }
            });
            running = work.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default);
        }
        try { await work.ConfigureAwait(false); }
        catch (Exception ex)
        {
            lock (gate)
                if (!closing && version == generation && sources.TryGetValue(sourceId, out var state))
                {
                    if (bindings.TryGetValue(WindowsPath.Normalize(state.Source.RootPath).RootKey, out var verification))
                        verification.Invalidate();
                    state.Access = ex is UnauthorizedAccessException or System.Security.SecurityException
                        or Win32Exception { NativeErrorCode: 5 } ? SourceAccessState.AccessDenied
                        : ex is InvalidOperationException { Message: var message }
                            && message.Contains("BindingChanged", StringComparison.Ordinal)
                            ? SourceAccessState.BindingChanged : SourceAccessState.Unavailable;
                    state.Suppressed = true;
                    NotifyAccessChanged(state.Source.Id, state.Access);
                }
            throw;
        }
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
                    CompleteRequest(state, state.Completion, Cancelled());
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
            sources[id].Watcher?.Dispose(); CompleteRequest(sources[id], sources[id].Completion, Cancelled()); sources.Remove(id);
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
                    state.WatcherFailures = 0; state.WatcherRetryAt = default;
                    state.Discovery = true;
                    state.Baseline = policy.RefreshMode != SourceRefreshMode.Manual;
                    state.Dirty++; state.FirstSignal = state.LastSignal = now();
                }
            }
        }
    }
    private bool Quarantined(SourceState state, DeletionIsolationSnapshot? isolation = null) =>
        (isolation ?? database.GetDeletionIsolationSnapshot(includeCategories: true)).IsCategoryBlocked(state.Category.Id);

    private bool Eligible(SourceState state, DeletionIsolationSnapshot isolation)
    {
        if (!state.Source.IsEnabled || Quarantined(state, isolation)) return false;
        if (state.Manual) return true;
        if (timer is null || !state.Category.IsEnabled || state.Suppressed) return false;
        if (state.Policy.RefreshMode == SourceRefreshMode.Events && state.WatcherFailures > 0)
            return now() >= state.WatcherRetryAt && now() >= state.RetryAt;
        if (state.Discovery || (state.Baseline && now() >= state.RetryAt)) return true;
        if (state.Startup && state.Policy.ScanOnStartup) return true;
        if (now() < state.RetryAt) return false;
        if (state.Failures > 0 && (state.Policy.ScanOnStartup || state.Policy.RefreshMode != SourceRefreshMode.Manual)) return true;
        if (state.Policy.RefreshMode == SourceRefreshMode.Manual) return false;
        if (state.Dirty != state.Captured && (now() - state.LastSignal >= TimeSpan.FromSeconds(2)
            || now() - state.FirstSignal >= TimeSpan.FromSeconds(30))) return true;
        return state.Policy.IntervalHours is { } hours && (state.Policy.LastCompletedAtUtc is not { } completed
            || now() - DateTimeOffset.FromUnixTimeMilliseconds(completed) >= TimeSpan.FromHours(hours));
    }
    private void Pump()
    {
        if (closing || exclusions != 0 || !running.IsCompleted) return;
        var isolation = database.GetDeletionIsolationSnapshot(includeCategories: true);
        foreach (var queued in sources.Values.Where(s => s.Completion is not null && (!s.Source.IsEnabled || Quarantined(s, isolation))))
        {
            CompleteRequest(queued, queued.Completion, Warning("소스 비활성 또는 삭제 격리로 스캔을 보류합니다."));
            queued.Manual = false;
        }
        var state = sources.Values.Where(s => Eligible(s, isolation)).OrderByDescending(s => s.Manual).ThenBy(s => s.Order).FirstOrDefault();
        if (state is null) return;
        state.Order = ++order;
        runningSource = state;
        long version = generation, dirty = state.Dirty;
        runningGeneration = version; runningSourceSnapshot = state.Source; runningCategorySnapshot = state.Category;
        state.AcceptsManual = true;
        bool manual = state.Manual, startup = state.Startup, discovery = state.Discovery;
        runningAutomatic = !manual && (state.Baseline || (startup && state.Policy.ScanOnStartup) || !discovery);
        state.Manual = false; state.Startup = false; state.Discovery = false;
        state.ActiveCompletion = state.Completion;
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
                CompleteRequest(state, state.ActiveCompletion, result);
                state.ActiveCompletion = null;
                if (result.Status == LibraryScanStatus.Cancelled && !closing && !state.Suppressed)
                { state.Discovery |= discovery; state.Startup |= startup; }
                cancellation?.Dispose(); cancellation = null; state.Collecting = false; state.AcceptsManual = false; runningSource = null;
                NotifyAccessChanged(state.Source.Id, state.Access);
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
        token.ThrowIfCancellationRequested();
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
        }
        bool local = evidence.Kind == StorageKind.Local;
        FileSystemWatcher? failedWatcher = null;
        lock (gate)
            if (state.WatcherFailed)
            {
                failedWatcher = state.Watcher; state.Watcher = null; state.WatcherFailed = false;
            }
        failedWatcher?.Dispose();
        // Watcher construction may itself block: it belongs to this tracked worker.
        token.ThrowIfCancellationRequested();
        if (local && state.Policy.RefreshMode == SourceRefreshMode.Events && state.Watcher is null)
        {
            FileSystemWatcher? watcher = null;
            try
            {
                watcher = createWatcher(source);
                var attached = watcher;
                void Dirty()
                {
                    lock (gate)
                        if (!closing && ReferenceEquals(state.Watcher, attached) && state.Source == source)
                            Signal(source.Id);
                }
                watcher.Changed += (_, _) => Dirty(); watcher.Created += (_, _) => Dirty();
                watcher.Deleted += (_, _) => Dirty(); watcher.Renamed += (_, _) => Dirty();
                watcher.Error += (_, _) =>
                {
                    lock (gate)
                        if (!closing && ReferenceEquals(state.Watcher, attached))
                        { state.WatcherFailed = true; Dirty(); }
                };
                token.ThrowIfCancellationRequested();
                watcher.EnableRaisingEvents = true;
                lock (gate)
                {
                    if (Valid(state, source, category, version, token))
                    {
                        state.Watcher = watcher; watcher = null;
                        state.WatcherFailures = 0; state.WatcherRetryAt = default;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException
                or System.Security.SecurityException or Win32Exception)
            {
                lock (gate)
                    if (Valid(state, source, category, version, token))
                    {
                        state.WatcherFailures++;
                        state.WatcherRetryAt = now() + RetryDelay(state.WatcherFailures);
                        bool denied = ex is UnauthorizedAccessException or System.Security.SecurityException
                            or Win32Exception { NativeErrorCode: 5 };
                        state.Access = denied ? SourceAccessState.AccessDenied : SourceAccessState.Unavailable;
                        if (denied) state.Suppressed = true;
                    }
            }
            finally { watcher?.Dispose(); }
        }
        IProgress<LibraryScanProgress> progress;
        lock (gate)
        {
            if (!Valid(state, source, category, version, token)) return Cancelled();
            // Keep probe/discovery open to same-generation manual waiters until the final
            // collection decision. Once discovery completes, a new request queues separately.
            shouldScan = manual || state.ActiveCompletion?.HasWaiters == true || state.Baseline
                || (startup && state.Policy.ScanOnStartup)
                || (!discovery && (state.Policy.RefreshMode != SourceRefreshMode.Manual
                    || (state.Failures > 0 && state.Policy.ScanOnStartup)));
            state.AcceptsManual = false; state.Collecting = shouldScan;
            progress = new RequestProgress(this, state, version, token);
        }
        if (!shouldScan) return new(LibraryScanStatus.Completed, 0, 0, 0, Array.Empty<string>());
        token.ThrowIfCancellationRequested();
        var observation = await collect(category, source, local, progress, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var after = await observe(source.RootPath, token).ConfigureAwait(false);
        if (local && observation.MissingKeys.Count != 0)
        {
            token.ThrowIfCancellationRequested();
            observation = await LibraryScanner.RecheckLocalMissingAsync(observation, token).ConfigureAwait(false);
        }
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
                state.Captured = dirty; state.Failures = 0; state.Baseline = false;
            }
            else
            {
                state.Access = observation.AccessDenied ? SourceAccessState.AccessDenied : SourceAccessState.Unavailable;
                if (observation.AccessDenied) state.Suppressed = true; else Backoff(state);
                verification.Invalidate();
            }
            return new(LibraryScanStatus.Completed, observation.Present.Count, ids.Length,
                observation.Warnings.Count, observation.Warnings);
        }
    }
    private static void CompleteRequest(SourceState state, RequestGroup? owner, LibraryScanResult result)
    {
        if (owner is null) return;
        foreach (var request in owner.Requests)
        {
            request.Progress = null;
            request.Completion.TrySetResult(request.Cancelled ? Cancelled() : result);
        }
        owner.Requests.Clear();
        if (ReferenceEquals(owner, state.Completion)) state.Completion = null;
    }
    private sealed class RequestProgress(ScanCoordinator owner, SourceState state, long version,
        CancellationToken token) : IProgress<LibraryScanProgress>
    {
        public void Report(LibraryScanProgress value)
        {
            lock (owner.gate)
            {
                if (!owner.closing && version == owner.generation && !token.IsCancellationRequested
                    && owner.cancellation is { } current && current.Token == token
                    && ReferenceEquals(owner.runningSource, state) && state.Collecting
                    && state.ActiveCompletion is { } group)
                    foreach (var request in group.Requests.ToArray())
                        if (!request.Cancelled && !request.Completion.Task.IsCompleted)
                            request.Progress?.Report(value);
            }
        }
    }

    private bool Valid(SourceState state, CategorySource source, Category category, long version, CancellationToken token) =>
        !closing && exclusions == 0 && version == generation && !token.IsCancellationRequested
        && sources.TryGetValue(source.Id, out var current) && ReferenceEquals(state, current)
        && database.GetSources(category.Id).Contains(source) && database.GetCategories().Contains(category);
    private void Backoff(SourceState state)
    {
        state.Failures++;
        state.RetryAt = now() + RetryDelay(state.Failures);
    }
    private static TimeSpan RetryDelay(int failures) => TimeSpan.FromSeconds(failures == 1 ? 30 : failures == 2 ? 120 : 600);
    private static LibraryScanResult Cancelled() => new(LibraryScanStatus.Cancelled, 0, 0, 0, Array.Empty<string>());
    private static LibraryScanResult Warning(string message) => new(LibraryScanStatus.Completed, 0, 0, 1, new[] { message });
}
