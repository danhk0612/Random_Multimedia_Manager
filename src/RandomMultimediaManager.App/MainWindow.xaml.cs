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
    private bool exitRequested;
    private Task recovery = Task.CompletedTask;

    private static string ComicTracePath => Path.Combine(Path.GetTempPath(), "RandomMultimediaManager-T08-startup.log");

    public MainWindow() : this(((App)Application.Current).Database, ((App)Application.Current).Deletions) { }

    public MainWindow(Data.LibraryDatabase database, Deletion.DeletionService deletions)
    {
        this.database = database; this.deletions = deletions;
        InitializeComponent();
        CategoryEditor.DataContext = new ViewModels.CategoryEditorViewModel(database);
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
        if (exitRequested) return;
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

    private void OpenViewing(object sender, RoutedEventArgs e)
    {
        if (exitRequested) return;
        if (Viewing is not null) { Viewing.Activate(); return; }
        if (CategoryEditor.DataContext is ViewModels.CategoryEditorViewModel { IsScanning: true })
        {
            MessageBox.Show(this, "진행 중인 스캔이 끝난 뒤 감상을 시작하세요.", "랜덤 감상");
            return;
        }
        // A modal owner keeps scan application after the final visit/checkpoint boundary.
        // No scan can invalidate progress while the session is using that observed item.
        Viewing = new(database, deletions) { Owner = this };
        try { Viewing.ShowDialog(); }
        finally { Viewing = null; }
    }

    private void OpenVideoValidation(object sender, RoutedEventArgs e)
    {
        if (exitRequested) return;
        if (videoValidation is not null) { videoValidation.Activate(); return; }
        videoValidation = new Video.VideoValidationWindow { Owner = this };
        videoValidation.Closed += (_, _) => videoValidation = null;
        videoValidation.Show();
    }

    public void SetExitRequested(bool value)
    {
        exitRequested = value;
        MainContent.IsEnabled = !value;
        Viewing?.SetExitRequested(value);
        if (comicViewer is not null) comicViewer.IsEnabled = !value;
        if (videoValidation is not null) videoValidation.IsEnabled = !value;
    }
    public async Task StopScanningAsync()
    {
        if (CategoryEditor.DataContext is ViewModels.CategoryEditorViewModel editor)
        {
            editor.CancelScan();
            await editor.ScanCompletion;
        }
        await recovery;
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
        if (exitRequested || !recovery.IsCompleted) return;
        async Task Recover()
        {
            await System.Windows.Threading.Dispatcher.Yield();
            if (exitRequested) return;
            await Deletion.DeletionDialogs.RecoverAsync(deletions, this);
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
