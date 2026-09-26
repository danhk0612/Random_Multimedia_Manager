using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using RandomMultimediaManager.App;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Deletion;
using RandomMultimediaManager.App.Lifecycle;
using RandomMultimediaManager.App.Sessions;
using RandomMultimediaManager.App.ViewModels;
using RandomMultimediaManager.Core;
using SkiaSharp;

internal static class T15NativeVerification
{
    private static void Check(bool ok, string name)
    { if (!ok) throw new Exception(name); Console.WriteLine("PASS T15 " + name); }
    private static async Task Wait(Func<bool> predicate)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (!predicate()) { if (DateTime.UtcNow > until) throw new TimeoutException("T15 UI condition"); await Task.Delay(20); }
    }
    public static async Task Run()
    {
        await Scenario("main window and exit", async f =>
        {
            Check(f.Main.IsVisible, "startup visible");
            f.Main.WindowState = WindowState.Minimized;
            Check(f.Main.ShowInTaskbar && f.Lifecycle.State == LifecycleState.Visible, "ordinary minimize keeps taskbar");
            f.Lifecycle.Restore();
            f.Main.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(!f.Main.IsVisible && f.Lifecycle.State == LifecycleState.Hidden, "main X hides without closing");
            f.Lifecycle.Restore(); f.Lifecycle.Restore();
            Check(f.Main.IsVisible && f.Main.WindowState == WindowState.Normal, "repeat restore reuses main");
            f.TrayAvailable = false; f.Main.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(f.Main.IsVisible, "tray failure never strands hidden main");
            var first = f.Lifecycle.ExitAsync(); var second = f.Lifecycle.ExitAsync();
            Check(ReferenceEquals(first, second), "duplicate exit shares task");
            Check(await first && f.Exits == 1 && f.Removals == 1, "one DB/tray/exit completion");
        });
        await Scenario("active comic normal close", async f =>
        {
            var view = await f.OpenViewing();
            await view.Coordinator.OpenManualAsync(Guid.NewGuid(), f.Item.Id);
            var id = view.Coordinator.View.Pending!.VisitId;
            bool closed = false; view.Closed += (_, _) => closed = true;
            view.Close(); await Wait(() => closed);
            Check(f.Main.IsVisible && f.Db.GetHistory(f.Item.Id).Single().VisitId == id, "viewing X leaves visit, main stays open");
            Check(await f.Lifecycle.ExitAsync(), "exit after viewing X");
        });
        await Scenario("active tray exit", async f =>
        {
            var view = await f.OpenViewing();
            await view.Coordinator.OpenManualAsync(Guid.NewGuid(), f.Item.Id);
            var id = view.Coordinator.View.Pending!.VisitId;
            Check(await f.Lifecycle.ExitAsync(), "active exit saves and releases");
            using var read = LibraryDatabase.Open(f.DbPath);
            Check(read.GetHistory(f.Item.Id).Single().VisitId == id, "active exit durable visit once");
            using var file = File.Open(f.Item.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        });
        await Scenario("save failure recovery", async f =>
        {
            var view = await f.OpenViewing();
            await view.Coordinator.OpenManualAsync(Guid.NewGuid(), f.Item.Id);
            var pending = view.Coordinator.View.Pending;
            f.Sql("CREATE TRIGGER t15_fail BEFORE INSERT ON VisitCommit BEGIN SELECT RAISE(ABORT, 'injected'); END;");
            Check(!await f.Lifecycle.ExitAsync() && f.Lifecycle.State == LifecycleState.ExitBlocked, "save failure blocks app exit");
            Check(view.IsVisible && view.Coordinator.View.Pending == pending && f.Removals == 0,
                "blocked exit preserves Pending and restore path");
            Check(((Button)view.FindName("RetryButton")).IsEnabled && ((Button)view.FindName("ResumeButton")).IsEnabled,
                "existing retry/resume controls accessible");
            f.Sql("DROP TRIGGER t15_fail;");
            await view.Coordinator.ResumeAfterSaveFailureAsync(Guid.NewGuid());
            Check(await f.Lifecycle.ExitAsync(), "resolved save failure can exit");
        });
        await Scenario("viewing close retry completes original close", async f =>
        {
            var view = await f.OpenViewing();
            await view.Coordinator.OpenManualAsync(Guid.NewGuid(), f.Item.Id);
            f.Sql("CREATE TRIGGER t15_fail BEFORE INSERT ON VisitCommit BEGIN SELECT RAISE(ABORT, 'injected'); END;");
            Check(!await view.RequestCloseAsync(), "viewing close preserves failed save");
            f.Sql("DROP TRIGGER t15_fail;");
            bool closed = false; view.Closed += (_, _) => closed = true;
            ((Button)view.FindName("RetryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => closed);
            Check(f.Db.GetHistory(f.Item.Id).Count == 1, "retry saves once and completes original viewing close");
            Check(await f.Lifecycle.ExitAsync(), "app exit after viewing retry");
        });
        await Scenario("CommitUnknown preserves frozen visit", async f =>
        {
            var view = await f.OpenViewing();
            await view.Coordinator.OpenManualAsync(Guid.NewGuid(), f.Item.Id);
            var pending = view.Coordinator.View.Pending!;
            f.Sql($"INSERT INTO VisitCommit VALUES('{pending.VisitId:D}', '{f.Item.Id:D}', zeroblob(32));");
            Check(!await f.Lifecycle.ExitAsync() && view.Coordinator.View.FrozenCommit is not null,
                "conflicting commit acknowledgement blocks exit with frozen payload");
            Check((await view.Coordinator.ResumeAfterSaveFailureAsync(Guid.NewGuid())).Status == SessionStatus.CommitUnknown
                && view.Coordinator.View.Pending == pending && f.Removals == 0,
                "CommitUnknown cannot resume or discard visit");
            // Remove only the deliberately injected marker in this temporary DB.
            f.Sql($"DELETE FROM VisitCommit WHERE VisitId='{pending.VisitId:D}';");
            await view.Coordinator.RetryAsync(Guid.NewGuid());
            Check(await f.Lifecycle.ExitAsync(), "resolved acknowledgement follows original frozen transition");
        });
        await Scenario("modal delete confirmation exit", async f =>
        {
            var view = await f.OpenViewing();
            await view.Coordinator.OpenManualAsync(Guid.NewGuid(), f.Item.Id);
            _ = view.Dispatcher.BeginInvoke(new Action(() => ((Button)view.FindName("DeleteButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
            await Wait(() => view.OwnedWindows.OfType<DeleteConfirmationWindow>().Any());
            var exit = f.Lifecycle.ExitAsync();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(!exit.IsCompleted, "exit waits confirmation command published before modal pump");
            view.OwnedWindows.OfType<DeleteConfirmationWindow>().Single().Close();
            Check(await exit && f.OsCalls == 0, "cancelled confirmation exits without OS deletion");
        });
        await Scenario("opening exit", async f =>
        {
            var view = await f.OpenViewing();
            var opening = view.Coordinator.OpenManualAsync(Guid.NewGuid(), f.Item.Id);
            var exit = f.Lifecycle.ExitAsync();
            var result = await opening;
            Check(await exit && view.Coordinator.View.Pending is null, "exit awaits opening cancellation and media release");
            Check(result.Status == SessionStatus.Cancelled, "pending preparation cancelled on exit");
        });
        await Scenario("current Unknown", async f =>
        {
            var view = await f.OpenViewing();
            await view.Coordinator.OpenManualAsync(Guid.NewGuid(), f.Item.Id);
            var pending = view.Coordinator.View.Pending;
            f.Outcome = DeletionOutcome.Unknown;
            await view.Coordinator.DeleteCurrentAsync(Guid.NewGuid(), f.Service, DeletionMode.Recycle);
            Check(!await f.Lifecycle.ExitAsync() && view.Coordinator.View.Pending == pending,
                "current Unknown blocks Leave and preserves visit");
            Check(((Button)view.FindName("RecoveryButton")).IsEnabled, "deletion recovery UI accessible");
            await view.Coordinator.ResolveDeletionAsync(Guid.NewGuid(), f.Service, f.Service.Pending.Single(), false);
            Check(await f.Lifecycle.ExitAsync() && f.OsCalls == 1, "Unknown confirmation exits without repeating OS delete");
        });
        foreach (var outcome in new[] { DeletionOutcome.Succeeded, DeletionOutcome.Failed, DeletionOutcome.Cancelled })
            await Scenario("deleting " + outcome, async f =>
            {
                var view = await f.OpenViewing();
                await view.Coordinator.OpenManualAsync(Guid.NewGuid(), f.Item.Id);
                f.Outcome = outcome;
                f.DeleteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var deletion = view.Coordinator.DeleteCurrentAsync(Guid.NewGuid(), f.Service, DeletionMode.Recycle);
                await Wait(() => f.OsCalls == 1);
                var exit = f.Lifecycle.ExitAsync();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check(!exit.IsCompleted && f.Exits == 0, "exit waits entire deleting command " + outcome);
                f.DeleteGate.SetResult(); await deletion;
                Check(await exit && f.OsCalls == 1, "deleting exit completes once " + outcome);
            });
        await Scenario("Succeeded cleanup failure", async f =>
        {
            var view = await f.OpenViewing();
            await view.Coordinator.OpenManualAsync(Guid.NewGuid(), f.Item.Id);
            f.Journal.FailRemove = true;
            await view.Coordinator.DeleteCurrentAsync(Guid.NewGuid(), f.Service, DeletionMode.Recycle);
            Check(view.Coordinator.View.Pending is null && !await f.Lifecycle.ExitAsync(), "success tombstone alone cannot allow exit");
            f.Journal.FailRemove = false;
            await view.Coordinator.ResolveDeletionAsync(Guid.NewGuid(), f.Service, f.Service.Pending.Single(), true);
            Check(await f.Lifecycle.ExitAsync() && f.OsCalls == 1, "cleanup retry does not delete twice");
        });
        await Scenario("unrelated durable Unknown", async f =>
        {
            var record = await f.Service.PrepareAsync(f.Item.PathKey, DeletionMode.Recycle);
            await f.Service.RecordResultAsync(record, new(DeletionOutcome.Unknown));
            Check(await f.Lifecycle.ExitAsync() && File.Exists(f.Journal.FileFor(record)), "unrelated durable recovery survives exit");
        });
        await Scenario("scan exit", async f =>
        {
            var editor = (CategoryEditorViewModel)((FrameworkElement)f.Main.FindName("CategoryEditor")).DataContext;
            editor.SelectedCategory = editor.Categories.Single();
            var scan = editor.ScanSelectedCategoryAsync();
            Check(editor.IsScanning, "scan entered before exit");
            Check(await f.Lifecycle.ExitAsync() && scan.IsCompleted && !editor.IsScanning,
                "exit cancels and awaits scan before DB disposal");
        });
        // The shell may be absent on unattended runners; report that separately, never as a pass.
        int restored = 0, exited = 0;
        using var tray = new TrayIcon(() => restored++, () => exited++, () => { });
        if (tray.Available) Check(tray.EnsureAvailable(), "native Shell_NotifyIcon add/modify");
        else Console.WriteLine("UNVERIFIED T15 shell icon display: no notification area on runner");
        var fields = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var menu = (ContextMenu)typeof(TrayIcon).GetField("menu", fields)!.GetValue(tray)!;
        Check(menu.Items.OfType<MenuItem>().Single(i => i.Header.ToString()!.Contains("미구현")).IsEnabled == false,
            "T16 menu remains disabled");
        menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "열기/복원"))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var source = (System.Windows.Interop.HwndSource)typeof(TrayIcon).GetField("source", fields)!.GetValue(tray)!;
        SendMessage(source.Handle, 0x8001, IntPtr.Zero, (IntPtr)((1 << 16) | 0x203));
        menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "종료"))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(restored == 2 && exited == 1, "tray menu and native double-click use registered actions");
        tray.Dispose();
        Check(!tray.Available && !tray.EnsureAvailable(), "tray disposal is idempotent and cannot re-add");
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    private static async Task Scenario(string name, Func<Fixture, Task> test)
    {
        using var f = new Fixture();
        f.Main.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await test(f);
        Console.WriteLine("PASS T15 scenario " + name);
    }
    private sealed class FaultJournal(string path) : DeletionJournal(path)
    {
        public bool FailRemove;
        public override void Remove(string path) { if (FailRemove) throw new IOException("injected cleanup failure"); base.Remove(path); }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "rmm-t15-ui-" + Guid.NewGuid());
        public string DbPath => Path.Combine(root, "test.db");
        public LibraryDatabase Db;
        public MainWindow Main;
        public AppLifecycle Lifecycle;
        public FaultJournal Journal;
        public DeletionService Service;
        public MediaItem Item;
        public int Exits, Removals, OsCalls;
        public bool TrayAvailable = true;
        public DeletionOutcome Outcome = DeletionOutcome.Succeeded;
        public TaskCompletionSource? DeleteGate;
        public Fixture()
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "test.cbz");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var bitmap = new SKBitmap(30, 40))
            {
                bitmap.Erase(SKColors.Navy);
                using var image = SKImage.FromBitmap(bitmap);
                using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                using var entry = zip.CreateEntry("1.png").Open(); png.SaveTo(entry);
            }
            Db = LibraryDatabase.Open(DbPath);
            var category = new Category(Guid.NewGuid(), "comic", MediaType.Comic);
            Db.SaveCategory(category);
            Db.AddSource(new(Guid.NewGuid(), category.Id, root, root.ToUpperInvariant()));
            var file = new FileInfo(path);
            Item = new(Guid.NewGuid(), category.Id, MediaType.Comic, path, path.ToUpperInvariant(), file.Length, file.LastWriteTimeUtc.Ticks);
            Db.ApplyObservedItems([Item], []);
            Journal = new(Path.Combine(root, "journal"));
            Service = new(Db, Journal, async _ => { OsCalls++; if (DeleteGate is not null) await DeleteGate.Task; return new(Outcome); });
            Main = new(Db, Service);
            Lifecycle = new(Main, Db, Service, () => TrayAvailable, () => Removals++, () => Exits++);
            Main.Lifecycle = Lifecycle;
        }
        public async Task<RandomMultimediaManager.App.Viewing.ViewingWindow> OpenViewing()
        {
            _ = Main.Dispatcher.BeginInvoke(new Action(() => Main.GetType().GetMethod("OpenViewing",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(Main, [Main, new RoutedEventArgs()])));
            await Wait(() => Main.Viewing is not null && ((ListBox)Main.Viewing.FindName("Categories")).Items.Count != 0
                && ((FrameworkElement)Main.Viewing.FindName("SessionControls")).IsEnabled);
            return Main.Viewing!;
        }
        public void Sql(string sql)
        {
            using var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False"); connection.Open();
            using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
        }
        public void Dispose()
        {
            Main.Lifecycle = null; Main.Close(); Db.Dispose();
            Directory.Delete(root, true);
        }
    }
}
