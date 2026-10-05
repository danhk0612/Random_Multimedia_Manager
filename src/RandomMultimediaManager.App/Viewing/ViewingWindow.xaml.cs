using System.ComponentModel;
using RandomMultimediaManager.App.Deletion;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;
using Microsoft.Win32;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Sessions;
using RandomMultimediaManager.App.ViewModels;
using RandomMultimediaManager.App.Views;
using RandomMultimediaManager.App.Video;
using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.Viewing;

public partial class ViewingWindow : Window
{
    private readonly LibraryDatabase database;
    private readonly DeletionService? deletions;
    private Guid? initialManualItemId;
    public SessionCoordinator Coordinator { get; }
    public ViewingMedia? Current { get; private set; }
    private readonly DispatcherTimer timer;
    private readonly DispatcherTimer fullscreenControlsTimer;
    private Task command = Task.CompletedTask;
    private Task<bool>? closeTask;
    private bool exitRequested, closeAfterRetry;
    private bool applicationClosing;
    private Window? libraryDialog;
    private LibraryBrowserViewModel? libraryBrowser;
    public string? CloseError { get; private set; }
    private bool busy, allowClose, closing, seeking, refreshing, fullscreen, fullscreenControlsVisible = true;
    private bool imeComposing;
    private int modalInputDepth;
    private DateTime lastCheckpoint = DateTime.UtcNow;
    private PlaybackProgress? saved;
    private SessionToken? savedToken;
    private WindowState previousState;

