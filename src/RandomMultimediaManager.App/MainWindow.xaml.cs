using System.ComponentModel;
using System.IO;
using System.Windows;

namespace RandomMultimediaManager.App;

public partial class MainWindow : Window
{
    private Media.Comic.ComicViewerWindow? comicViewer;
    private Video.VideoValidationWindow? videoValidation;
    private bool waitingForVideoClose;

    private static string ComicTracePath => Path.Combine(Path.GetTempPath(), "RandomMultimediaManager-T08-startup.log");

    public MainWindow()
    {
        InitializeComponent();
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
        if (CategoryEditor.DataContext is ViewModels.CategoryEditorViewModel { IsScanning: true })
        {
            MessageBox.Show(this, "진행 중인 스캔이 끝난 뒤 감상을 시작하세요.", "랜덤 감상");
            return;
        }
        // A modal owner keeps scan application after the final visit/checkpoint boundary.
        // No scan can invalidate progress while the session is using that observed item.
        new Viewing.ViewingWindow(((App)Application.Current).Database) { Owner = this }.ShowDialog();
    }

    private void OpenVideoValidation(object sender, RoutedEventArgs e)
    {
        if (videoValidation is not null) { videoValidation.Activate(); return; }
        videoValidation = new Video.VideoValidationWindow { Owner = this };
        videoValidation.Closed += (_, _) => videoValidation = null;
        videoValidation.Show();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        TraceComicStartup("MainWindow OnClosing entered");
        comicViewer?.Close();

        if (videoValidation is not null)
        {
            e.Cancel = true;
            base.OnClosing(e);
            if (waitingForVideoClose) return;
            waitingForVideoClose = true;
            try { await videoValidation.ShutdownAsync(); Close(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "영상 리소스 해제 실패"); }
            finally { waitingForVideoClose = false; }
            return;
        }
        base.OnClosing(e);
    }
}
