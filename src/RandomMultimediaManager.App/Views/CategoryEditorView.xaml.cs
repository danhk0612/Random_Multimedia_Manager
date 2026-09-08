using System.Windows;
using System.Windows.Controls;
using RandomMultimediaManager.App.ViewModels;

namespace RandomMultimediaManager.App.Views;

public partial class CategoryEditorView : UserControl
{
    public CategoryEditorView() => InitializeComponent();

    private CategoryEditorViewModel ViewModel => (CategoryEditorViewModel)DataContext;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is null)
            DataContext = new CategoryEditorViewModel(((App)Application.Current).Database);
    }

    private void NewCategory_Click(object sender, RoutedEventArgs e) => ViewModel.BeginNewCategory();
    private void SaveCategory_Click(object sender, RoutedEventArgs e) => ViewModel.SaveCategory();
    private void NewSource_Click(object sender, RoutedEventArgs e) => ViewModel.BeginNewSource();
    private void SaveSource_Click(object sender, RoutedEventArgs e) => ViewModel.SaveSource();
    private void RemoveSource_Click(object sender, RoutedEventArgs e) => ViewModel.RemoveSelectedSource();
}
