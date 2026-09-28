using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using RandomMultimediaManager.App.ViewModels;

namespace RandomMultimediaManager.App.Views;

public partial class LibraryBrowserView : UserControl
{
    private readonly Action<ProcessStartInfo> startExplorer;
    private bool initializing;

    public LibraryBrowserView() : this(start => Process.Start(start)) { }

    public LibraryBrowserView(Action<ProcessStartInfo> startExplorer)
    {
        this.startExplorer = startExplorer;
        InitializeComponent();
    }

    public event Action<Guid>? ManualOpenRequested;
    public Task RefreshAsync() => DataContext is LibraryBrowserViewModel viewModel
        ? viewModel.RefreshAsync() : Task.CompletedTask;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (initializing || DataContext is not LibraryBrowserViewModel viewModel) return;
        initializing = true;
        try
        {
            await viewModel.LoadCategoriesAsync();
            await viewModel.LoadSelectedCategoryAsync();
        }
        finally { initializing = false; }
    }

    private async void CategorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || !IsLoaded || DataContext is not LibraryBrowserViewModel viewModel) return;
        await viewModel.LoadSelectedCategoryAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is LibraryBrowserViewModel viewModel) await viewModel.RefreshAsync();
    }

    private async void FileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is LibraryBrowserViewModel viewModel) await viewModel.LoadSelectedProgressAsync();
    }

    private void OpenManual_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is LibraryBrowserViewModel { SelectedItem: { CanOpen: true } selected })
            ManualOpenRequested?.Invoke(selected.Item.Id);
    }

    private void ShowInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LibraryBrowserViewModel { SelectedItem: { } selected } viewModel) return;
        if (selected.Item.IsMissing || !File.Exists(selected.Item.Path))
        {
            viewModel.ReportMessage("파일이 현재 경로에 없습니다. 위치 열기를 취소했습니다.");
            return;
        }
        try
        {
            var start = new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true
            };
            start.ArgumentList.Add($"/select,{selected.Item.Path}");
            startExplorer(start);
            viewModel.ReportMessage("탐색기에서 파일 위치를 열었습니다.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
        {
            viewModel.ReportMessage($"탐색기에서 파일 위치를 열지 못했습니다: {ex.Message}");
        }
    }
}
