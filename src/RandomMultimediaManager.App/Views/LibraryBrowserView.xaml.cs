using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using RandomMultimediaManager.App.ViewModels;
using RandomMultimediaManager.App.Scanning;
using RandomMultimediaManager.App.Sessions;

namespace RandomMultimediaManager.App.Views;

public partial class LibraryBrowserView : UserControl
{
    private readonly Action<ProcessStartInfo> startExplorer;
    private bool initializing;
    private long explorerRequest;
    private bool explorerBusy;
    public ScanCoordinator? Scans { get; set; }
    public ViewingAccess? Access { get; set; }

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

    private void OnUnloaded(object sender, RoutedEventArgs e) => Interlocked.Increment(ref explorerRequest);

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
        if (DataContext is not LibraryBrowserViewModel viewModel) return;
        await viewModel.LoadSelectedProgressAsync();
        if (!IsLoaded) return;
        ManualOpenButton.IsEnabled = viewModel.CanOpenSelected;
        ExplorerButton.IsEnabled = viewModel.CanShowSelectedInExplorer && !explorerBusy;
    }

    private void OpenManual_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is LibraryBrowserViewModel { SelectedItem: { CanOpen: true } selected })
            ManualOpenRequested?.Invoke(selected.Item.Id);
    }

    private async void ShowInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LibraryBrowserViewModel { SelectedItem: { } selected } viewModel || explorerBusy) return;
        Task operation = ShowInExplorerAsync(viewModel, selected, Interlocked.Increment(ref explorerRequest));
        await viewModel.TrackOperation(operation);
    }

    private async Task ShowInExplorerAsync(LibraryBrowserViewModel viewModel,
        LibraryFileRow selected, long request)
    {
        IDisposable? admission = null;
        explorerBusy = true;
        ExplorerButton.IsEnabled = false;
        try
        {
            if (Scans is not null)
            {
                admission = await Scans.EnterExclusiveAsync();
                if (admission is null) throw new OperationCanceledException("종료 중이거나 다른 작업에서 접근 중입니다.");
            }
            var access = Access
                ?? throw new InvalidOperationException("연결 확인과 IO admission을 사용할 수 없습니다.");
            if (!IsCurrentRequest(request, viewModel, selected)) return;
            if (!await viewModel.IsCurrentItemExplorerEligibleAsync(selected))
                throw new IOException("파일이 삭제 격리 중이거나 DB 목록에서 변경되어 위치 열기를 취소했습니다.");

            // CheckAsync observes the binding and probes file access off the UI thread.
            var ticket = await Task.Run(() => access.CheckAsync(selected.Item.Path, CancellationToken.None));
            bool currentTicket = await Task.Run(() => access.RecheckAsync(selected.Item.Path, ticket, CancellationToken.None));
            bool eligible = await viewModel.IsCurrentItemExplorerEligibleAsync(selected);
            if (!currentTicket || !IsCurrentRequest(request, viewModel, selected) || !eligible)
                throw new IOException("연결 상태 또는 선택 항목이 바뀌어 위치 열기를 취소했습니다.");

            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            start.ArgumentList.Add($"/select,{selected.Item.Path}");
            startExplorer(start);
            viewModel.ReportMessage("현재 연결 대상과 접근을 확인하고 탐색기에서 위치를 열었습니다.");
        }
        catch (OperationCanceledException ex)
        {
            if (IsCurrentRequest(request, viewModel, selected))
                viewModel.ReportMessage($"탐색기 위치 열기를 취소했습니다: {ex.Message}");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException
            or UnauthorizedAccessException or NotSupportedException)
        {
            if (IsCurrentRequest(request, viewModel, selected))
                viewModel.ReportMessage($"탐색기에서 위치를 열지 못했습니다: {ex.Message}");
        }
        catch (IOException ex)
        {
            if (IsCurrentRequest(request, viewModel, selected)) viewModel.ReportMessage(ex.Message);
        }
        finally
        {
            admission?.Dispose();
            explorerBusy = false;
            if (IsLoaded && DataContext == viewModel)
                ExplorerButton.IsEnabled = viewModel.CanShowSelectedInExplorer;
        }
    }

    private bool IsCurrentRequest(long request, LibraryBrowserViewModel viewModel, LibraryFileRow selected) =>
        request == Volatile.Read(ref explorerRequest) && IsLoaded && DataContext == viewModel
        && ReferenceEquals(viewModel.SelectedItem, selected)
        && Scans?.IsClosing != true;
}
