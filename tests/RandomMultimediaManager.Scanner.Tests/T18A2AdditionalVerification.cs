using RandomMultimediaManager.App.Scanning;
using RandomMultimediaManager.Core;

internal static partial class T18A2Verification
{
    private static async Task AdditionalReviewRegressions()
    {
        var errors = new List<Exception>();
        foreach (var test in new Func<Task>[] { ScopedCancellation, SharedCancellation, CategoryGroupCancellation, AutomaticOwnerCancellation, MissingRootWatcherRetry, WatcherFailureInjection, WatcherPermissionJoin })
        {
            try { await test(); }
            catch (Exception ex) { Console.WriteLine("FAIL additional review: " + ex.Message); errors.Add(ex); }
        }
        if (errors.Count != 0) throw new AggregateException(errors);
    }

    private static async Task ScopedCancellation()
    {
        using var f = new Fixture(); var a = f.Add(@"\\server\a"); var b = f.Add(@"\\server\b");
        f.Confirm(a); f.Confirm(b);
        var entered = NewSignal(); var release = NewSignal(); CancellationToken active = default; int bCalls = 0;
        var scans = new ScanCoordinator(f.Db, (p, _) => Task.FromResult(Evidence(p)),
            async (_, source, _, _, token) =>
            {
                if (source.Id == a.Id) { active = token; entered.SetResult(); await release.Task; }
                else bCalls++;
                return Empty;
            });
        try
        {
            var first = scans.ScanSourceAsync(a.Id); await entered.Task;
            using var cancel = new CancellationTokenSource();
            var second = scans.ScanSourceAsync(b.Id, cancel.Token); cancel.Cancel();
            Check((await second).Status == LibraryScanStatus.Cancelled, "queued B cancellation completes B");
            Check(!active.IsCancellationRequested, "queued B cancellation must not cancel active A");
            release.SetResult(); Check((await first).Status == LibraryScanStatus.Completed && bCalls == 0, "A completes and cancelled B never collects");
            using var already = new CancellationTokenSource(); already.Cancel();
            Check((await scans.ScanSourceAsync(b.Id, already.Token)).Status == LibraryScanStatus.Cancelled && bCalls == 0,
                "pre-cancelled request admits zero IO");
        }
        finally { release.TrySetResult(); await scans.CloseAsync(); }
        Console.WriteLine("PASS additional review: queued source cancellation is request-scoped");
    }

    private static async Task SharedCancellation()
    {
        foreach (bool automatic in new[] { false, true })
        {
            using var f = new Fixture(); var source = f.Add(@"\\server\shared"); f.Confirm(source);
            if (automatic) f.Db.SaveSourceRefreshPolicy(source.Id, new(true, SourceRefreshMode.Manual));
            var entered = NewSignal(); var release = NewSignal(); CancellationToken active = default;
            IProgress<LibraryScanProgress>? progress = null; int firstReports = 0, secondReports = 0, calls = 0;
            var scans = new ScanCoordinator(f.Db, (p, _) => Task.FromResult(Evidence(p)),
                async (_, _, _, p, token) => { calls++; progress = p; active = token; entered.SetResult(); await release.Task; return Empty; });
            try
            {
                if (automatic) { scans.Start(); await entered.Task; }
                using var cancel = new CancellationTokenSource();
                var first = scans.ScanCategoryAsync(f.Category.Id, new CallbackProgress(_ => firstReports++), cancel.Token);
                await entered.Task;
                var second = scans.ScanCategoryAsync(f.Category.Id, new CallbackProgress(_ => secondReports++));
                cancel.Cancel(); progress!.Report(new("shared", 0, 0));
                Check(!active.IsCancellationRequested && !second.IsCompleted, "one shared waiter cancellation preserves worker and other waiter");
                Check(firstReports == 0 && secondReports == 1, "shared cancellation releases only its progress callback");
                release.SetResult();
                Check((await first).Status == LibraryScanStatus.Cancelled && (await second).Status == LibraryScanStatus.Completed && calls == 1,
                    "shared waiters retain independent results with one collection");
            }
            finally { release.TrySetResult(); await scans.CloseAsync(); }
        }
        Console.WriteLine("PASS additional review: manual/automatic sharing and progress ownership");
    }

