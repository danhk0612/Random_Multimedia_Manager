using System.ComponentModel;
using System.IO;
using System.Windows;

namespace RandomMultimediaManager.App;

public partial class MainWindow : Window
{
    private Media.Comic.ComicViewerWindow? comicViewer;
    private Video.VideoValidationWindow? videoValidation;
    public Viewing.ViewingWindow? Viewing { get; private set; }
    public Lifecycle.AppLifecycle? Lifecycle { get; set; }
    private readonly Data.LibraryDatabase database;
    private readonly Deletion.DeletionService deletions;
    public Scanning.ScanCoordinator Scans { get; }
    private bool enteringViewing;
    private bool exitRequested;
    private Task recovery = Task.CompletedTask;

    private static string ComicTracePath => Path.Combine(Path.GetTempPath(), "RandomMultimediaManager-T08-startup.log");

    public MainWindow() : this(((App)Application.Current).Database, ((App)Application.Current).Deletions) { }

    public MainWindow(Data.LibraryDatabase database, Deletion.DeletionService deletions, Scanning.ScanCoordinator? scans = null)
    {
        this.database = database; this.deletions = deletions;
        Scans = scans ?? new(database);
        deletions.ConfirmedGeneration = Scans.ConfirmedDeletionGeneration;
        InitializeComponent();
        CategoryEditor.DataContext = new ViewModels.CategoryEditorViewModel(database, Scans);
        LibraryBrowser.DataContext = new ViewModels.LibraryBrowserViewModel(database);
        LibraryBrowser.ManualOpenRequested += OpenManualItem;
    }

