using System.Windows;
using RandomMultimediaManager.App.Data;

namespace RandomMultimediaManager.App;

public partial class App : Application
{
    public LibraryDatabase Database { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            Database = await Task.Run(() => LibraryDatabase.Open());
            MainWindow = new MainWindow();
            MainWindow.Show();
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
        Database?.Dispose();
        base.OnExit(e);
    }
}
