using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Deletion;
using RandomMultimediaManager.App.Lifecycle;
using RandomMultimediaManager.App.Sessions;
using RandomMultimediaManager.App.Viewing;
using RandomMultimediaManager.Core;
using SkiaSharp;

internal static class T13ViewingShortcutsVerification
{
    public static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "rmm-t13-shortcuts-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        ViewingWindow window = null!;
        PrivacyWindows? privacy = null;
        try
        {
            using var database = LibraryDatabase.Open(Path.Combine(root, "library.db"));
            int deleteCalls = 0;
            var comicCategory = new Category(Guid.NewGuid(), "T13 Comics", MediaType.Comic);
            var videoCategory = new Category(Guid.NewGuid(), "T13 Videos", MediaType.Video);
            database.SaveCategory(comicCategory);
            database.SaveCategory(videoCategory);

            MediaItem AddItem(Category category, string path)
            {
                var file = new FileInfo(path);
                var item = new MediaItem(Guid.NewGuid(), category.Id, category.MediaType,
                    file.FullName, file.FullName.ToUpperInvariant(), file.Length, file.LastWriteTimeUtc.Ticks);
                database.ApplyObservedItems([item], []);
                return item;
            }

            string comicPath1 = CreateComic(root, "first.cbz", 7);
            string comicPath2 = CreateComic(root, "second.cbz", 7);
            string videoPath = Path.Combine(root, "delete-copy.mp4");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "silent.mp4"), videoPath);
            AddItem(comicCategory, comicPath1);
            AddItem(comicCategory, comicPath2);
            var video = AddItem(videoCategory, videoPath);

            var deletions = new DeletionService(database,
                new DeletionJournal(Path.Combine(root, "delete-journal")), record =>
                {
                    deleteCalls++;
                    File.Delete(record.Path);
                    return Task.FromResult(new FileDeletionResult(DeletionOutcome.Succeeded));
                });
            await deletions.InitializeAsync();

            window = new ViewingWindow(database, deletions) { WindowStartupLocation = WindowStartupLocation.CenterScreen };
            window.Show();
            await Wait(() => ((ListBox)window.FindName("Categories")).Items.Count == 2
                && ((FrameworkElement)window.FindName("SessionControls")).IsEnabled, "T13 category and session controls loaded");
            var categoryPicker = (ListBox)window.FindName("Categories");
            categoryPicker.SelectedItems.Add(comicCategory);
            ClickButton(window, "랜덤 시작");
            await Wait(() => window.Current?.Item.CategoryId == comicCategory.Id
                && window.Coordinator.View.Phase == SessionPhase.Active, "T13 comic session opened");
            FocusCommandButton(window);

            Guid initialComicId = window.Current!.Item.Id;
            bool favoriteBefore = database.GetItems(comicCategory.Id).Single(item => item.Id == initialComicId).IsFavorite;
            var favoriteKey = PressKey(window, Key.F);
            await Wait(() => database.GetItems(comicCategory.Id).Single(item => item.Id == initialComicId).IsFavorite != favoriteBefore,
                "T13 F toggles favorite through existing command");
            Check(favoriteKey.Handled, "T13 handled favorite key is consumed");
            bool favoriteAfter = database.GetItems(comicCategory.Id).Single(item => item.Id == initialComicId).IsFavorite;
            Check(Route(window, Key.F, ModifierKeys.None, true) && database.GetItems(comicCategory.Id)
                .Single(item => item.Id == initialComicId).IsFavorite == favoriteAfter,
                "T13 favorite key repeat is ignored");

            PressKey(window, Key.I);
            await Wait(() => window.Coordinator.View.Pending?.SuppressHistory == true, "T13 I excludes the current visit");
            Route(window, Key.I, ModifierKeys.None, true);
            Check(window.Coordinator.View.Pending?.SuppressHistory == true, "T13 history-exclusion repeat is ignored");
            PressKey(window, Key.I);
            await Wait(() => window.Coordinator.View.Pending?.SuppressHistory == false, "T13 I restores normal visit recording");

            var comicContent = window.Current!.ComicContent!;
            var displayMode = FindElement<ComboBox>(comicContent, control => control.Name == "DisplayModeBox");
            var zoomText = FindElement<TextBlock>(comicContent, control => control.Name == "ZoomText");
            displayMode.SelectedItem = displayMode.Items.Cast<ComboBoxItem>()
                .Single(item => Equals(item.Tag?.ToString(), "SinglePage"));
            int page = window.Current!.Capture(window.Coordinator.View.ActiveToken!).ComicPageIndex!.Value;
            var pageDown = PressKey(window, Key.PageDown);
            await Wait(() => window.Current!.Capture(window.Coordinator.View.ActiveToken!).ComicPageIndex == page + 1,
                "T13 PageDown moves one comic page");
            Check(pageDown.Handled, "T13 comic page key is consumed once by the common window");
            PressKey(window, Key.PageUp);
            await Wait(() => window.Current!.Capture(window.Coordinator.View.ActiveToken!).ComicPageIndex == page,
                "T13 PageUp moves to the previous comic page");

            displayMode.SelectedItem = displayMode.Items.Cast<ComboBoxItem>()
                .Single(item => Equals(item.Tag?.ToString(), "TwoPage"));
            await window.Current!.WhenIdleAsync();
            page = window.Current!.Capture(window.Coordinator.View.ActiveToken!).ComicPageIndex!.Value;
            Route(window, Key.PageDown, ModifierKeys.None, false);
            await Wait(() => window.Current!.Capture(window.Coordinator.View.ActiveToken!).ComicPageIndex == page + 2,
                "T13 PageDown preserves the existing two-page spread movement");
            page += 2;
            SetField(window, "busy", true);
            Route(window, Key.PageDown, ModifierKeys.None, true);
            Route(window, Key.PageDown, ModifierKeys.None, true);
            SetField(window, "busy", false);
            Check(window.Current!.Capture(window.Coordinator.View.ActiveToken!).ComicPageIndex == page,
                "T13 page repeat during Busy does not queue another page operation");

            Check(Route(window, Key.Add, ModifierKeys.Control, true), "T13 Ctrl+numpad Add is recognized");
            Check(Route(window, Key.Add, ModifierKeys.Control, true), "T13 repeated comic zoom input is admitted without queuing");
            await Wait(() => zoomText.Text == "121%", "T13 repeated comic zoom applies each admitted step without a queued backlog");
            Check(Route(window, Key.OemPlus, ModifierKeys.Control | ModifierKeys.Shift, false),
                "T13 Ctrl+Plus accepts the shifted OEM plus key");
            await Wait(() => zoomText.Text == "133%", "T13 Ctrl+Plus zooms using the existing scale");
            Check(!Route(window, Key.OemMinus, ModifierKeys.Control | ModifierKeys.Shift, false),
                "T13 unrelated Ctrl+Shift+Minus is not treated as Ctrl+Minus");
            Check(Route(window, Key.OemMinus, ModifierKeys.Control, false), "T13 Ctrl+Minus is recognized");
            await Wait(() => zoomText.Text == "121%", "T13 Ctrl+Minus uses the existing zoom scale");
            Check(Route(window, Key.D0, ModifierKeys.Control, false), "T13 Ctrl+0 is recognized");
            await Wait(() => zoomText.Text == "FitWindow", "T13 Ctrl+0 selects the existing window fit mode");
            Check(!Route(window, Key.F, ModifierKeys.Control, false), "T13 modified letter is not treated as an unmodified shortcut");

            var days = (TextBox)window.FindName("Days");
            Keyboard.Focus(days);
            bool inputFavoriteBefore = database.GetItems(comicCategory.Id).Single(item => item.Id == initialComicId).IsFavorite;
            var textInputKey = PressKey(window, Key.F);
            Check(!textInputKey.Handled && database.GetItems(comicCategory.Id)
                .Single(item => item.Id == initialComicId).IsFavorite == inputFavoriteBefore,
                "T13 does not steal a key from a text input");
            Keyboard.Focus(categoryPicker);
            page = window.Current!.Capture(window.Coordinator.View.ActiveToken!).ComicPageIndex!.Value;
            var listInputKey = PressKey(window, Key.PageDown);
            Check(!listInputKey.Handled && window.Current!.Capture(window.Coordinator.View.ActiveToken!).ComicPageIndex == page,
                "T13 preserves list-box page-key handling");
            FocusCommandButton(window);
            SetField(window, "imeComposing", true);
            var imeKey = PressKey(window, Key.F);
            SetField(window, "imeComposing", false);
            Check(!imeKey.Handled && database.GetItems(comicCategory.Id)
                .Single(item => item.Id == initialComicId).IsFavorite == inputFavoriteBefore,
                "T13 suppresses shortcuts during IME composition");

            Guid currentComicId = window.Current!.Item.Id;
            SetField(window, "busy", true);
            Check(Route(window, Key.N, ModifierKeys.None, false), "T13 next key is recognized while busy");
            Check(window.Current!.Item.Id == currentComicId, "T13 busy state blocks the keyboard navigation command");
            SetField(window, "busy", false);

            PressKey(window, Key.F11);
            Check(window.WindowStyle == WindowStyle.None && window.WindowState == WindowState.Maximized,
                "T13 F11 enters borderless fullscreen");
            var toolbar = FindElement<Border>(comicContent, control => control.Name == "Toolbar");
            await Task.Delay(TimeSpan.FromMilliseconds(3500));
            Check(((FrameworkElement)window.FindName("TopChrome")).Visibility == Visibility.Collapsed
                && toolbar.Visibility == Visibility.Collapsed,
                "T13 fullscreen auto-hides common and comic controls after three idle seconds");
            RevealWithMouse(window);
            Check(((FrameworkElement)window.FindName("TopChrome")).Visibility == Visibility.Visible
                && toolbar.Visibility == Visibility.Visible,
                "T13 mouse movement restores fullscreen controls");

            privacy = new PrivacyWindows();
            privacy.Hide();
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            Check(privacy.Hidden && !PrivacyWindows.IsWindowVisible(hwnd), "T13 T16 privacy hide hides fullscreen window");
            privacy.Restore();
            await Wait(() => PrivacyWindows.IsWindowVisible(hwnd), "T13 T16 privacy restore shows fullscreen window");
            Check(window.WindowStyle == WindowStyle.None && window.WindowState == WindowState.Maximized,
                "T13 privacy restore preserves fullscreen style and state");
            privacy.Dispose();
            privacy = null;
            PressKey(window, Key.Escape);
            Check(window.WindowStyle == WindowStyle.SingleBorderWindow && window.WindowState == WindowState.Normal,
                "T13 Escape exits fullscreen after privacy restore");
            var normalEscape = PressKey(window, Key.Escape);
            Check(!normalEscape.Handled && window.IsVisible, "T13 Escape does not close or hide a normal window");

            categoryPicker.SelectedItems.Clear();
            categoryPicker.SelectedItems.Add(videoCategory);
            ClickButton(window, "랜덤 시작");
            await Wait(() => window.Current?.Video is not null
                && window.Current!.Item.Id == video.Id
                && window.Coordinator.View.Phase == SessionPhase.Active, "T13 video session opened");
            FocusCommandButton(window);
            var videoMedia = window.Current!;
            videoMedia.Video!.SetPaused(videoMedia.VideoVisit, true);
            await Wait(() => videoMedia.Video!.Snapshot(videoMedia.VideoVisit).State == VLCState.Paused,
                "T13 video is paused for keyboard checks");

            PressKey(window, Key.Space);
            await Wait(() => videoMedia.Video!.Snapshot(videoMedia.VideoVisit).State == VLCState.Playing
                && ((FrameworkElement)window.FindName("VideoControls")).IsEnabled,
                "T13 Space plays a paused video through the existing command");
            var spaceRepeat = Route(window, Key.Space, ModifierKeys.None, true);
            await Wait(() => videoMedia.Video!.Snapshot(videoMedia.VideoVisit).State == VLCState.Playing
                && ((FrameworkElement)window.FindName("VideoControls")).IsEnabled,
                "T13 Space auto-repeat does not toggle playback again");
            Check(spaceRepeat, "T13 repeated Space is consumed");
            PressKey(window, Key.Space);
            await Wait(() => videoMedia.Video!.Snapshot(videoMedia.VideoVisit).State == VLCState.Paused
                && ((FrameworkElement)window.FindName("VideoControls")).IsEnabled,
                "T13 Space pauses a playing video");

            var volume = (Slider)window.FindName("Volume");
            volume.Value = 50;
            await Wait(() => videoMedia.Video!.Snapshot(videoMedia.VideoVisit).Volume == 50
                && ((FrameworkElement)window.FindName("VideoControls")).IsEnabled, "T13 video volume control completed");
            PressKey(window, Key.Up);
            await Wait(() => videoMedia.Video!.Snapshot(videoMedia.VideoVisit).Volume == 55
                && videoControlsEnabled(window),
                "T13 Up raises video volume by five");
            PressKey(window, Key.Down);
            await Wait(() => videoMedia.Video!.Snapshot(videoMedia.VideoVisit).Volume == 50
                && videoControlsEnabled(window),
                "T13 Down lowers video volume by five");
            Check(Route(window, Key.Up, ModifierKeys.None, true), "T13 repeated volume input is admitted");
            await Wait(() => videoMedia.Video!.Snapshot(videoMedia.VideoVisit).Volume == 55
                && videoControlsEnabled(window),
                "T13 volume repeat uses the existing range");

            var videoSnapshot = videoMedia.Video!.Snapshot(videoMedia.VideoVisit);
            long seed = videoSnapshot.DurationMs / 2;
            Check(videoSnapshot.Seekable && videoMedia.Video!.Seek(videoMedia.VideoVisit, seed), "T13 video supports range-limited seek");
            PressKey(window, Key.Right);
            long afterRight = Math.Min(seed + 5000, videoSnapshot.DurationMs);
            await Wait(() => Math.Abs(videoMedia.Video!.Snapshot(videoMedia.VideoVisit).Progress.VideoPositionMs!.Value - afterRight) < 250
                && videoControlsEnabled(window),
                "T13 Right seeks forward five seconds within duration");
            PressKey(window, Key.Left);
            long afterLeft = Math.Max(0, afterRight - 5000);
            await Wait(() => Math.Abs(videoMedia.Video!.Snapshot(videoMedia.VideoVisit).Progress.VideoPositionMs!.Value - afterLeft) < 250
                && videoControlsEnabled(window),
                "T13 Left seeks back five seconds within duration");

            bool muteBefore = videoMedia.Video!.Snapshot(videoMedia.VideoVisit).Muted;
            var muteKey = PressKey(window, Key.M);
            await Wait(() => videoMedia.Video!.Snapshot(videoMedia.VideoVisit).Muted != muteBefore
                && videoControlsEnabled(window),
                "T13 M toggles app mute through the existing command");
            Check(muteKey.Handled && Route(window, Key.M, ModifierKeys.None, true)
                && videoMedia.Video!.Snapshot(videoMedia.VideoVisit).Muted != muteBefore,
                "T13 mute auto-repeat is consumed without a second toggle");

            var videoControls = (FrameworkElement)window.FindName("VideoControls");
            PressKey(window, Key.F11);
            await Task.Delay(TimeSpan.FromMilliseconds(3500));
            Check(((FrameworkElement)window.FindName("TopChrome")).Visibility == Visibility.Collapsed
                && videoControls.Visibility == Visibility.Collapsed,
                "T13 fullscreen auto-hides video controls");
            PressKey(window, Key.M);
            Check(videoControls.Visibility == Visibility.Visible,
                "T13 keyboard input restores fullscreen video controls");
            await Wait(() => videoControlsEnabled(window), "T13 fullscreen video command completed");
            PressKey(window, Key.Escape);
            Check(window.WindowStyle == WindowStyle.SingleBorderWindow && window.WindowState == WindowState.Normal,
                "T13 video fullscreen exits with Escape");

            var deleteButton = (Button)window.FindName("DeleteButton");
            database.QuarantineDeletion(video.PathKey);
            Invoke(window, "Controls");
            Check(!deleteButton.IsEnabled, "T13 quarantined file keeps the existing delete button disabled");
            Check(Route(window, Key.Delete, ModifierKeys.None, false) && deleteCalls == 0,
                "T13 Delete shortcut cannot bypass deletion quarantine");
            database.ReleaseDeletion(video.PathKey);
            Invoke(window, "Controls");
            Check(deleteButton.IsEnabled, "T13 delete button reopens after the quarantine is released");

            int dialogsBefore = Application.Current.Windows.OfType<DeleteConfirmationWindow>().Count();
            Check(Route(window, Key.Delete, ModifierKeys.None, true), "T13 repeated Delete is consumed without opening a dialog");
            Check(Application.Current.Windows.OfType<DeleteConfirmationWindow>().Count() == dialogsBefore,
                "T13 repeated Delete opens no duplicate confirmation");

            var cancelTimer = ScheduleDialogAction(dialog => dialog.Close());
            var cancelDeleteKey = PressKey(window, Key.Delete);
            cancelTimer.Stop();
            await Wait(() => !GetField<bool>(window, "busy"), "T13 canceled delete dialog returned");
            Check(cancelDeleteKey.Handled && File.Exists(videoPath) && deleteCalls == 0
                && window.Current?.Item.Id == video.Id,
                "T13 Delete cancellation leaves test copy and visit untouched");

            int confirmationCount = 0;
            bool repeatedDeleteConsumed = false;
            var confirmTimer = ScheduleDialogAction(dialog =>
            {
                confirmationCount++;
                repeatedDeleteConsumed = Route(window, Key.Delete, ModifierKeys.None, true);
                var panel = (StackPanel)dialog.Content;
                var buttons = (StackPanel)panel.Children.OfType<StackPanel>().Single();
                buttons.Children.OfType<Button>().First(button => Equals(button.Content, "선택한 방법으로 삭제"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            PressKey(window, Key.Delete);
            confirmTimer.Stop();
            await Wait(() => !GetField<bool>(window, "busy") && deleteCalls == 1,
                "T13 confirmed test-copy deletion completed");
            Check(confirmationCount == 1 && repeatedDeleteConsumed && deleteCalls == 1 && !File.Exists(videoPath),
                "T13 one confirmed Delete executes once despite a repeated key");

            Check(Route(window, Key.F2, ModifierKeys.None, false) == false,
                "T13 unrelated key is not consumed");
        }
        finally
        {
            privacy?.Restore();
            privacy?.Dispose();
            if (window is { IsVisible: true })
            {
                bool closed = false;
                window.Closed += (_, _) => closed = true;
                window.Close();
                await Wait(() => closed, "T13 viewing window closed normally");
            }
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateComic(string root, string fileName, int pages)
    {
        string path = Path.Combine(root, fileName);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var bitmap = new SKBitmap(80, 100);
        bitmap.Erase(SKColors.DarkSlateBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        for (int index = 0; index < pages; index++)
        {
            using var stream = archive.CreateEntry($"page-{index + 1:00}.png").Open();
            png.SaveTo(stream);
        }
        return path;
    }

    private static void ClickButton(DependencyObject root, string label) =>
        FindElement<Button>(root, button => Equals(button.Content, label))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void FocusCommandButton(Window window) =>
        Keyboard.Focus(FindElement<Button>(window, button => Equals(button.Content, "랜덤 시작")));

    private static bool videoControlsEnabled(Window window) =>
        ((FrameworkElement)window.FindName("VideoControls")).IsEnabled;

    private static KeyEventArgs PressKey(Window window, Key key)
    {
        var source = PresentationSource.FromVisual(window)
            ?? throw new InvalidOperationException("T13 WPF window has no presentation source.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        window.RaiseEvent(args);
        return args;
    }

    private static void RevealWithMouse(Window window)
    {
        var args = new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
        { RoutedEvent = Mouse.PreviewMouseMoveEvent };
        window.RaiseEvent(args);
    }

    private static bool Route(ViewingWindow window, Key key, ModifierKeys modifiers, bool isRepeat) =>
        (bool)(typeof(ViewingWindow).GetMethod("TryHandleShortcut", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("TryHandleShortcut"))
            .Invoke(window, [key, modifiers, isRepeat])!;

    private static void Invoke(ViewingWindow window, string methodName) =>
        (typeof(ViewingWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(methodName)).Invoke(window, null);

    private static void SetField(ViewingWindow window, string fieldName, object value) =>
        (typeof(ViewingWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(fieldName)).SetValue(window, value);

    private static T GetField<T>(ViewingWindow window, string fieldName) =>
        (T)(typeof(ViewingWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(fieldName)).GetValue(window)!;

    private static DispatcherTimer ScheduleDialogAction(Action<DeleteConfirmationWindow> action)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher)
        { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.Current.Windows.OfType<DeleteConfirmationWindow>().FirstOrDefault();
            if (dialog is null) return;
            timer.Stop();
            action(dialog);
        };
        timer.Start();
        return timer;
    }

    private static T FindElement<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        if (root is T candidate && predicate(candidate)) return candidate;
        foreach (var child in GetChildren(root))
        {
            try { return FindElement(child, predicate); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException($"T13 element not found: {typeof(T).Name}");
    }

    private static IEnumerable<DependencyObject> GetChildren(DependencyObject root)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject node) yield return node;
        if (root is Visual)
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
                yield return VisualTreeHelper.GetChild(root, index);
    }

    private static async Task Wait(Func<bool> condition, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(message);
            await Task.Delay(25);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}