    private static async Task CategoryGroupCancellation()
    {
        using var f = new Fixture(); var a = f.Add(@"\\server\group-a"); var b = f.Add(@"\\server\group-b"); f.Confirm(a); f.Confirm(b);
        var entered = NewSignal(); var release = NewSignal(); int bCalls = 0; CancellationToken active = default;
        var scans = new ScanCoordinator(f.Db, (p, _) => Task.FromResult(Evidence(p)),
            async (_, source, _, _, token) =>
            {
                if (source.Id == a.Id) { active = token; entered.TrySetResult(); await release.Task; }
                else bCalls++;
                return Empty;
            });
        try
        {
            var independent = scans.ScanSourceAsync(a.Id); await entered.Task;
            using var cancel = new CancellationTokenSource();
            var group = scans.ScanCategoryAsync(f.Category.Id, token: cancel.Token);
            cancel.Cancel();
            Check((await group).Status == LibraryScanStatus.Cancelled && !active.IsCancellationRequested,
                "category cancellation affects its A/B waiters, not independent A owner");
            release.SetResult();
            Check((await independent).Status == LibraryScanStatus.Completed && bCalls == 0,
                "cancelled category B does not collect, independent A succeeds");
        }
        finally { release.TrySetResult(); await scans.CloseAsync(); }
        Console.WriteLine("PASS additional review: category group cancellation preserves independent source owner");
    }

    private static async Task AutomaticOwnerCancellation()
    {
        using var f = new Fixture(); var source = f.Add(@"\\server\auto-owner"); f.Confirm(source);
        f.Db.SaveSourceRefreshPolicy(source.Id, new(true, SourceRefreshMode.Manual));
        var entered = NewSignal(); var release = NewSignal(); CancellationToken active = default; int calls = 0;
        var scans = new ScanCoordinator(f.Db, (p, _) => Task.FromResult(Evidence(p)),
            async (_, _, _, _, token) => { calls++; active = token; entered.TrySetResult(); await release.Task; return Empty; });
        try
        {
            scans.Start(); await entered.Task;
            using var cancel = new CancellationTokenSource();
            var manual = scans.ScanSourceAsync(source.Id, cancel.Token); cancel.Cancel();
            Check((await manual).Status == LibraryScanStatus.Cancelled && !active.IsCancellationRequested,
                "cancelling sole manual waiter does not cancel automatic owner");
            var close = scans.CloseAsync();
            Check(!close.IsCompleted && active.IsCancellationRequested, "Closing still cancels and drains automatic worker");
            release.SetResult(); await close; scans.Tick();
            Check(calls == 1, "Closing does not restart shared worker");
        }
        finally { release.TrySetResult(); await scans.CloseAsync(); }
        Console.WriteLine("PASS additional review: automatic ownership survives manual cancel, Closing drains real IO");
    }

