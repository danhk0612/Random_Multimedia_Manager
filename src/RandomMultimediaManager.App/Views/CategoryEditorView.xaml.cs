using System.Windows;
using System.Windows.Controls;
using RandomMultimediaManager.App.Scanning;
using RandomMultimediaManager.App.ViewModels;

namespace RandomMultimediaManager.App.Views;

public partial class CategoryEditorView : UserControl
{
    private Action<Guid, SourceAccessState>? accessChanged;
    public CategoryEditorView() => InitializeComponent();

    private CategoryEditorViewModel ViewModel => (CategoryEditorViewModel)DataContext;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is null)
            DataContext = new CategoryEditorViewModel(((App)Application.Current).Database);
        ViewModel.SetUiAttached(true);
        if (accessChanged is null && ViewModel.Scans is { } scans)
        {
            accessChanged = (sourceId, _) =>
            {
                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (IsLoaded && ViewModel.SelectedSource?.Id == sourceId)
                        ViewModel.RefreshSelectedSourceStatus();
                }));
            };
            scans.SourceAccessChanged += accessChanged;
            ViewModel.RefreshSelectedSourceStatus();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.SetUiAttached(false);
        if (accessChanged is not { } handler || ViewModel.Scans is not { } scans) return;
        scans.SourceAccessChanged -= handler;
        accessChanged = null;
    }

    private void NewCategory_Click(object sender, RoutedEventArgs e) => ViewModel.BeginNewCategory();
    private async void SaveCategory_Click(object sender, RoutedEventArgs e) => await ViewModel.EditAsync(ViewModel.SaveCategory);
    private void NewSource_Click(object sender, RoutedEventArgs e) => ViewModel.BeginNewSource();
    private async void SaveSource_Click(object sender, RoutedEventArgs e) => await ViewModel.EditAsync(ViewModel.SaveSource);
    private async void RemoveSource_Click(object sender, RoutedEventArgs e) => await ViewModel.EditAsync(ViewModel.RemoveSelectedSource);
    private async void SavePolicy_Click(object sender, RoutedEventArgs e) => await ViewModel.SaveSelectedSourcePolicyAsync();
    private async void ConfirmBinding_Click(object sender, RoutedEventArgs e) => await ViewModel.ConfirmSelectedBindingAsync();
    private async void ScanCategory_Click(object sender, RoutedEventArgs e) => await ViewModel.ScanSelectedCategoryAsync();
    private void CancelScan_Click(object sender, RoutedEventArgs e) => ViewModel.CancelScan();
}
