using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.IO;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Viewing;
using RandomMultimediaManager.App.ViewModels;
using RandomMultimediaManager.App.Views;
using RandomMultimediaManager.Core;

internal static class T17LibraryBrowserVerification
{
    public static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "rmm-t17-ui-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            using var database = LibraryDatabase.Open(Path.Combine(root, "library.db"));
            var comicCategory = new Category(Guid.NewGuid(), "A Comics", MediaType.Comic);
            var videoCategory = new Category(Guid.NewGuid(), "B Videos", MediaType.Video);
            database.SaveCategory(comicCategory);
            database.SaveCategory(videoCategory);
            MediaItem Add(string name, Category category, bool favorite)
            {
                string path = Path.Combine(root, name);
                File.WriteAllBytes(path, [1, 2, 3]);
                var file = new FileInfo(path);
                var item = new MediaItem(Guid.NewGuid(), category.Id, category.MediaType,
                    file.FullName, file.FullName.ToUpperInvariant(), file.Length, file.LastWriteTimeUtc.Ticks,
                    favorite);
                database.ApplyObservedItems([item], []);
                return item;
            }
            var comic = Add("comic.cbz", comicCategory, false);
            var video = Add("한글 video 파일.mp4", videoCategory, true);
            var staleVideo = Add("stale selection.mp4", videoCategory, false);
            database.CommitVisit(new(Guid.NewGuid(), video.Id, 1000, VisitOrigin.Manual,
                false, PlaybackProgress.Video(65_000)));

            var viewModel = new LibraryBrowserViewModel(database);
            ProcessStartInfo? explorerStart = null;
            var delayedAccessStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseDelayedAccess = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var browser = new LibraryBrowserView(start => explorerStart = start)
            {
                DataContext = viewModel,
                Access = new ViewingAccess(database, observe: (path, token) => Task.FromResult(
                    StorageObservation.Classify(path, MappingLookup.NotMapped, localDriveType: true,
                        device: "test-volume", volume: "00000001")),
                    accessible: async (path, token) =>
                    {
                        if (path == staleVideo.Path)
                        {
                            delayedAccessStarted.TrySetResult();
                            await releaseDelayedAccess.Task.WaitAsync(token);
                        }
                        _ = await Task.Run(() => File.GetAttributes(path), token);
                    })
            };
            Guid? requestedItemId = null;
            browser.ManualOpenRequested += id => requestedItemId = id;
            var host = new Window { Content = browser, Width = 920, Height = 600 };
            host.Show();
            await Wait(() => viewModel.VisibleItems.Count == 1
                && viewModel.VisibleItems[0].Item.Id == comic.Id);

            var categoryPicker = (ComboBox)browser.FindName("CategoryPicker");
            categoryPicker.SelectedItem = viewModel.Categories.Single(category => category.Id == videoCategory.Id);
            await Wait(() => viewModel.VisibleItems.Count == 2
                && viewModel.VisibleItems.Any(item => item.Item.Id == video.Id));
            var fileList = (ListBox)browser.FindName("FileList");
            fileList.SelectedItem = viewModel.VisibleItems.Single(item => item.Item.Id == video.Id);
            await Wait(() => viewModel.SelectedItem?.Item.Id == video.Id, "T17 WPF selected-item binding");
            Check(viewModel.CanOpenSelected, "T17 WPF selected item permits opening");
            var openButton = (Button)FindButton(browser, "감상 열기");
            Check(ReferenceEquals(openButton.DataContext, viewModel), "T17 WPF open button inherits browser data context");
            await Wait(() => openButton.IsEnabled,
                "T17 WPF open button binding");
            await Wait(() => viewModel.SelectedProgressText == "00:01:05");
            Check(viewModel.SelectedItem!.FileName == "한글 video 파일.mp4"
                && viewModel.SelectedItem.LastViewedAtUtc == 1000
                && viewModel.SelectedItem.CategoryName == videoCategory.Name,
                "T17 WPF browser shows selected item information");
            ((Button)FindButton(browser, "감상 열기")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(requestedItemId == video.Id, "T17 WPF manual-open command forwards the selected ItemId");
            ((Button)FindButton(browser, "탐색기에서 위치 열기")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => explorerStart is not null, "T18A-5 async Explorer launch");
            Check(explorerStart is { FileName: "explorer.exe" } explorerCommand
                && explorerCommand.ArgumentList.SequenceEqual([$"/select,{video.Path}"]),
                "T18A-5 Explorer selection preserves Korean and spaces in one argument after binding verification");

            explorerStart = null;
            fileList.SelectedItem = viewModel.VisibleItems.Single(item => item.Item.Id == staleVideo.Id);
            await Wait(() => viewModel.SelectedItem?.Item.Id == staleVideo.Id);
            ((Button)FindButton(browser, "탐색기에서 위치 열기")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await delayedAccessStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            fileList.SelectedItem = viewModel.VisibleItems.Single(item => item.Item.Id == video.Id);
            await Wait(() => viewModel.SelectedItem?.Item.Id == video.Id);
            releaseDelayedAccess.TrySetResult();
            await viewModel.WaitForPendingReadsAsync();
            Check(explorerStart is null,
                "T18A-5 delayed Explorer result does not act on a selection that changed during access verification");

            File.Delete(video.Path);
            explorerStart = null;
            viewModel.ReportMessage(string.Empty);
            ((Button)FindButton(browser, "탐색기에서 위치 열기")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => !string.IsNullOrEmpty(viewModel.StatusMessage));
            Check(explorerStart is null, "T18A-5 Explorer refuses a file removed after DB indexing");

            viewModel.SetExitRequested(true);
            await viewModel.WaitForPendingReadsAsync();
            host.Close();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static DependencyObject FindButton(DependencyObject root, string label)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is Button button && Equals(button.Content, label)) return button;
            if (child is DependencyObject node)
            {
                try { return FindButton(node, label); }
                catch (InvalidOperationException) { }
            }
        }
        throw new InvalidOperationException($"Button not found: {label}");
    }

    private static async Task Wait(Func<bool> condition, string failure = "T17 library browser UI condition")
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(failure);
            await Task.Delay(50);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}