    private static async Task MissingRootWatcherRetry()
    {
        using var f = new Fixture(); string media = Path.Combine(f.DirectoryPath, "removed");
        Directory.CreateDirectory(media); var source = f.Add(media);
        var proof = await WindowsStorageProbe.ObserveAsync(media, default);
        Check(proof.Kind == StorageKind.Local, "real Windows local proof required");
        var binding = f.Db.GetStorageBinding(media)!; f.Db.ConfirmStorageBinding(media, binding.Revision, proof, false);
        f.Db.SaveSourceRefreshPolicy(source.Id, new(true, SourceRefreshMode.Events, 24));
        Directory.Delete(media);
        var clock = DateTimeOffset.UtcNow; int probes = 0;
        var scans = new ScanCoordinator(f.Db, async (p, t) => { Interlocked.Increment(ref probes); return await WindowsStorageProbe.ObserveAsync(p, t); }, now: () => clock);
        try
        {
            scans.Start(); await scans.Completion; int initial = probes;
            clock += TimeSpan.FromSeconds(3); scans.Tick(); await scans.Completion;
            Check(probes == initial, "missing-root watcher failure must not re-probe after 3 seconds");
            clock += TimeSpan.FromSeconds(27); scans.Tick(); await scans.Completion;
            Check(probes > initial, "watcher retries after approved 30-second delay"); int retry = probes;
            clock += TimeSpan.FromSeconds(119); scans.Tick(); await scans.Completion;
            Check(probes == retry, "successful absence scan must preserve watcher 2-minute backoff");
            Directory.CreateDirectory(media); clock += TimeSpan.FromSeconds(1); scans.Tick(); await scans.Completion;
            Check(probes > retry, "recreated local root reattaches watcher at retry deadline");
            File.WriteAllText(Path.Combine(media, "restored.mp4"), "metadata fixture");
            await Task.Delay(250); clock += TimeSpan.FromSeconds(3); scans.Tick();
            await Until(() => { clock += TimeSpan.FromSeconds(3); scans.Tick(); return f.Db.GetItems(f.Category.Id).Count == 1; });
            Check(scans.GetAccess(source.Id) == SourceAccessState.Available, "watcher recovery returns available");
        }
        finally { await scans.CloseAsync(); }
        Console.WriteLine("PASS additional review: real local folder removal/recreation watcher backoff and recovery");
    }
    private static async Task WatcherFailureInjection()
    {
        using var f = new Fixture(); var source = f.Add(f.DirectoryPath);
        var proof = await WindowsStorageProbe.ObserveAsync(source.RootPath, default);
        var binding = f.Db.GetStorageBinding(source.RootPath)!;
        f.Db.ConfirmStorageBinding(source.RootPath, binding.Revision, proof, false);
        f.Db.SaveSourceRefreshPolicy(source.Id, new(true, SourceRefreshMode.Events, 24));
        var clock = DateTimeOffset.UtcNow; int attempts = 0; bool denied = false, recover = false;
        var scans = new ScanCoordinator(f.Db, (_, _) => Task.FromResult(proof),
            (_, _, _, _, _) => Task.FromResult(Empty), () => clock, source =>
            {
                attempts++;
                if (denied) throw new UnauthorizedAccessException("injected watcher permission failure");
                if (!recover) throw new IOException("injected watcher attach failure");
                return new FileSystemWatcher(source.RootPath);
            });
        try
        {
            scans.Start(); await scans.Completion;
            foreach (int seconds in new[] { 30, 120, 600 })
            {
                int before = attempts;
                clock += TimeSpan.FromSeconds(seconds - 1); scans.Tick(); await scans.Completion;
                Check(attempts == before, "watcher backoff deadline must not be shortened by successful metadata");
                clock += TimeSpan.FromSeconds(1); scans.Tick(); await scans.Completion;
                Check(attempts == before + 1, "watcher retries at 30/120/600 seconds");
            }
            denied = true; await scans.ScanSourceAsync(source.Id); int stopped = attempts;
            clock += TimeSpan.FromHours(48); scans.Tick(); await scans.Completion;
            Check(attempts == stopped && scans.GetAccess(source.Id) == SourceAccessState.AccessDenied,
                "watcher permission failure waits for explicit action despite successful metadata");
            denied = false; recover = true;
            Check((await scans.ScanSourceAsync(source.Id)).Status == LibraryScanStatus.Completed && attempts == stopped + 1,
                "manual retry bypasses backoff and recovers watcher");
            Check(scans.GetAccess(source.Id) == SourceAccessState.Available, "successful watcher recovery clears failure state");
            await scans.CloseAsync(); clock += TimeSpan.FromDays(1); scans.Tick();
            Check(attempts == stopped + 1, "Closing blocks watcher retries");
        }
        finally { await scans.CloseAsync(); }
        Console.WriteLine("PASS additional review: injected watcher 30/120/600 backoff, permission wait, manual recovery, Closing");
    }

    private static async Task WatcherPermissionJoin()
    {
        using var f = new Fixture(); var source = f.Add(f.DirectoryPath);
        var proof = await WindowsStorageProbe.ObserveAsync(source.RootPath, default);
        var binding = f.Db.GetStorageBinding(source.RootPath)!;
        f.Db.ConfirmStorageBinding(source.RootPath, binding.Revision, proof, false);
        f.Db.SaveSourceRefreshPolicy(source.Id, new(true, SourceRefreshMode.Events, 24));
        var clock = DateTimeOffset.UtcNow; int attempts = 0;
        var entered = NewSignal(); var release = NewSignal();
        var scans = new ScanCoordinator(f.Db, (_, _) => Task.FromResult(proof),
            async (_, _, _, _, _) => { entered.TrySetResult(); await release.Task; return Empty; }, () => clock,
            _ => { attempts++; throw new UnauthorizedAccessException("injected watcher permission failure"); });
        try
        {
            scans.Start(); await entered.Task;
            var joined = scans.ScanSourceAsync(source.Id);
            release.SetResult(); await joined; await scans.Completion;
            clock += TimeSpan.FromDays(2); scans.Tick(); await scans.Completion;
            Check(attempts == 1, "joining a worker after watcher denial must not clear its permission wait");
        }
        finally { release.TrySetResult(); await scans.CloseAsync(); }
        Console.WriteLine("PASS additional review: joining a failed watcher observation preserves permission wait");
    }

}