    private static void TraceComicStartup(string message)
    {
        try
        {
            File.AppendAllText(ComicTracePath, $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostic logging must never affect application behavior.
        }
    }

    private void OpenComicViewer(object sender, RoutedEventArgs e)
    {
        if (exitRequested || Lifecycle?.BlocksNewCommands == true || Scans.IsClosing) return;
        TraceComicStartup("OpenComicViewer entered");

        if (comicViewer is not null)
        {
            TraceComicStartup("Existing viewer: before Activate");
            comicViewer.Activate();
            TraceComicStartup("Existing viewer: after Activate");
            return;
        }

        try
        {
            TraceComicStartup("Before ComicViewerWindow constructor");
            var nextViewer = new Media.Comic.ComicViewerWindow();
            TraceComicStartup("After ComicViewerWindow constructor");

            nextViewer.Owner = this;
            TraceComicStartup("After Owner assignment");

            nextViewer.Closed += (_, _) =>
            {
                TraceComicStartup("ComicViewerWindow Closed event");
                comicViewer = null;
            };
            comicViewer = nextViewer;

            TraceComicStartup("Before ComicViewerWindow.Show");
            nextViewer.Show();
            TraceComicStartup("After ComicViewerWindow.Show");
        }
        catch (Exception ex)
        {
            TraceComicStartup($"Managed exception: {ex}");
            comicViewer = null;
            MessageBox.Show(this, ex.ToString(), "만화 뷰어 시작 실패");
        }
    }

    private async void OpenViewing(object sender, RoutedEventArgs e) => await OpenViewingAsync(null);
    private async void OpenManualItem(Guid itemId) => await OpenViewingAsync(itemId);

    private async Task OpenViewingAsync(Guid? itemId)
    {
        if (exitRequested || Lifecycle?.BlocksNewCommands == true || Scans.IsClosing || enteringViewing) return;
        if (Viewing is not null) { Viewing.Activate(); return; }
        enteringViewing = true;
        try
        {
            if (!Scans.Completion.IsCompleted) ShowLifecycleError("진행 중인 스캔 IO 완료를 기다리고 있습니다.");
            using var admission = await Scans.EnterExclusiveAsync();
            if (admission is null || exitRequested || Scans.IsClosing || Lifecycle?.BlocksNewCommands == true) return;
            Viewing = new Viewing.ViewingWindow(database, deletions, itemId,
                new Sessions.ViewingAccess(database, Scans.ConfirmedDeletionGeneration, () => Scans.IsClosing)) { Owner = this };
            try { Viewing.ShowDialog(); }
            finally { Viewing = null; }
        }
        catch (Exception ex) { ShowLifecycleError(ex.Message); }
        finally { enteringViewing = false; }
        if (!exitRequested && !Scans.IsClosing) await LibraryBrowser.RefreshAsync();
    }

    private void OpenVideoValidation(object sender, RoutedEventArgs e)
    {
        if (exitRequested || Lifecycle?.BlocksNewCommands == true || Scans.IsClosing) return;
        if (videoValidation is not null) { videoValidation.Activate(); return; }
        videoValidation = new Video.VideoValidationWindow { Owner = this };
        videoValidation.Closed += (_, _) => videoValidation = null;
        videoValidation.Show();
    }

    public void SetExitRequested(bool value)
    {
        exitRequested = value;
        if (value) { deletions.BeginClosing(); _ = Scans.CloseAsync(); }
        MainContent.IsEnabled = !value;
        if (LibraryBrowser.DataContext is ViewModels.LibraryBrowserViewModel browser)
            browser.SetExitRequested(value || Scans.IsClosing);
        CategoryEditor.IsEnabled = LibraryBrowser.IsEnabled = !value && !Scans.IsClosing;
        if (value) Viewing?.BeginApplicationClosing();
        else Viewing?.SetExitRequested(false);
        if (comicViewer is not null) comicViewer.IsEnabled = !value && !Scans.IsClosing;
        if (videoValidation is not null) videoValidation.IsEnabled = !value && !Scans.IsClosing;
    }
    public async Task StopScanningAsync()
    {
        if (!Scans.Completion.IsCompleted) ShowLifecycleError("종료 준비: 진행 중인 IO의 실제 완료를 기다립니다. 복원 키/트레이를 사용할 수 있습니다.");
        await Scans.CloseAsync();
        await deletions.DrainAsync();
        if (CategoryEditor.DataContext is ViewModels.CategoryEditorViewModel editor)
        {
            editor.CancelScan();
            await editor.ScanCompletion;
        }
        await recovery;
        if (LibraryBrowser.DataContext is ViewModels.LibraryBrowserViewModel browser)
            await browser.WaitForPendingReadsAsync();
    }
    public async Task CloseAuxiliaryWindowsAsync()
    {
        if (comicViewer is { } comic) await comic.ShutdownAsync();
        if (videoValidation is { } video) await video.ShutdownAsync();
    }
    public void ShowLifecycleError(string message) => LifecycleStatus.Text = message;
    private async void ExitApplication(object sender, RoutedEventArgs e)
    { if (Lifecycle is not null) await Lifecycle.ExitAsync(); }
    private async void RecoverDeletion(object sender, RoutedEventArgs e)
    {
        if (exitRequested || Lifecycle?.BlocksNewCommands == true || !recovery.IsCompleted) return;
        async Task Recover()
        {
            await System.Windows.Threading.Dispatcher.Yield();
            if (exitRequested || Lifecycle?.BlocksNewCommands == true) return;
            if (Scans.IsClosing)
            {
                await Scans.CloseAsync();
                if (!database.IsDisposed) await Deletion.DeletionDialogs.RecoverAsync(deletions, this);
            }
            else
            {
                using var admission = await Scans.EnterExclusiveAsync();
                if (admission is not null) await Deletion.DeletionDialogs.RecoverAsync(deletions, this);
            }
        }
        recovery = Recover();
        try { await recovery; }
        catch (Exception ex) { ShowLifecycleError(ex.Message); }
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (Lifecycle is { State: not RandomMultimediaManager.App.Lifecycle.LifecycleState.Exited })
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke(new Action(Lifecycle.HideMain));
        }
        base.OnClosing(e);
    }
}
