using RandomMultimediaManager.App.Scanning;
using RandomMultimediaManager.Core;

internal static partial class T18A2Verification
{
    private static async Task AdditionalReviewRegressions()
    {
        var errors = new List<Exception>();
        foreach (var test in new Func<Task>[] { ScopedCancellation, SharedCancellation, MissingRootWatcherRetry })
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
            await Until(() => f.Db.GetItems(f.Category.Id).Count == 1);
            Check(scans.GetAccess(source.Id) == SourceAccessState.Available, "watcher recovery returns available");
        }
        finally { await scans.CloseAsync(); }
        Console.WriteLine("PASS additional review: real local folder removal/recreation watcher backoff and recovery");
    }
}
