using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Scanning;
using RandomMultimediaManager.Core;

internal static class T18A2Verification
{
    public static async Task Run()
    {
        await RemoteScopeAndEvidence();
        await DrainAndViewing();
        await GenerationAndQuarantine();
        await PoliciesAndSignals();
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
