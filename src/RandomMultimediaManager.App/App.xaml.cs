using System.Windows;
using RandomMultimediaManager.App.Data;

namespace RandomMultimediaManager.App;

public partial class App : Application
{
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
