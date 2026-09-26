using System.Windows;
using RandomMultimediaManager.App.Data;

namespace RandomMultimediaManager.App;

public partial class App : Application
{
    private Lifecycle.TrayIcon? tray;
    private Lifecycle.GlobalHotKeys? keys;
    public Lifecycle.AppLifecycle? Lifecycle { get; private set; }
    public Deletion.DeletionService Deletions { get; private set; } = null!;
    public LibraryDatabase Database { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            Database = await Task.Run(() => LibraryDatabase.Open());
            Deletions = new(Database, new Deletion.DeletionJournal(System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(LibraryDatabase.DefaultPath)!, "delete-journal")), Deletion.WindowsFileDeletion.DeleteAsync);
            await Deletions.InitializeAsync();
            await Deletion.DeletionDialogs.RecoverAsync(Deletions);
            if (Deletions.GloballyBlocked) { Shutdown(1); return; }
            var main = new MainWindow(Database, Deletions);
            MainWindow = main;
            Lifecycle = new(main, Database, Deletions, () => tray?.EnsureAvailable() == true,
                () => { keys?.Dispose(); tray?.Dispose(); }, () => Shutdown());
            main.Lifecycle = Lifecycle;
            main.Show();
            try
            {
                tray = new(Lifecycle.Restore, () => _ = Lifecycle.ExitAsync(), Lifecycle.TrayUnavailable,
                    Lifecycle.ToggleHidden, Lifecycle.Privacy);
                keys = new(Lifecycle.ToggleHidden, () => _ = Lifecycle.ExitAsync());
                var unavailable = new List<string>();
                if (!keys.HideRegistered) unavailable.Add("Ctrl+Shift+H 모두 숨기기/복원");
                if (!keys.ExitRegistered) unavailable.Add("Ctrl+Shift+Q 빠른 종료");
                main.ShowLifecycleError(unavailable.Count == 0
                    ? "Ctrl+Shift+H 모두 숨기기/복원 · Ctrl+Shift+Q 빠른 종료"
                    : "전역 키 등록 실패 (해당 키 비활성): " + string.Join(", ", unavailable));
            }
            catch (Exception ex) { main.ShowLifecycleError("트레이 생성 실패: " + ex.Message); }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"데이터를 열 수 없습니다.\n{ex.Message}", "Random Multimedia Manager",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        keys?.Dispose();
        tray?.Dispose();
        Lifecycle?.DisposePrivacy();
        Database?.Dispose();
        base.OnExit(e);
    }
}
