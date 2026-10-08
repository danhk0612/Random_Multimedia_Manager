using System.Windows;
using RandomMultimediaManager.App.Data;

namespace RandomMultimediaManager.App;

public partial class App : Application
{
    private Lifecycle.TrayIcon? tray;
    private Lifecycle.GlobalHotKeys? keys;
    private int requestedExitCode;
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
            if (Deletions.GloballyBlocked) { await RequestShutdownAsync(1); return; }
            var main = new MainWindow(Database, Deletions);
            MainWindow = main;
            Lifecycle = new(main, Database, Deletions, () => tray?.EnsureAvailable() == true,
                () => keys?.HideRegistered == true, () => tray?.Dispose(), () => base.Shutdown(requestedExitCode));
            main.Lifecycle = Lifecycle;
            main.Show();
            main.Scans.Start();
            var unavailable = new List<string>();
            // Independent attempts: tray construction failure must not skip key registration.
            try
            {
                keys = new(Lifecycle.ToggleHidden, () => _ = Lifecycle.ExitAsync());
                if (!keys.HideRegistered) unavailable.Add("Ctrl+Shift+H 모두 숨기기/복원 등록 실패");
                if (!keys.ExitRegistered) unavailable.Add("Ctrl+Shift+Q 빠른 종료 등록 실패");
            }
            catch (Exception ex) { unavailable.Add("전역 키 등록 실패: " + ex.Message); }
            try
            {
                tray = new(Lifecycle.Restore, () => _ = Lifecycle.ExitAsync(), Lifecycle.TrayUnavailable,
                    Lifecycle.ToggleHidden, Lifecycle.Privacy, Lifecycle.QuickHideUnavailableReason);
            }
            catch (Exception ex) { unavailable.Add("트레이 생성 실패: " + ex.Message); }
            if (Lifecycle.QuickHideUnavailableReason() is { } reason) unavailable.Add(reason);
            main.ShowLifecycleError(unavailable.Count == 0
                ? "Ctrl+Shift+H 모두 숨기기/복원 · Ctrl+Shift+Q 빠른 종료"
                : string.Join(" · ", unavailable));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"데이터를 열 수 없습니다.\n{ex.Message}", "Random Multimedia Manager",
                MessageBoxButton.OK, MessageBoxImage.Error);
            await RequestShutdownAsync(1);
        }
    }

    // All app-controlled termination requests use this boundary. Only the lifecycle's
    // terminal callback calls WPF Shutdown after actual IO and resource drain.
    public Task<bool> RequestShutdownAsync(int exitCode = 0)
    {
        Dispatcher.VerifyAccess();
        if (exitCode != 0) requestedExitCode = exitCode;
        if (Lifecycle is not null) return Lifecycle.ExitAsync();
        // Startup recovery has not constructed/started the scan coordinator yet.
        base.Shutdown(requestedExitCode);
        return Task.FromResult(true);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (Lifecycle is null) return; // No scan IO before lifecycle creation.
        // WPF cannot await this OS notification. Defer synchronously, then reuse the
        // dispatcher-owned asynchronous exit, preserving ExitBlocked/retry/restore.
        e.Cancel = true;
        _ = RequestShutdownAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Normal exit already completed DB/tray/hook cleanup in AppLifecycle.
        // Keep the restoration key until all potentially failing cleanup is past.
        Database?.Dispose();
        tray?.Dispose();
        Lifecycle?.DisposePrivacy();
        keys?.Dispose();
        base.OnExit(e);
    }
}
