using System.ComponentModel;
using RandomMultimediaManager.App.Deletion;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LibVLCSharp.Shared.Structures;
using Microsoft.Win32;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Sessions;
using RandomMultimediaManager.App.Video;
using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.Viewing;

public partial class ViewingWindow : Window
{
    private readonly LibraryDatabase database;
    private readonly DeletionService? deletions;
    public SessionCoordinator Coordinator { get; }
    public ViewingMedia? Current { get; private set; }
    private readonly DispatcherTimer timer;
    private Task command = Task.CompletedTask;
    private Task<bool>? closeTask;
    private bool exitRequested;
    public string? CloseError { get; private set; }
    private bool busy, allowClose, closing, seeking, refreshing, fullscreen;
    private DateTime lastCheckpoint = DateTime.UtcNow;
    private PlaybackProgress? saved;
    private SessionToken? savedToken;
    private WindowState previousState;

    public ViewingWindow(LibraryDatabase database, DeletionService? deletions = null)
    {
        this.database = database;
        this.deletions = deletions;
        InitializeComponent();
        Coordinator = new(database, new ViewingMediaPreparer(VideoSurface, OnMediaActivated, Released));
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
            async (_, _) => await Tick(), Dispatcher);
        Loaded += async (_, _) => await Run(async () =>
        {
            Categories.ItemsSource = (await Task.Run(database.GetCategories)).Where(c => c.IsEnabled).ToArray();
            var settings = await Task.Run(database.GetSettings);
            Days.Text = settings.HistoryExclusionDays.ToString();
            ResumeModeBox.SelectedIndex = settings.ResumeMode == ResumeMode.Resume ? 0 : 1;
        });
        Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    private void OnMediaActivated(ViewingMedia media)
    {
        Current = media;
        ComicSurface.Content = media.ComicContent;
        VideoControls.Visibility = media.Video is null ? Visibility.Collapsed : Visibility.Visible;
        Volume.Value = 70; Muted.IsChecked = false; Rate.SelectedIndex = 1;
        saved = null; savedToken = null;
        ExternalSubtitles.ItemsSource = null; AudioTracks.ItemsSource = null; SubtitleTracks.ItemsSource = null;
        Title = $"랜덤 감상 — {Path.GetFileName(media.Item.Path)}";
    }
    private void Released(ViewingMedia media)
    {
        if (!ReferenceEquals(Current, media)) return;
        Current = null; ComicSurface.Content = null;
        VideoControls.Visibility = Visibility.Collapsed;
    }
    private void Controls()
    {
        DeleteButton.IsEnabled = !busy && !closing && !exitRequested && Current is not null && deletions is not null
            && !database.IsDeletionBlocked(Current.Item.PathKey);
        RecoveryButton.IsEnabled = !busy && !closing && !exitRequested && deletions?.Pending.Count > 0;
        bool failed = Coordinator.View.Phase == SessionPhase.SaveFailed;
        SessionControls.IsEnabled = SettingsControls.IsEnabled = !busy && !closing && !exitRequested && !failed;
        VideoControls.IsEnabled = !busy && !closing && !exitRequested && !failed;
        if (Current?.ComicContent is { } comic) comic.IsEnabled = !busy && !closing && !exitRequested && !failed;
        RetryButton.IsEnabled = ResumeButton.IsEnabled = !busy && !closing && !exitRequested && failed;
    }
    // Admission is synchronous; navigation never queues behind another command. A checkpoint
    // already admitted finishes before any transition can capture/commit a later position.
    private async Task Run(Func<Task> action)
    {
        if (busy || closing || exitRequested) return;
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
    private async Task Navigate(Func<Task<SessionResult>> action)
    {
        var before = Current;
        Status.Text = "감상 전환 준비 중… 준비 중에는 ‘준비 취소’를 사용할 수 있습니다.";
        // Let WPF render the pending state before entering native preparation calls.
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Background);
        if (closing || exitRequested) return;
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
            if (!ReferenceEquals(before, current) && current.Video is not null)
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
        if (dialog.ShowDialog() != true || dialog.Selection is not { } mode || closing || exitRequested) return;
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
        await DeletionDialogs.RecoverAsync(deletions, this, Coordinator);
        Status.Text = deletions.Pending.Count == 0 ? "삭제 복구 처리를 완료했습니다." : "미해결 삭제 경로의 격리를 유지합니다.";
        Suppressed.IsChecked = Coordinator.View.Pending?.SuppressHistory == true;
    });
    private async void Start(object s, RoutedEventArgs e) => await Run(() => Navigate(() => Coordinator.StartRandomAsync(Guid.NewGuid(), Categories.SelectedItems.Cast<Category>().Select(c => c.Id))));
    private async void Next(object s, RoutedEventArgs e) => await Run(() => Navigate(() => Coordinator.NextAsync(Guid.NewGuid())));
    private async void Previous(object s, RoutedEventArgs e) => await Run(() => Navigate(() => Coordinator.PreviousAsync(Guid.NewGuid())));
    private void Cancel(object s, RoutedEventArgs e)
    { if (Coordinator.PreparingToken is { } t) Coordinator.CancelOpening(t.SessionId, t.OperationId); }
    private async void Retry(object s, RoutedEventArgs e) => await Run(async () =>
    {
        await Navigate(() => Coordinator.RetryAsync(Guid.NewGuid()));

    });
    private async void ResumeFailed(object s, RoutedEventArgs e) => await Run(async () =>
    {
        await Navigate(() => Coordinator.ResumeAfterSaveFailureAsync(Guid.NewGuid()));
        if (Coordinator.View.Phase == SessionPhase.Active) closing = false;
    });
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
        if (busy || closing || exitRequested || Coordinator.View.Phase != SessionPhase.Active) return;
        try
        {
            if (Current?.Video is { } video)
            {
                var snapshot = video.Snapshot(Current.VideoVisit);
                if (!seeking) { Position.Maximum = Math.Max(1, snapshot.DurationMs); Position.Value = snapshot.Progress.VideoPositionMs!.Value; }
                TimeText.Text = $"{TimeSpan.FromMilliseconds(snapshot.Progress.VideoPositionMs!.Value):hh\\:mm\\:ss} / {TimeSpan.FromMilliseconds(snapshot.DurationMs):hh\\:mm\\:ss}  {snapshot.State}";
            }
            if (DateTime.UtcNow - lastCheckpoint >= TimeSpan.FromSeconds(5))
            { lastCheckpoint = DateTime.UtcNow; await Run(CheckpointAsync); }
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
    private async void LoadSubtitle(object s, RoutedEventArgs e)
    {
        if (busy || closing || exitRequested) return;
        var dialog = new OpenFileDialog { Filter="자막|*.srt;*.smi" };
        if (dialog.ShowDialog(this) == true) await Run(() => Subtitle(dialog.FileName));
    }
    private async void DisableSubtitles(object s, RoutedEventArgs e) => await VideoAction((v,t) => v.DisableSubtitles(t));
    private void BeginSeek(object s, MouseButtonEventArgs e) => seeking = true;
    private async void EndSeek(object s, MouseButtonEventArgs e) { await VideoAction((v,t) => v.Seek(t,(long)Position.Value)); seeking = false; }
    private void Fullscreen(object s, RoutedEventArgs e)
    {
        if (closing || exitRequested) return;
        if (!fullscreen) { previousState = WindowState; WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized; }
        else { WindowState = WindowState.Normal; WindowStyle = WindowStyle.SingleBorderWindow; WindowState = previousState; }
        fullscreen = !fullscreen;
    }
    public void SetExitRequested(bool value)
    {
        exitRequested = value;
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
