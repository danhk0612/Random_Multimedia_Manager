using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using RandomMultimediaManager.App;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Deletion;
using RandomMultimediaManager.App.Lifecycle;
using RandomMultimediaManager.App.Scanning;
using RandomMultimediaManager.Core;

internal static class T18A2NativeVerification
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS T18A-2 WPF " + message); }
    private static async Task Until(Func<bool> predicate)
    { var deadline = DateTime.UtcNow.AddSeconds(20); while (!predicate()) { if (DateTime.UtcNow > deadline) throw new TimeoutException(); await Task.Delay(30); } }
    public static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "rmm-t18a2-wpf-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        using var db = LibraryDatabase.Open(Path.Combine(root, "library.db"));
        var category = new Category(Guid.NewGuid(), "remote", MediaType.Video); db.SaveCategory(category);
        var path = WindowsPath.Normalize(@"\\server\share");
        var source = db.AddSource(new(Guid.NewGuid(), category.Id, path.Path, path.PathKey));
        var evidence = StorageObservation.Classify(source.RootPath, MappingLookup.Failed);
        db.ConfirmStorageBinding(source.RootPath, 0, evidence, true);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0, exits = 0;
        var scans = new ScanCoordinator(db, (_, _) => Task.FromResult(evidence),
            async (_, _, _, _, _) => { calls++; entered.TrySetResult(); await release.Task;
                return new(Array.Empty<MediaItem>(), Array.Empty<string>(), Array.Empty<string>()); });
        var deletions = new DeletionService(db, new DeletionJournal(Path.Combine(root, "journal")),
            _ => throw new Exception("No OS deletion expected"));
        await deletions.InitializeAsync();
        var main = new MainWindow(db, deletions, scans);
        // Reuse real registered keys, and exercise the existing no-tray visible exit fallback.
        GlobalHotKeys? keys = null;
        var lifecycle = new AppLifecycle(main, db, deletions, () => false, () => keys?.HideRegistered == true,
            () => { }, () => exits++);
        main.Lifecycle = lifecycle;
        keys = new(lifecycle.ToggleHidden, () => _ = lifecycle.ExitAsync());
        try
        {
            main.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var scan = scans.ScanSourceAsync(source.Id); await entered.Task;
            var app = (VerificationApp)Application.Current;
            typeof(App).GetProperty(nameof(App.Lifecycle))!.SetValue(app, lifecycle);
            var ending = new SessionEndingCancelEventArgs(ReasonSessionEnding.Logoff);
            app.RaiseSessionEnding(ending);
            Check(ending.Cancel, "SessionEnding is synchronously deferred before shutdown");
            var exit = lifecycle.ExitAsync();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(!exit.IsCompleted && !db.IsDisposed && main.IsVisible, "stalled scan keeps DB alive and fallback window visible");
            Check(ReferenceEquals(exit, lifecycle.ExitAsync()), "duplicate exit shares original drain");
            lifecycle.Restore(); scans.Tick();
            Check((await scans.ScanSourceAsync(source.Id)).Status == LibraryScanStatus.Cancelled && calls == 1,
                "Closing/Restore admits zero additional scan IO");
            release.SetResult(); await scan;
            Check(await exit && db.IsDisposed && exits == 1, "DB disposal follows actual IO completion");
            scans.Tick(); Check((await scans.ScanSourceAsync(source.Id)).Status == LibraryScanStatus.Cancelled, "disposed DB cannot resume scans");
        }
        finally
        {
            release.TrySetResult(); await scans.CloseAsync(); keys.Dispose(); lifecycle.DisposePrivacy();
            typeof(App).GetProperty(nameof(App.Lifecycle))!.SetValue(Application.Current, null);
            main.Lifecycle = null; main.Close(); db.Dispose();
            Directory.Delete(root, true);
        }
    }
}
