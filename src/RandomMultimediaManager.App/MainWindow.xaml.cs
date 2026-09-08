using System.Windows;
using System.ComponentModel;

namespace RandomMultimediaManager.App;

public partial class MainWindow : Window
{
    private Video.VideoValidationWindow? videoValidation;
    private bool waitingForVideoClose;
    public MainWindow()
    {
        InitializeComponent();
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