    public ViewingWindow(LibraryDatabase database, DeletionService? deletions = null, Guid? initialManualItemId = null)
    {
        this.database = database;
        this.deletions = deletions;
        this.initialManualItemId = initialManualItemId;
        InitializeComponent();
        Coordinator = new(database, new ViewingMediaPreparer(VideoSurface, OnMediaActivated, Released));
        fullscreenControlsTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        { Interval = TimeSpan.FromSeconds(3) };
        fullscreenControlsTimer.Tick += (_, _) => HideFullscreenControls();
        TextCompositionManager.AddPreviewTextInputStartHandler(this, CompositionStarted);
        TextCompositionManager.AddTextInputHandler(this, CompositionCompleted);
        Keyboard.AddLostKeyboardFocusHandler(this, (_, _) => imeComposing = false);
        Deactivated += (_, _) => imeComposing = false;
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
            async (_, _) => await Tick(), Dispatcher);
        Loaded += async (_, _) =>
        {
            await Run(async () =>
            {
                Categories.ItemsSource = (await Task.Run(database.GetCategories)).Where(c => c.IsEnabled).ToArray();
                var settings = await Task.Run(database.GetSettings);
                Days.Text = settings.HistoryExclusionDays.ToString();
                ResumeModeBox.SelectedIndex = settings.ResumeMode == ResumeMode.Resume ? 0 : 1;
            }, whileHidden: true);
            if (initialManualItemId is not { } itemId) return;
            initialManualItemId = null;
            if (exitRequested) return;
            if (PreparedVideo.PrivacyMuted)
            {
                Status.Text = "숨김 상태에서 시작한 수동 감상 열기를 취소했습니다. 다시 감상하려면 복원 후 선택하세요.";
                return;
            }
            await Run(() => Navigate(() => Coordinator.OpenManualAsync(Guid.NewGuid(), itemId)));
        };
        Closed += (_, _) => { timer.Stop(); fullscreenControlsTimer.Stop(); };
        timer.Start();
    }

    private void OnMediaActivated(ViewingMedia media)
    {
        Current = media;
        ComicSurface.Content = media.ComicContent;
        ApplyFullscreenControlsVisibility();
        Volume.Value = 70; Muted.IsChecked = false; Rate.SelectedIndex = 1;
        saved = null; savedToken = null;
        ExternalSubtitles.ItemsSource = null; AudioTracks.ItemsSource = null; SubtitleTracks.ItemsSource = null;
        Title = $"감상 — {Path.GetFileName(media.Item.Path)}";
    }
    private void Released(ViewingMedia media)
    {
        if (!ReferenceEquals(Current, media)) return;
        Current = null; ComicSurface.Content = null;
        ApplyFullscreenControlsVisibility();
    }
    private void Controls()
    {
        DeleteButton.IsEnabled = !applicationClosing && !busy && !closing && !exitRequested && Current is not null && deletions is not null
            && !database.IsDeletionBlocked(Current.Item.PathKey);
        RecoveryButton.IsEnabled = !busy && !closing && !exitRequested && deletions?.Pending.Count > 0;
        bool failed = Coordinator.View.Phase == SessionPhase.SaveFailed;
        SessionControls.IsEnabled = SettingsControls.IsEnabled = !applicationClosing && !busy && !closing && !exitRequested && !failed;
        VideoControls.IsEnabled = !applicationClosing && !busy && !closing && !exitRequested && !failed;
        if (Current?.ComicContent is { } comic) comic.IsEnabled = !applicationClosing && !busy && !closing && !exitRequested && !failed;
        RetryButton.IsEnabled = ResumeButton.IsEnabled = !busy && !closing && !exitRequested && failed;
    }
    // Admission is synchronous; navigation never queues behind another command. A checkpoint
    // already admitted finishes before any transition can capture/commit a later position.
    private async Task Run(Func<Task> action, bool whileHidden = false, bool recovery = false)
    {
        if (busy || closing || exitRequested || (applicationClosing && !recovery) || (!whileHidden && PreparedVideo.PrivacyMuted)) return;
        busy = true; Controls();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        command = completion.Task; // Publish before a modal dialog can pump tray messages.
        async Task Execute()
        {
            try { if (Current is { } media) await media.WhenIdleAsync(); await action(); }
            catch (Exception ex) { Status.Text = ex.Message; }
            finally { busy = false; Controls(); completion.SetResult(); }
        }
        await Execute();
    }
    private async Task Navigate(Func<Task<SessionResult>> action, bool recovery = false)
    {
        var before = Current;
        Status.Text = "감상 전환 준비 중… 준비 중에는 ‘준비 취소’를 사용할 수 있습니다.";
        // Let WPF render the pending state before entering native preparation calls.
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Background);
        if (closing || exitRequested || (applicationClosing && !recovery)) return;
        if (PreparedVideo.PrivacyMuted)
        {
            Status.Text = "숨김 중에는 감상을 열 수 없습니다.";
            return;
        }
        var result = await action();
        Status.Text = result.Error ?? result.Status switch
        {
            SessionStatus.Completed => "감상 준비 완료",
            SessionStatus.NoCandidates => "조건에 맞는 후보가 없습니다. 분류와 제외 기간을 확인하세요.",
            SessionStatus.NoPrevious => "이전 항목이 없습니다.",
            SessionStatus.SelectionRequired => "분류를 선택하세요.",
            SessionStatus.SaveFailed => "저장하지 못했습니다. 재시도하거나 저장 취소 후 감상으로 복귀하세요.",
            SessionStatus.CommitUnknown => "저장 결과를 확인할 수 없습니다. 현재 방문을 보존합니다. 재시도하세요.",
            SessionStatus.Cancelled => "준비를 취소했습니다. 기존 감상을 유지합니다.",
            SessionStatus.SessionLimit => "세션 한도에 도달했습니다. 새 랜덤 감상을 시작하세요.",
            _ => result.Status.ToString()
        };
        if (Current is { } current)
        {
            var item = (await Task.Run(() => database.GetItems(current.Item.CategoryId))).Single(i => i.Id == current.Item.Id);
            Favorite.IsChecked = item.IsFavorite; Excluded.IsChecked = item.IsRandomExcluded;
            Suppressed.IsChecked = Coordinator.View.Pending?.SuppressHistory == true;
            if (!applicationClosing && !ReferenceEquals(before, current) && current.Video is not null)
            {
                Status.Text += "\n" + current.Video.Notice;
                if (current.Video.RestoredCompleted) Status.Text += "재생 완료 위치입니다. 재생을 누르면 처음부터 시작합니다.";
                try
                {
                    var candidates = await Task.Run(() => ExternalSubtitleService.Discover(current.Item.Path));
                    refreshing = true;
                    ExternalSubtitles.ItemsSource = candidates;
                    refreshing = false;
                    if (candidates.Count > 0) await Subtitle(candidates[0].Path);
                }
                catch (Exception ex) { Status.Text += "\n자막 검색 실패: " + ex.Message; }
                RefreshTracks(this, new RoutedEventArgs());
            }
        }
    }
    private async void DeleteCurrent(object s, RoutedEventArgs e) => await Run(async () =>
    {
        if (Current is null || deletions is null) return;
        var dialog = new DeleteConfirmationWindow(Current.Item.Path) { Owner = this };
        bool? confirmed;
        modalInputDepth++;
        try { confirmed = dialog.ShowDialog(); }
        finally { modalInputDepth--; }
        if (confirmed != true || dialog.Selection is not { } mode || closing || exitRequested) return;
        Status.Text = "삭제 중… 결과와 저널 저장이 끝날 때까지 기다려 주세요.";
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Background);
        if (closing || exitRequested) return;
        var result = await Coordinator.DeleteCurrentAsync(Guid.NewGuid(), deletions, mode);
        Status.Text = result.Error ?? result.Status.ToString();
        Suppressed.IsChecked = Coordinator.View.Pending?.SuppressHistory == true;
        if (Current?.Video is { } video)
        {
            var snapshot = video.Snapshot(Current.VideoVisit);
            Volume.Value = snapshot.Volume; Muted.IsChecked = snapshot.Muted;
        }
    });
    private async void RecoverDeletion(object s, RoutedEventArgs e) => await Run(async () =>
    {
        if (deletions is null) return;
        modalInputDepth++;
        try { await DeletionDialogs.RecoverAsync(deletions, this, Coordinator); }
        finally { modalInputDepth--; }
        Status.Text = deletions.Pending.Count == 0 ? "삭제 복구 처리를 완료했습니다." : "미해결 삭제 경로의 격리를 유지합니다.";
        Suppressed.IsChecked = Coordinator.View.Pending?.SuppressHistory == true;
    }, recovery: true);
    private async void Start(object s, RoutedEventArgs e) => await Run(() => Navigate(() => Coordinator.StartRandomAsync(Guid.NewGuid(), Categories.SelectedItems.Cast<Category>().Select(c => c.Id))));
    private async void BrowseLibrary(object s, RoutedEventArgs e) => await Run(async () =>
    {
        var viewModel = new LibraryBrowserViewModel(database);
        var browser = new LibraryBrowserView { DataContext = viewModel };
        var dialog = new Window
        {
            Title = "라이브러리에서 파일 선택",
            Owner = this,
            Content = browser,
            Width = 1000,
            Height = 680,
            MinWidth = 760,
            MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        Guid? selectedItemId = null;
        bool allowDialogClose = false;
        browser.ManualOpenRequested += id =>
        {
            if (closing || exitRequested) return;
            selectedItemId = id;
            dialog.Close();
        };
        dialog.Closing += async (_, args) =>
        {
            if (allowDialogClose) return;
            args.Cancel = true;
            viewModel.SetExitRequested(true);
            browser.IsEnabled = false;
            await viewModel.WaitForPendingReadsAsync();
            allowDialogClose = true;
            _ = dialog.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (dialog.IsVisible) dialog.Close();
            }), DispatcherPriority.Normal);
        };
        libraryDialog = dialog;
        libraryBrowser = viewModel;
        try
        {
            modalInputDepth++;
            try { dialog.ShowDialog(); }
            finally { modalInputDepth--; }
            await viewModel.WaitForPendingReadsAsync();
            if (selectedItemId is { } itemId && !closing && !exitRequested && !PreparedVideo.PrivacyMuted)
                await Navigate(() => Coordinator.OpenManualAsync(Guid.NewGuid(), itemId));
        }
        finally
        {
            viewModel.SetExitRequested(true);
            await viewModel.WaitForPendingReadsAsync();
            if (ReferenceEquals(libraryDialog, dialog)) libraryDialog = null;
            if (ReferenceEquals(libraryBrowser, viewModel)) libraryBrowser = null;
        }
    });
    private async void Next(object s, RoutedEventArgs e) => await Run(() => Navigate(() => Coordinator.NextAsync(Guid.NewGuid())));
    private async void Previous(object s, RoutedEventArgs e) => await Run(() => Navigate(() => Coordinator.PreviousAsync(Guid.NewGuid())));
    private void Cancel(object s, RoutedEventArgs e)
    { if (Coordinator.PreparingToken is { } t) Coordinator.CancelOpening(t.SessionId, t.OperationId); }
    private async void Retry(object s, RoutedEventArgs e)
    {
        await Run(() => Navigate(() => Coordinator.RetryAsync(Guid.NewGuid()), recovery: true), recovery: true);
        if (closeAfterRetry && !busy && !closing && !exitRequested
            && Coordinator.View.Phase != SessionPhase.SaveFailed && Coordinator.ReleaseError is null)
            await RequestCloseAsync();
    }
    private async void ResumeFailed(object s, RoutedEventArgs e) => await Run(async () =>
    {
        await Navigate(() => Coordinator.ResumeAfterSaveFailureAsync(Guid.NewGuid()), recovery: true);
        if (Coordinator.View.Phase == SessionPhase.Active) { closing = false; closeAfterRetry = false; }
    }, recovery: true);
    private async void SuppressedChanged(object s, RoutedEventArgs e) => await Run(async () =>
    {
        if (Current?.Token is { } t) await Navigate(() => Coordinator.SetSuppressedAsync(Guid.NewGuid(), t, Suppressed.IsChecked == true));
    });
    private async Task SetFlag(bool favorite)
    {
        if (Current is not { } media) return;
        try
        {
            bool desired = (favorite ? Favorite : Excluded).IsChecked == true;
            await Task.Run(() => { if (favorite) database.SetFavorite(media.Item.Id, desired); else database.SetRandomExcluded(media.Item.Id, desired); });
        }
        finally
        {
            var item = (await Task.Run(() => database.GetItems(media.Item.CategoryId))).Single(i => i.Id == media.Item.Id);
            Favorite.IsChecked = item.IsFavorite; Excluded.IsChecked = item.IsRandomExcluded;
        }
    }
    private async void FavoriteChanged(object s, RoutedEventArgs e) => await Run(() => SetFlag(true));
    private async void ExcludedChanged(object s, RoutedEventArgs e) => await Run(() => SetFlag(false));
    private async void SaveSettings(object s, RoutedEventArgs e) => await Run(async () =>
    {
        try
        {
            if (!int.TryParse(Days.Text, out int days)) throw new ArgumentException("제외 기간은 0~36500일 정수로 입력하세요.");
            var settings = new AppSettings(days, ResumeModeBox.SelectedIndex == 0 ? ResumeMode.Resume : ResumeMode.FromStart);
            settings.Validate(); await Task.Run(() => database.SaveSettings(settings));
            Status.Text = "설정을 저장했습니다. 다음 열기부터 적용합니다.";
        }
        finally
        {
            var settings = await Task.Run(database.GetSettings);
            Days.Text = settings.HistoryExclusionDays.ToString();
            ResumeModeBox.SelectedIndex = settings.ResumeMode == ResumeMode.Resume ? 0 : 1;
        }
    });
    public async Task CheckpointAsync()
    {
        if (Current is not { Token: { } token } media || Coordinator.View.Phase != SessionPhase.Active) return;
        var progress = media.Capture(token);
        if (!Coordinator.ObserveProgress(token, progress) || (savedToken == token && saved == progress)) return;
        await Task.Run(() => database.SaveProgress(token.ItemId, progress, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        saved = progress; savedToken = token;
    }
    private async Task Tick()
    {
        if (applicationClosing || busy || closing || exitRequested || Coordinator.View.Phase != SessionPhase.Active) return;
        try
        {
            if (Current?.Video is { } video)
            {
                var snapshot = video.Snapshot(Current.VideoVisit);
                if (!seeking) { Position.Maximum = Math.Max(1, snapshot.DurationMs); Position.Value = snapshot.Progress.VideoPositionMs!.Value; }
                TimeText.Text = $"{TimeSpan.FromMilliseconds(snapshot.Progress.VideoPositionMs!.Value):hh\\:mm\\:ss} / {TimeSpan.FromMilliseconds(snapshot.DurationMs):hh\\:mm\\:ss}  {snapshot.State}";
            }
            if (DateTime.UtcNow - lastCheckpoint >= TimeSpan.FromSeconds(5))
            { lastCheckpoint = DateTime.UtcNow; await Run(CheckpointAsync, whileHidden: true); }
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private async Task VideoCommand(Func<PreparedVideo, VideoVisit, Task> action, bool checkpoint = false)
    {
        await Run(async () =>
        {
            if (Current?.Video is not { } video || Coordinator.View.Phase != SessionPhase.Active) return;
            await action(video, Current.VideoVisit);
            if (checkpoint) await CheckpointAsync();
        });
    }
    private Task VideoAction(Action<PreparedVideo, VideoVisit> action, bool checkpoint = false) =>
        VideoCommand((v,t) => { action(v,t); return Task.CompletedTask; }, checkpoint);
    private async void Play(object s, RoutedEventArgs e) => await VideoAction((v,t) => { v.Play(t); v.SetVolume(t,(int)Volume.Value); v.SetMuted(t,Muted.IsChecked == true); });
    private async void Pause(object s, RoutedEventArgs e) => await VideoAction((v,t) => v.SetPaused(t,true), true);
    private async void Stop(object s, RoutedEventArgs e) => await VideoCommand((v,t) => v.StopAsync(t), true);
    private async void SeekBack(object s, RoutedEventArgs e) => await VideoAction((v,t) => v.Seek(t,v.CaptureProgress(t).VideoPositionMs!.Value-10000));
    private async void SeekForward(object s, RoutedEventArgs e) => await VideoAction((v,t) => v.Seek(t,v.CaptureProgress(t).VideoPositionMs!.Value+10000));
    private async void VolumeChanged(object s, RoutedPropertyChangedEventArgs<double> e) { if (Coordinator is not null) await VideoAction((v,t) => v.SetVolume(t,(int)e.NewValue)); }
    private async void Mute(object s, RoutedEventArgs e) => await VideoAction((v,t) => v.SetMuted(t,Muted.IsChecked == true));
    private async void RateChanged(object s, SelectionChangedEventArgs e) { if (Coordinator is not null) await VideoAction((v,t) => v.SetRate(t,new float[]{.5f,1,1.5f,2}[Rate.SelectedIndex])); }
    private void RefreshTracks(object s, RoutedEventArgs e)
    {
        if (Current?.Video is not { } video) return;
        refreshing = true;
        try { AudioTracks.ItemsSource = video.AudioTracks(Current.VideoVisit); SubtitleTracks.ItemsSource = video.SubtitleTracks(Current.VideoVisit); }
        finally { refreshing = false; }
    }
    private async void AudioTrackChanged(object s, SelectionChangedEventArgs e) { if (!refreshing && AudioTracks.SelectedItem is TrackDescription track) await VideoAction((v,t) => v.SetAudioTrack(t,track.Id)); }
    private async void SubtitleTrackChanged(object s, SelectionChangedEventArgs e) { if (!refreshing && SubtitleTracks.SelectedItem is TrackDescription track) await VideoAction((v,t) => v.SetSubtitleTrack(t,track.Id)); }
    private async Task Subtitle(string path)
    {
        if (Current?.Video is not { } video) return;
        var result = await video.LoadExternalSubtitleAsync(Current.VideoVisit,path);
        Status.Text += result.Success ? "\n자막: " + Path.GetFileName(path) : "\n자막 실패 (영상 유지): " + result.Error;
        RefreshTracks(this,new RoutedEventArgs());
    }
    private async void ExternalChanged(object s, SelectionChangedEventArgs e) { if (!refreshing && ExternalSubtitles.SelectedItem is ExternalSubtitleCandidate c) await Run(() => Subtitle(c.Path)); }
    private async void LoadSubtitle(object s, RoutedEventArgs e) => await Run(async () =>
    {
        var dialog = new OpenFileDialog { Filter="자막|*.srt;*.smi" };
        bool? selected;
        modalInputDepth++;
        try { selected = dialog.ShowDialog(this); }
        finally { modalInputDepth--; }
        if (selected == true && !closing && !exitRequested) await Subtitle(dialog.FileName);
    });
    private async void DisableSubtitles(object s, RoutedEventArgs e) => await VideoAction((v,t) => v.DisableSubtitles(t));
    private void BeginSeek(object s, MouseButtonEventArgs e) => seeking = true;
    private async void EndSeek(object s, MouseButtonEventArgs e) { await VideoAction((v,t) => v.Seek(t,(long)Position.Value)); seeking = false; }
    private void Fullscreen(object s, RoutedEventArgs e)
    {
        if (!CanAcceptSessionInput) return;
        SetFullscreen(!fullscreen);
    }

    private void OnPreviewMouseActivity(object sender, MouseEventArgs e) => ShowFullscreenControls();
    private void OnPreviewMouseActivity(object sender, MouseButtonEventArgs e) => ShowFullscreenControls();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (fullscreen) ShowFullscreenControls();
        if (e.Handled || !IsEnabled || closing || exitRequested || PreparedVideo.PrivacyMuted || imeComposing
            || modalInputDepth > 0
            || e.Key is Key.ImeProcessed or Key.DeadCharProcessed
            || IsShortcutInputControl(e.OriginalSource as DependencyObject)
            || IsShortcutInputControl(Keyboard.FocusedElement as DependencyObject)) return;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (TryHandleShortcut(key, Keyboard.Modifiers, e.IsRepeat)) e.Handled = true;
    }

    private void CompositionStarted(object sender, TextCompositionEventArgs e) => imeComposing = true;
    private void CompositionCompleted(object sender, TextCompositionEventArgs e) => imeComposing = false;

    private bool CanAcceptViewingInput => IsEnabled && !busy && !closing && !exitRequested && !PreparedVideo.PrivacyMuted;
    private bool CanAcceptSessionInput => CanAcceptViewingInput && SessionControls.IsEnabled;
    private bool CanAcceptVideoInput => CanAcceptViewingInput && VideoControls.IsEnabled;

    private bool TryHandleShortcut(Key key, ModifierKeys modifiers, bool isRepeat)
    {
        if (modifiers == ModifierKeys.None)
        {
            switch (key)
            {
                case Key.F:
                    if (Current is null) return true;
                    if (CanAcceptSessionInput && Favorite.IsEnabled && !isRepeat)
                    {
                        Favorite.IsChecked = Favorite.IsChecked != true;
                        FavoriteChanged(Favorite, new RoutedEventArgs());
                    }
                    return true;
                case Key.B:
                    if (CanAcceptSessionInput && !isRepeat) Previous(this, new RoutedEventArgs());
                    return true;
                case Key.N:
                    if (CanAcceptSessionInput && !isRepeat) Next(this, new RoutedEventArgs());
                    return true;
                case Key.I:
                    if (Current is null) return true;
                    if (CanAcceptSessionInput && Suppressed.IsEnabled && !isRepeat)
                    {
                        Suppressed.IsChecked = Suppressed.IsChecked != true;
                        SuppressedChanged(Suppressed, new RoutedEventArgs());
                    }
                    return true;
                case Key.Delete:
                    if (DeleteButton.IsEnabled && CanAcceptViewingInput && !isRepeat)
                        DeleteCurrent(DeleteButton, new RoutedEventArgs());
                    return true;
                case Key.F11:
                    if (CanAcceptSessionInput && !isRepeat) SetFullscreen(!fullscreen);
                    return true;
                case Key.Escape:
                    if (!fullscreen) return false;
                    if (CanAcceptSessionInput && !isRepeat) SetFullscreen(false);
                    return true;
                case Key.PageUp:
                case Key.PageDown:
                    if (Current?.ComicContent is null) return false;
                    if (CanAcceptViewingInput && Current.ComicContent.IsEnabled)
                    {
                        var media = Current;
                        int delta = key == Key.PageUp ? -1 : 1;
                        _ = Run(() => media.MoveComicPageFromShortcutAsync(delta));
                    }
                    return true;
                case Key.Space:
                    if (Current?.Video is not { } spaceVideo) return false;
                    if (CanAcceptVideoInput && !isRepeat)
                    {
                        if (spaceVideo.Snapshot(Current.VideoVisit).State == VLCState.Playing)
                            Pause(this, new RoutedEventArgs());
                        else
                            Play(this, new RoutedEventArgs());
                    }
                    return true;
                case Key.Left:
                case Key.Right:
                    if (Current?.Video is null) return false;
                    if (CanAcceptVideoInput)
                    {
                        long delta = key == Key.Left ? -5000 : 5000;
                        _ = VideoAction((video, visit) =>
                            video.Seek(visit, video.CaptureProgress(visit).VideoPositionMs!.Value + delta));
                    }
                    return true;
                case Key.Up:
                case Key.Down:
                    if (Current?.Video is null) return false;
                    if (CanAcceptVideoInput)
                        Volume.Value = Math.Clamp(Volume.Value + (key == Key.Up ? 5 : -5), Volume.Minimum, Volume.Maximum);
                    return true;
                case Key.M:
                    if (Current?.Video is null) return false;
                    if (CanAcceptVideoInput && !isRepeat)
                    {
                        Muted.IsChecked = Muted.IsChecked != true;
                        Mute(Muted, new RoutedEventArgs());
                    }
                    return true;
            }
        }

        if (TryGetComicZoomDirection(key, modifiers, out int zoomDirection))
        {
            if (Current?.ComicContent is null) return false;
            if (CanAcceptViewingInput && Current.ComicContent.IsEnabled)
            {
                var media = Current;
                _ = Run(() =>
                {
                    media.AdjustComicZoomFromShortcut(zoomDirection);
                    return Task.CompletedTask;
                });
            }
            return true;
        }

        if (modifiers == ModifierKeys.Control && (key is Key.D0 or Key.NumPad0))
        {
            if (Current?.ComicContent is null) return false;
            if (CanAcceptViewingInput && Current.ComicContent.IsEnabled)
            {
                var media = Current;
                _ = Run(() =>
                {
                    media.FitComicToWindowFromShortcut();
                    return Task.CompletedTask;
                });
            }
            return true;
        }

        return false;
    }

    private static bool TryGetComicZoomDirection(Key key, ModifierKeys modifiers, out int direction)
    {
        direction = 0;
        if (modifiers == ModifierKeys.Control && key is (Key.Add or Key.Subtract or Key.OemMinus))
            direction = key == Key.Add ? 1 : -1;
        else if ((modifiers == ModifierKeys.Control || modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            && key == Key.OemPlus)
            direction = 1;
        return direction != 0;
    }

    private static bool IsShortcutInputControl(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is TextBoxBase or PasswordBox or ItemsControl or RangeBase) return true;
            element = element switch
            {
                ContentElement content => ContentOperations.GetParent(content),
                Visual visual => VisualTreeHelper.GetParent(visual),
                _ => LogicalTreeHelper.GetParent(element)
            };
        }
        return false;
    }

    private void SetFullscreen(bool enabled)
    {
        if (closing || exitRequested || PreparedVideo.PrivacyMuted || fullscreen == enabled) return;
        if (enabled)
        {
            previousState = WindowState;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            LayoutRoot.Margin = new Thickness(0);
            fullscreen = true;
            fullscreenControlsVisible = true;
            ApplyFullscreenControlsVisibility();
            ShowFullscreenControls();
            Keyboard.Focus(this);
        }
        else
        {
            fullscreenControlsTimer.Stop();
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.SingleBorderWindow;
            LayoutRoot.Margin = new Thickness(8);
            WindowState = previousState;
            fullscreen = false;
            fullscreenControlsVisible = true;
            ApplyFullscreenControlsVisibility();
        }
    }

    private void ShowFullscreenControls()
    {
        if (!fullscreen || PreparedVideo.PrivacyMuted) return;
        fullscreenControlsVisible = true;
        ApplyFullscreenControlsVisibility();
        fullscreenControlsTimer.Stop();
        fullscreenControlsTimer.Start();
    }

    private void HideFullscreenControls()
    {
        if (!fullscreen || !fullscreenControlsVisible) return;
        if (!IsEnabled || PreparedVideo.PrivacyMuted
            || IsShortcutInputControl(Keyboard.FocusedElement as DependencyObject))
        {
            fullscreenControlsTimer.Stop();
            fullscreenControlsTimer.Start();
            return;
        }
        Keyboard.Focus(this);
        fullscreenControlsVisible = false;
        ApplyFullscreenControlsVisibility();
    }

    private void ApplyFullscreenControlsVisibility()
    {
        bool visible = !fullscreen || fullscreenControlsVisible;
        TopChrome.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        VideoControls.Visibility = Current?.Video is null || !visible ? Visibility.Collapsed : Visibility.Visible;
        Current?.SetFullscreenControlsVisible(visible);
    }
    public void SetExitRequested(bool value)
    {
        exitRequested = value;
        applicationClosing |= value;
        libraryBrowser?.SetExitRequested(value);
        if (value && libraryDialog is { IsVisible: true })
            libraryDialog.Close();
        Coordinator.SetExitRequested(value);
        Controls();
    }
    public Task<bool> RequestCloseAsync()
    {
        Dispatcher.VerifyAccess();
        if (allowClose) return Task.FromResult(true);
        if (closeTask is { IsCompleted: false }) return closeTask;
        closing = true; CloseError = null;
        Coordinator.SetExitRequested(true);
        Controls();
        Cancel(this, new RoutedEventArgs());
        return closeTask = CloseCoreAsync();
    }
    private async Task<bool> CloseCoreAsync()
    {
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Background);
        try
        {
            await command;
            await Coordinator.WhenIdleAsync();
            if (Current is { } media) await media.WhenIdleAsync();
            if (deletions?.GloballyBlocked == true || deletions?.HasIncompleteSuccess == true)
                throw new InvalidOperationException("삭제 복구 확인/재시도로 DB·저널 정리를 완료하세요.");
            if (Coordinator.View.Phase == SessionPhase.SaveFailed)
                throw new InvalidOperationException("저장 재시도 또는 감상 복귀로 현재 방문을 해결하세요.");
            var result = await Coordinator.LeaveAsync(Guid.NewGuid());
            if (result.Status is not (SessionStatus.Completed or SessionStatus.NoOp) || result.Error is not null
                || Coordinator.ReleaseError is not null)
                throw new InvalidOperationException(result.Error ?? Coordinator.ReleaseError ?? "현재 방문의 저장·삭제 복구를 확인하세요.");
            allowClose = true;
            Close();
            return true;
        }
        catch (Exception ex)
        {
            closeAfterRetry = Coordinator.View.Phase == SessionPhase.SaveFailed;
            CloseError = ex.Message;
            Status.Text = "종료하지 않았습니다. " + ex.Message;
            return false;
        }
        finally
        {
            closing = false;
            Coordinator.SetExitRequested(exitRequested);
            Controls();
        }
    }
    private async void OnClosing(object? s, CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        await RequestCloseAsync();
    }
}
