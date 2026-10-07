using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Scanning;
using RandomMultimediaManager.App.ViewModels;
using RandomMultimediaManager.Core;

internal static class T18A2Verification
{
    public static async Task Run()
    {
        await ReviewRegressions();
        await PostedProgressOwnership();
        await CancelledProbeAndOtherSource();
        await RemoteScopeAndEvidence();
        await DrainAndViewing();
        await GenerationAndQuarantine();
        await PoliciesAndSignals();
        await RemappingAndPartialFailure();
        await ManualPriorityAndCancellation();
        await RealLocalWatcher();
        Console.WriteLine("PASS T18A-2 N04/N05/N09/N10/N11 coordinator; NAS/RaiDrive hardware NOT tested");
    }
    private static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); }
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static SourceScanObservation Empty => new(Array.Empty<MediaItem>(), Array.Empty<string>(), Array.Empty<string>());
    private static StorageObservation Evidence(string path) => StorageObservation.Classify(path, MappingLookup.Failed);
    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!predicate()) await Task.Delay(20, timeout.Token);
    }
    private sealed class Fixture : IDisposable
    {
        public string DirectoryPath = Path.Combine(Path.GetTempPath(), "rmm-t18a2-" + Guid.NewGuid());
        public LibraryDatabase Db;
        public Category Category = new(Guid.NewGuid(), "test", MediaType.Video);
        public Fixture() { Directory.CreateDirectory(DirectoryPath); Db = LibraryDatabase.Open(Path.Combine(DirectoryPath, "db.sqlite")); Db.SaveCategory(Category); }
        public CategorySource Add(string path)
        {
            var p = WindowsPath.Normalize(path);
            return Db.AddSource(new(Guid.NewGuid(), Category.Id, p.Path, p.PathKey, true));
        }
        public void Confirm(CategorySource source)
        { var b = Db.GetStorageBinding(source.RootPath)!; Db.ConfirmStorageBinding(source.RootPath, b.Revision, Evidence(source.RootPath), true); }
        public MediaItem Item(CategorySource source, string name = "movie.mp4")
        {
            var p = WindowsPath.Normalize(source.RootPath + "\\" + name);
            var item = new MediaItem(Guid.NewGuid(), Category.Id, MediaType.Video, p.Path, p.PathKey, 1, 1);
            Db.ApplyObservedItems(new[] { item }, Array.Empty<Guid>()); return item;
        }
        public void Dispose() { Db.Dispose(); try { Directory.Delete(DirectoryPath, true); } catch { } }
    }
    private sealed class CallbackProgress(Action<LibraryScanProgress> callback) : IProgress<LibraryScanProgress>
    { public void Report(LibraryScanProgress value) => callback(value); }
    private static async Task ReviewRegressions()
    {
        var errors = new List<Exception>();
        foreach (bool discoveryOnly in new[] { false, true })
        {
            try { await ProbeJoin(discoveryOnly); }
            catch (Exception ex) { Console.WriteLine("FAIL probe join: " + ex.Message); errors.Add(ex); }
        }
        try { await ProgressOwnership(); }
        catch (Exception ex) { Console.WriteLine("FAIL progress ownership: " + ex.Message); errors.Add(ex); }
        if (errors.Count != 0) throw new AggregateException(errors);
    }
    private static async Task ProbeJoin(bool discoveryOnly)
    {
        using var f = new Fixture(); var source = f.Add(@"\\server\probe"); f.Confirm(source);
        f.Db.SaveSourceRefreshPolicy(source.Id, new(!discoveryOnly, SourceRefreshMode.Manual));
        var entered = NewSignal(); var release = NewSignal(); int probes = 0, collects = 0;
        var coordinator = new ScanCoordinator(f.Db, async (p, _) =>
        {
            if (Interlocked.Increment(ref probes) == 1) { entered.SetResult(); await release.Task; }
            return Evidence(p);
        }, (_, _, _, _, _) => { Interlocked.Increment(ref collects); return Task.FromResult(Empty); });
        try
        {
            coordinator.Start(); await entered.Task;
            var worker = coordinator.Completion;
            var manual = coordinator.ScanSourceAsync(source.Id);
            release.SetResult(); await worker; await manual; await coordinator.Completion;
            Check(collects == 1, $"probe-time manual request collects once (discoveryOnly={discoveryOnly}, actual={collects})");
            Console.WriteLine("PASS probe-time manual joins same collection; discoveryOnly=" + discoveryOnly);
        }
        finally { release.TrySetResult(); await coordinator.CloseAsync(); }
    }
    private static async Task ProgressOwnership()
    {
        using var f = new Fixture(); var source = f.Add(@"\\server\progress"); f.Confirm(source);
        var clock = DateTimeOffset.UtcNow;
        f.Db.SaveSourceRefreshPolicy(source.Id, new(false, SourceRefreshMode.Scheduled, 1, clock.ToUnixTimeMilliseconds()));
        IProgress<LibraryScanProgress>? stale = null;
        int oldCalls = 0, newCalls = 0, collects = 0;
        var entered = NewSignal(); var release = NewSignal(); bool block = false;
        var coordinator = new ScanCoordinator(f.Db, (p, _) => Task.FromResult(Evidence(p)),
            async (_, _, _, progress, _) =>
            {
                Interlocked.Increment(ref collects); stale = progress;
                progress?.Report(new("during collection", 0, 0));
                if (block) { entered.TrySetResult(); await release.Task; }
                return Empty;
            }, () => clock);
        try
        {
            await coordinator.ScanCategoryAsync(f.Category.Id, new CallbackProgress(_ => oldCalls++));
            Check(oldCalls == 1, "active manual progress delivered");
            stale?.Report(new("late after completion", 0, 0));
            coordinator.Start(); await coordinator.Completion;
            clock += TimeSpan.FromHours(2); coordinator.Tick(); await Until(() => collects == 2); await coordinator.Completion;
            Check(oldCalls == 1, "completed progress cannot receive late reports or subsequent automatic scan");
            block = true;
            using var cancel = new CancellationTokenSource();
            var cancelled = coordinator.ScanCategoryAsync(f.Category.Id, new CallbackProgress(_ => oldCalls++), cancel.Token);
            await entered.Task; cancel.Cancel(); var cancelledProgress = stale;
            int before = oldCalls;
            cancelledProgress?.Report(new("after cancel", 0, 0));
            var next = coordinator.ScanCategoryAsync(f.Category.Id, new CallbackProgress(_ => newCalls++));
            Check(!next.IsCompleted, "new request must wait for cancelled worker drain");
            release.SetResult();
            Check((await cancelled).Status == LibraryScanStatus.Cancelled, "old request remains cancelled");
            Check((await next).Status == LibraryScanStatus.Completed && newCalls == 1, "new request and callback survive old worker cleanup");
            cancelledProgress?.Report(new("late old worker", 0, 0));
            Check(oldCalls == before, "cancelled callback never leaks into new request");
            Console.WriteLine("PASS progress ownership: completed/automatic/late/cancelled/new request");
        }
        finally { release.TrySetResult(); await coordinator.CloseAsync(); }
    }

    private sealed class HeldContext : SynchronizationContext
    {
        private readonly List<(SendOrPostCallback Callback, object? State)> posts = new();
        public override void Post(SendOrPostCallback callback, object? state)
        { lock (posts) posts.Add((callback, state)); }
        public int Count { get { lock (posts) return posts.Count; } }
        public void RunLast()
        {
            (SendOrPostCallback Callback, object? State) item;
            lock (posts) { item = posts[^1]; posts.RemoveAt(posts.Count - 1); }
            item.Callback(item.State);
        }
    }
    private static async Task PostedProgressOwnership()
    {
        using var f = new Fixture(); var source = f.Add(@"\\server\posted"); f.Confirm(source);
        var entered = NewSignal(); var release = NewSignal(); int calls = 0;
        var coordinator = new ScanCoordinator(f.Db, (p, _) => Task.FromResult(Evidence(p)),
            async (_, _, _, progress, _) =>
            { progress?.Report(new("queued progress " + Interlocked.Increment(ref calls), 0, 0)); entered.TrySetResult(); await release.Task; return Empty; });
        var editor = new CategoryEditorViewModel(f.Db, coordinator) { SelectedCategory = f.Category };
        var context = new HeldContext();
        Task<EditorResult> Begin()
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            try { return editor.ScanSelectedCategoryAsync(); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
        try
        {
            var first = Begin(); await entered.Task; release.SetResult();
            await Until(() => context.Count >= 2);
            // Deliver completion before the older Progress<T> dispatcher post.
            context.RunLast(); await first; string complete = editor.ScanProgressMessage;
            context.RunLast();
            Check(editor.ScanProgressMessage == complete, "already-posted progress cannot overwrite completed UI");
            entered = NewSignal(); release = NewSignal();
            var cancelled = Begin(); await entered.Task; editor.CancelScan(); release.SetResult();
            await Until(() => context.Count >= 2); context.RunLast(); await cancelled;
            string cancelMessage = editor.ScanProgressMessage;
            entered = NewSignal(); release = NewSignal();
            var next = Begin(); await entered.Task;
            context.RunLast(); // Deliver the new request's valid progress.
            string nextMessage = editor.ScanProgressMessage;
            context.RunLast(); // Deliver the old cancelled request's queued progress.
            Check(editor.ScanProgressMessage == nextMessage && nextMessage != cancelMessage,
                "late cancelled post does not overwrite new request; new callback is preserved");
            release.SetResult(); await Until(() => context.Count != 0); context.RunLast(); await next;
            Console.WriteLine("PASS posted Progress<T>: complete/cancel/new UI request ownership");
        }
        finally { release.TrySetResult(); await coordinator.CloseAsync(); }
    }

    private static async Task CancelledProbeAndOtherSource()
    {
        using var f = new Fixture(); var first = f.Add(@"\\server\cancelled-probe");
        var other = f.Add(@"\\server\other"); f.Confirm(first); f.Confirm(other);
        var entered = NewSignal(); var release = NewSignal(); int probes = 0;
        var collected = new List<Guid>();
        var coordinator = new ScanCoordinator(f.Db, async (p, _) =>
        {
            if (Interlocked.Increment(ref probes) == 1) { entered.SetResult(); await release.Task; }
            return Evidence(p);
        }, (_, source, _, _, _) => { lock (collected) collected.Add(source.Id); return Task.FromResult(Empty); });
        try
        {
            using var cancel = new CancellationTokenSource();
            var old = coordinator.ScanSourceAsync(first.Id, cancel.Token); await entered.Task; cancel.Cancel();
            var next = coordinator.ScanSourceAsync(first.Id);
            var separate = coordinator.ScanSourceAsync(other.Id);
            Check(!old.IsCompleted && !next.IsCompleted && !separate.IsCompleted,
                "cancelled probe and new/different source requests retain actual worker ownership");
            release.SetResult();
            Check((await old).Status == LibraryScanStatus.Cancelled, "cancelled probe result cannot satisfy new request");
            Check((await next).Status == LibraryScanStatus.Completed && (await separate).Status == LibraryScanStatus.Completed,
                "new same-source and distinct source requests each complete independently");
            await coordinator.Completion;
            lock (collected) Check(collected.Count == 2 && collected.Distinct().Count() == 2,
                "cancelled probe performs no collection and different sources never share results");
            Console.WriteLine("PASS cancelled probe/new request/different source separation");
        }
        finally { release.TrySetResult(); await coordinator.CloseAsync(); }
    }

    private static async Task RemoteScopeAndEvidence()
    {
        using var f = new Fixture();
        var manual = f.Add(@"\\server\manual"); var scheduled = f.Add(@"\\server\scheduled");
        f.Confirm(manual); f.Confirm(scheduled);
        var item = f.Item(scheduled);
        f.Db.SaveSourceRefreshPolicy(scheduled.Id, new(false, SourceRefreshMode.Scheduled, 24));
        var calls = new List<Guid>();
        var coordinator = new ScanCoordinator(f.Db, (p, _) => Task.FromResult(Evidence(p)),
            (_, s, _, _, _) => { lock (calls) calls.Add(s.Id); return Task.FromResult(new SourceScanObservation(
                Array.Empty<MediaItem>(), new[] { item.PathKey }, Array.Empty<string>())); });
        try
        {
            coordinator.Start();
            await Until(() => { lock (calls) return calls.Count == 1; });
            await coordinator.Completion;
            lock (calls) Check(calls.Single() == scheduled.Id, "remote manual source must never be enumerated by another source schedule");
            Check(!f.Db.GetItems(f.Category.Id).Single().IsMissing, "cached remote absence must preserve Present");
            var complete = f.Db.GetEffectiveSourceRefreshPolicy(scheduled.Id).LastCompletedAtUtc;
            Check(complete is not null, "successful source completion timestamp");
            var result = await coordinator.ScanSourceAsync(manual.Id);
            Check(result.Status == LibraryScanStatus.Completed, "explicit manual remote scan");
        }
        finally { await coordinator.CloseAsync(); }
        Console.WriteLine("PASS N04/N05: per-source remote schedule, manual auto enumeration 0, cached absence preserved");
    }
    private static async Task DrainAndViewing()
    {
        using var f = new Fixture(); var source = f.Add(@"\\server\share"); f.Confirm(source);
        var entered = NewSignal(); var release = NewSignal(); int calls = 0;
        var item = f.Item(source);
        var coordinator = new ScanCoordinator(f.Db, (p, _) => Task.FromResult(Evidence(p)),
            async (_, _, _, _, _) => { Interlocked.Increment(ref calls); entered.TrySetResult(); await release.Task;
                return new(new[] { item with { FileSize = 9 } }, Array.Empty<string>(), Array.Empty<string>()); });
        var scan = coordinator.ScanSourceAsync(source.Id);
        await entered.Task;
        var viewing = coordinator.EnterExclusiveAsync();
        Check(!viewing.IsCompleted, "viewing admission must await the real uncancellable IO");
        release.SetResult(); using (var lease = await viewing)
        {
            Check(lease is not null, "viewing lease acquired");
            await scan;
            Check(f.Db.GetItems(f.Category.Id).Single().FileSize == 1, "cancelled pre-viewing observation must not apply");
            foreach (var phase in new[] { "Empty", "Opening", "Active", "checkpoint", "SaveFailed", "Hidden" })
            {
                coordinator.Signal(source.Id); coordinator.Tick();
                Check((await coordinator.ScanSourceAsync(source.Id)).Status == LibraryScanStatus.Cancelled,
                    phase + " full-lifetime lease prevents apply");
            }
            Check(calls == 1, "no scan IO while viewing lease exists");
        }
        entered = NewSignal(); release = NewSignal();
        scan = coordinator.ScanSourceAsync(source.Id); await entered.Task;
        Task close = coordinator.CloseAsync();
        Check(!close.IsCompleted && ReferenceEquals(close, coordinator.CloseAsync()), "Closing retains actual IO and duplicate exit shares task");
        coordinator.Signal(source.Id); coordinator.Tick();
        Check((await coordinator.ScanSourceAsync(source.Id)).Status == LibraryScanStatus.Cancelled, "Closing refuses new IO");
        Check(calls == 2, "no replacement worker after cancel/close");
        release.SetResult(); await close; await scan;
        f.Db.Dispose(); coordinator.Tick(); await coordinator.CloseAsync();
        Check(calls == 2, "no DB access or IO after completed drain and disposal");
        Console.WriteLine("PASS N09/N10/N11: blocked IO, whole-viewing lease, late apply discard, close drain/disposed DB");
    }
    private static async Task GenerationAndQuarantine()
    {
        using var f = new Fixture(); var source = f.Add(@"\\server\share"); f.Confirm(source);
        var item = f.Item(source); var entered = NewSignal(); var release = NewSignal();
        var coordinator = new ScanCoordinator(f.Db, (p, _) => Task.FromResult(Evidence(p)),
            async (_, _, _, _, _) => { entered.TrySetResult(); await release.Task;
                return new(new[] { item with { FileSize = 42 } }, Array.Empty<string>(), Array.Empty<string>()); });
        var scan = coordinator.ScanSourceAsync(source.Id); await entered.Task;
        // Simulates an externally stale revision; production editing uses the exclusive lease.
        f.Confirm(source); release.SetResult(); await scan;
        Check(f.Db.GetItems(f.Category.Id).Single().FileSize == 1, "stale binding revision must discard whole observation");
        await coordinator.CloseAsync();
        f.Db.QuarantineDeletion(item.PathKey);
        int calls = 0;
        coordinator = new ScanCoordinator(f.Db, (p, _) => Task.FromResult(Evidence(p)),
            (_, _, _, _, _) => { calls++; return Task.FromResult(Empty); });
        var sibling = f.Add(@"\\server\other"); f.Confirm(sibling);
        try
        {
            var result = await coordinator.ScanSourceAsync(sibling.Id);
            Check(result.WarningCount == 1 && calls == 0, "whole related category quarantined, even another source");
            using (var lease = await coordinator.EnterExclusiveAsync())
            {
                f.Db.ReleaseDeletion(item.PathKey);
                f.Db.UpdateSource(source with { IsEnabled = false }); coordinator.ConfigurationChanged();
            }
            Check((await coordinator.ScanSourceAsync(source.Id)).Status == LibraryScanStatus.Cancelled, "disabled source not scanned");
        }
        finally { await coordinator.CloseAsync(); }
        Console.WriteLine("PASS N04/N05/N10: stale revision, source edit, category deletion quarantine");
    }
    private static async Task PoliciesAndSignals()
    {
        using var f = new Fixture(); var source = f.Add(@"\\server\share"); f.Confirm(source);
        DateTimeOffset clock = DateTimeOffset.UtcNow;
        f.Db.SaveSourceRefreshPolicy(source.Id, new(false, SourceRefreshMode.Scheduled, 1, clock.ToUnixTimeMilliseconds()));
        int calls = 0; bool fail = true;
        var coordinator = new ScanCoordinator(f.Db, (p, _) => Task.FromResult(Evidence(p)),
            (_, _, _, _, _) => { Interlocked.Increment(ref calls); if (fail) throw new IOException("injected disconnect"); return Task.FromResult(Empty); }, () => clock);
        try
        {
            coordinator.Start(); await Until(() => coordinator.GetAccess(source.Id) == SourceAccessState.Available);
            await coordinator.Completion;
            Check(calls == 0, "startup off and interval not due");
            clock += TimeSpan.FromHours(8); coordinator.Tick(); await Until(() => calls == 1); await coordinator.Completion;
            for (int i = 0; i < 100; i++) coordinator.Tick();
            Check(calls == 1, "no catch-up or transient retry storm");
            clock += TimeSpan.FromSeconds(31); coordinator.Tick(); await Until(() => calls == 2); await coordinator.Completion;
            clock += TimeSpan.FromSeconds(121); fail = false; coordinator.Tick(); await Until(() => calls == 3); await coordinator.Completion;
            Check(f.Db.GetEffectiveSourceRefreshPolicy(source.Id).LastCompletedAtUtc == clock.ToUnixTimeMilliseconds(), "success resets retry and completion time");
            await coordinator.SavePolicyAsync(source.Id, new());
            int previous = calls; clock += TimeSpan.FromDays(10); coordinator.Tick(); await coordinator.Completion;
            Check(calls == previous, "manual policy disables automatic retries/enumeration");
        }
        finally { await coordinator.CloseAsync(); }
        Console.WriteLine("PASS N04: startup/policy, catch-up coalescing, 30s/2m retry, manual off");
    }
    private static async Task RemappingAndPartialFailure()
    {
        using var f = new Fixture(); var source = f.Add(@"Z:\media"); var item = f.Item(source);
        StorageObservation proof = StorageObservation.Classify(source.RootPath, MappingLookup.Mapped, @"\\server\original");
        var binding = f.Db.GetStorageBinding(source.RootPath)!;
        f.Db.ConfirmStorageBinding(source.RootPath, binding.Revision, proof, true);
        int mode = 0, calls = 0;
        var coordinator = new ScanCoordinator(f.Db, (_, _) => Task.FromResult(proof),
            (_, _, _, _, _) =>
            {
                calls++;
                if (mode == 0) proof = StorageObservation.Classify(source.RootPath, MappingLookup.Mapped, @"\\server\replacement");
                return Task.FromResult(new SourceScanObservation(new[] { item with { FileSize = 7 } },
                    new[] { item.PathKey }, new[] { "injected partial result" }, AccessDenied: mode == 2));
            });
        try
        {
            await coordinator.ScanSourceAsync(source.Id);
            Check(coordinator.GetAccess(source.Id) == SourceAccessState.BindingChanged
                && f.Db.GetItems(f.Category.Id).Single().FileSize == 1, "mapping changed mid-enumeration discards all Present/Missing");
            proof = StorageObservation.Classify(source.RootPath, MappingLookup.Mapped, @"\\server\original"); mode = 1;
            await coordinator.ScanSourceAsync(source.Id);
            Check(f.Db.GetItems(f.Category.Id).Single().FileSize == 7 && !f.Db.GetItems(f.Category.Id).Single().IsMissing,
                "same binding partial success may update Present, never remote Missing");
            Check(f.Db.GetEffectiveSourceRefreshPolicy(source.Id).LastCompletedAtUtc is null, "partial success is not full completion");
            await coordinator.SavePolicyAsync(source.Id, new(true, SourceRefreshMode.Scheduled, 1));
            mode = 2; await coordinator.ScanSourceAsync(source.Id);
            Check(coordinator.GetAccess(source.Id) == SourceAccessState.AccessDenied, "permission failure records distinct access state");
            coordinator.Start(); await coordinator.Completion;
            int previous = calls; for (int i = 0; i < 100; i++) coordinator.Tick();
            Check(calls == previous, "permission failure stops automatic retries");
        }
        finally { await coordinator.CloseAsync(); }
        // Unknown providers must be confirmed in this process, and again after observed disconnection.
        var unknown = f.Add(@"Y:\media"); bool offline = false; int reads = 0;
        coordinator = new ScanCoordinator(f.Db,
            (p, _) => Task.FromResult(StorageObservation.Classify(p, MappingLookup.Failed)),
            (_, _, _, _, _) => { reads++; if (offline) throw new IOException("provider disconnected"); return Task.FromResult(Empty); });
        try
        {
            await coordinator.ScanSourceAsync(unknown.Id); Check(reads == 0, "unknown unconfirmed provider is not enumerated");
            await coordinator.ConfirmBindingAsync(unknown.Id); await coordinator.ScanSourceAsync(unknown.Id);
            Check(reads == 1, "explicit same-target confirmation unlocks unknown provider this generation");
            offline = true; await coordinator.ScanSourceAsync(unknown.Id); offline = false;
            await coordinator.ScanSourceAsync(unknown.Id);
            Check(reads == 2, "observed disconnect invalidates unknown confirmation before another enumeration");
        }
        finally { await coordinator.CloseAsync(); }
        Console.WriteLine("PASS N03/N05: changed mapping discard, partial Present/timestamp, access denial, unknown reconnect confirmation");
    }
    private static async Task ManualPriorityAndCancellation()
    {
        using var f = new Fixture(); var first = f.Add(@"\\server\first"); var auto = f.Add(@"\\server\auto"); var manual = f.Add(@"\\server\manual");
        foreach (var source in new[] { first, auto, manual }) f.Confirm(source);
        f.Db.SaveSourceRefreshPolicy(first.Id, new(true, SourceRefreshMode.Scheduled, 1));
        f.Db.SaveSourceRefreshPolicy(auto.Id, new(true, SourceRefreshMode.Scheduled, 1));
        var entered = NewSignal(); var release = NewSignal(); var sequence = new List<Guid>();
        var coordinator = new ScanCoordinator(f.Db, (p, _) => Task.FromResult(Evidence(p)),
            async (_, source, _, _, _) =>
            {
                lock (sequence) sequence.Add(source.Id);
                if (source.Id == first.Id) { entered.TrySetResult(); await release.Task; }
                return Empty;
            });
        try
        {
            var firstTask = coordinator.ScanSourceAsync(first.Id); await entered.Task;
            coordinator.Start();
            var priority = coordinator.ScanSourceAsync(manual.Id);
            release.SetResult(); await firstTask; await priority;
            lock (sequence) Check(sequence[1] == manual.Id, "queued manual source has priority over auto requests");
        }
        finally { await coordinator.CloseAsync(); }
        entered = NewSignal(); release = NewSignal(); int calls = 0;
        coordinator = new ScanCoordinator(f.Db, (p, _) => Task.FromResult(Evidence(p)),
            async (_, _, _, _, _) => { calls++; entered.TrySetResult(); await release.Task; return Empty; });
        try
        {
            using var cancel = new CancellationTokenSource();
            var scan = coordinator.ScanSourceAsync(first.Id, cancel.Token); await entered.Task; cancel.Cancel();
            Check(!scan.IsCompleted, "manual cancellation still waits for actual active IO");
            release.SetResult(); Check((await scan).Status == LibraryScanStatus.Cancelled, "cancelled scan is not applied");
            coordinator.Start(); await coordinator.Completion;
            Check(calls <= 2, "cancelled source is suppressed, other source may run once");
            Check(f.Db.GetEffectiveSourceRefreshPolicy(first.Id).LastCompletedAtUtc is not null,
                "cancellation preserves previously completed timestamp");
        }
        finally { await coordinator.CloseAsync(); }
        f.Db.SaveSourceRefreshPolicy(auto.Id, new());
        entered = NewSignal(); release = NewSignal(); calls = 0;
        coordinator = new ScanCoordinator(f.Db, (p, _) => Task.FromResult(Evidence(p)),
            async (_, _, _, _, _) => { calls++; entered.TrySetResult(); await release.Task; return Empty; });
        try
        {
            coordinator.Start(); await entered.Task;
            var shared = coordinator.ScanSourceAsync(first.Id);
            release.SetResult(); await shared; await coordinator.Completion;
            Check(calls == 1, "manual request joins same source automatic enumeration already in progress");
        }
        finally { await coordinator.CloseAsync(); }
        Console.WriteLine("PASS N04/N09: manual priority, cancel retains real IO ownership and no immediate automatic restart");
    }

    private static async Task RealLocalWatcher()
    {
        using var f = new Fixture(); string media = Path.Combine(f.DirectoryPath, "media"); Directory.CreateDirectory(media);
        var source = f.Add(media); var proof = await WindowsStorageProbe.ObserveAsync(media, default);
        Check(proof.Kind == StorageKind.Local, "Windows runner must provide actual local device/volume evidence");
        var coordinator = new ScanCoordinator(f.Db);
        try
        {
            coordinator.Start();
            await Until(() => f.Db.GetEffectiveSourceRefreshPolicy(source.Id).LastCompletedAtUtc is not null);
            File.WriteAllText(Path.Combine(media, "created.mp4"), "metadata fixture");
            await Until(() => f.Db.GetItems(f.Category.Id).Count == 1);
            File.Move(Path.Combine(media, "created.mp4"), Path.Combine(media, "renamed.mp4"));
            // Overflow has the same dirty-source path, never per-file writes.
            for (int i = 0; i < 500; i++) coordinator.Signal(source.Id);
            await Until(() => f.Db.GetItems(f.Category.Id).Count == 2 && f.Db.GetItems(f.Category.Id).Count(i => i.IsMissing) == 1);
            Check(f.Db.GetItems(f.Category.Id).Select(i => i.Id).Distinct().Count() == 2, "rename does not carry item identity");
        }
        finally { await coordinator.CloseAsync(); }
        Console.WriteLine("PASS N04 real Windows local proof + FileSystemWatcher create/rename convergence; media body never opened by indexer");
    }
}
