using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;
using Microsoft.Win32;

namespace RandomMultimediaManager.App.Video;

// An engine validation harness, not the T11 session coordinator. IDs have no DB association.
public partial class VideoValidationWindow : Window
{
    private readonly Guid sessionId = Guid.NewGuid();
    private readonly DispatcherTimer timer;
    private PreparedVideo? current;
    private PreparedVideo? staged;
    private VideoVisit? visit;
    private VideoOperation? operation;
    private CancellationTokenSource? cancellation;
    private Task preparing = Task.CompletedTask;
    private Task command = Task.CompletedTask;
    private Task? shutdown;
    private bool busy;
    private bool closing;
    private bool allowClose;
    private bool seeking;
    private bool refreshingTracks;
    private WindowState previousState;
    private bool fullscreen;

    public VideoValidationWindow()
    {
        InitializeComponent();
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
            (_, _) => RefreshSnapshot(), Dispatcher);
        timer.Start();
    }

    private async void PrepareFile(object sender, RoutedEventArgs e)
    {
        if (busy || closing || staged is not null) return;
        var dialog = new OpenFileDialog { Filter = "영상|*.mp4;*.mkv|모든 파일|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        var request = new VideoOperation(sessionId, Guid.NewGuid(), Guid.NewGuid());
        operation = request;
        cancellation = new CancellationTokenSource();
        busy = true;
        UpdateControls();
        preparing = PrepareSelectedAsync(request, dialog.FileName, SoftwareOnly.IsChecked != true, cancellation.Token);
        await preparing;
        if (staged is null) { cancellation?.Dispose(); cancellation = null; }
        busy = false;
        UpdateControls();
    }

    private async Task PrepareSelectedAsync(VideoOperation request, string path, bool hardware, CancellationToken token)
    {
        Status.Text = "대상 준비 중 — 기존 영상은 유지됩니다.";
        var result = await PreparedVideo.PrepareAsync(VideoSurface, request, path, null, hardware, token);
        // A cancelled generation never gets to change status, display, sound, or the other slot.
        if (closing || operation != request || token.IsCancellationRequested)
        {
            if (result.Video is not null) await result.Video.DisposeAsync();
            return;
        }
        staged = result.Video;
        Status.Text = result.Status switch
        {
            VideoPreparationStatus.Ready => $"Ready: {System.IO.Path.GetFileName(path)} — 숨김·음소거·일시정지, 활성화 대기",
            VideoPreparationStatus.Cancelled => "준비 취소",
            _ => $"준비 실패 (기존 영상 유지): {result.Error}"
        };
        if (staged is not null)
            Diagnostics.Text = $"libVLC {staged.NativeVersion}; HW 요청={staged.HardwareRequested}; " +
                $"Ready decoded video/audio={staged.PreparedVideoBlocks}/{staged.PreparedAudioBlocks}\n" + string.Join('\n', staged.Diagnostics);
    }

    private async void ActivatePrepared(object sender, RoutedEventArgs e)
    {
        if (busy || closing || staged is null || operation is null) return;
        if (!staged.CanActivate(operation)) { Status.Text = "준비 대상이 만료되었거나 실패했습니다. 폐기 후 다시 준비하세요."; return; }
        busy = true;
        UpdateControls();
        command = ActivateAsync();
        try { await command; }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { busy = false; UpdateControls(); }
    }

    private async Task ActivateAsync()
    {
        var next = staged!;
        var old = current;
        var oldVisit = visit;
        var previous = old is not null && oldVisit is not null ? old.Snapshot(oldVisit) : null;
        var nextVisit = new VideoVisit(next.Operation, Guid.NewGuid());
        try
        {
            if (old is not null && oldVisit is not null) { old.SetPaused(oldVisit, true); old.SetMuted(oldVisit, true); }
            // No CommitVisit here: T11 must insert its successful save before Activate.
            next.Activate(nextVisit, (int)Volume.Value, Muted.IsChecked == true, CreateOverlay());
        }
        catch
        {
            staged = null;
            operation = null;
            await next.DisposeAsync();
            cancellation?.Dispose();
            cancellation = null;
            if (old is not null && oldVisit is not null && previous is not null)
            {
                old.SetMuted(oldVisit, previous.Muted);
                old.SetPaused(oldVisit, previous.State != VLCState.Playing);
            }
            throw;
        }
        current = next;
        visit = nextVisit;
        Rate.SelectedIndex = 1;
        staged = null;
        operation = null;
        cancellation?.Dispose();
        cancellation = null;
        if (old is not null) await old.DisposeAsync();
        Status.Text = $"활성: {System.IO.Path.GetFileName(next.Path)} / Visit {nextVisit.VisitId}";
        RefreshTracks(this, new RoutedEventArgs());
    }

    private UIElement CreateOverlay()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Black };
        var pause = new Button { Content = "일시정지", Margin = new Thickness(6) };
        pause.Click += Pause;
        var full = new Button { Content = "전체화면 / 복귀", Margin = new Thickness(6) };
        full.Click += ToggleFullscreen;
        panel.PreviewKeyDown += OnKeyDown;
        panel.Children.Add(pause);
        panel.Children.Add(full);
        return panel;
    }

    private async void CancelPreparation(object sender, RoutedEventArgs e)
    {
        if (closing || (!preparing.IsCompleted && operation is null)) return;
        if (busy && preparing.IsCompleted) return;
        operation = null; // Invalidate before sending cancellation.
        cancellation?.Cancel();
        if (!preparing.IsCompleted) { Status.Text = "준비 취소 중"; return; }
        if (busy) return;
        busy = true;
        UpdateControls();
        command = DiscardStagedAsync();
        try { await command; Status.Text = "준비 대상 폐기 완료. 기존 영상 유지."; }
        catch (Exception ex) { Status.Text = $"해제 실패: {ex.Message}"; }
        finally { busy = false; UpdateControls(); }
    }

    private async Task DiscardStagedAsync()
    {
        var owned = staged;
        staged = null;
        if (owned is not null) await owned.DisposeAsync();
        cancellation?.Dispose();
        cancellation = null;
    }

    private void Apply(Action<PreparedVideo, VideoVisit> action)
    {
        if (busy || closing || current is null || visit is null) return;
        try { action(current, visit); }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private void Play(object sender, RoutedEventArgs e) => Apply((v, t) =>
        { v.Play(t); v.SetVolume(t, (int)Volume.Value); v.SetMuted(t, Muted.IsChecked == true); });
    private void Pause(object sender, RoutedEventArgs e) => Apply((v, t) => v.SetPaused(t, true));
    private async void Stop(object sender, RoutedEventArgs e)
    {
        if (busy || closing || current is null || visit is null) return;
        busy = true;
        UpdateControls();
        command = current.StopAsync(visit);
        try { await command; }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { busy = false; UpdateControls(); }
    }
    private void SeekBack(object sender, RoutedEventArgs e) => SeekRelative(-10000);
    private void SeekForward(object sender, RoutedEventArgs e) => SeekRelative(10000);
    private void SeekRelative(long offset) => Apply((v, t) => { if (!v.Seek(t, v.CaptureProgress(t).VideoPositionMs!.Value + offset)) Status.Text = "탐색 불가"; });
    private void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => Apply((v, t) => v.SetVolume(t, (int)e.NewValue));
    private void MuteChanged(object sender, RoutedEventArgs e) => Apply((v, t) => v.SetMuted(t, Muted.IsChecked == true));
    private void RateChanged(object sender, SelectionChangedEventArgs e) => Apply((v, t) =>
    {
        float rate = float.Parse((string)((ComboBoxItem)Rate.SelectedItem).Tag, CultureInfo.InvariantCulture);
        if (!v.SetRate(t, rate)) Status.Text = "배속 설정 실패";
    });
    private void RefreshTracks(object sender, RoutedEventArgs e)
    {
        if (current is null || visit is null || closing) return;
        refreshingTracks = true;
        try { Audio.ItemsSource = current.AudioTracks(visit); Subtitles.ItemsSource = current.SubtitleTracks(visit); }
        finally { refreshingTracks = false; }
    }
    private void AudioChanged(object sender, SelectionChangedEventArgs e)
    { if (!refreshingTracks && Audio.SelectedItem is TrackDescription track) Apply((v, t) => { if (!v.SetAudioTrack(t, track.Id)) Status.Text = "오디오 트랙 선택 실패"; }); }
    private void SubtitleChanged(object sender, SelectionChangedEventArgs e)
    { if (!refreshingTracks && Subtitles.SelectedItem is TrackDescription track) Apply((v, t) => { if (!v.SetSubtitleTrack(t, track.Id)) Status.Text = "자막 트랙 선택 실패"; }); }
    private void BeginSeek(object sender, MouseButtonEventArgs e) => seeking = true;
    private void EndSeek(object sender, MouseButtonEventArgs e)
    { Apply((v, t) => { if (!v.Seek(t, (long)Position.Value)) Status.Text = "탐색 불가"; }); seeking = false; }

    private void RefreshSnapshot()
    {
        if (busy || closing || current is null || visit is null) return;
        var snapshot = current.Snapshot(visit);
        if (!seeking) { Position.Maximum = Math.Max(1, snapshot.DurationMs); Position.Value = snapshot.Progress.VideoPositionMs!.Value; }
        Position.IsEnabled = snapshot.Seekable;
        TimeText.Text = $"{TimeSpan.FromMilliseconds(snapshot.Progress.VideoPositionMs!.Value):hh\\:mm\\:ss} / " +
            $"{TimeSpan.FromMilliseconds(snapshot.DurationMs):hh\\:mm\\:ss}  {snapshot.State}  {snapshot.Rate:0.0}×";
        if (snapshot.Error is not null) Status.Text = snapshot.Error;
    }

    private void UpdateControls()
    {
        PrepareButton.IsEnabled = !busy && !closing && staged is null;
        ActivateButton.IsEnabled = !busy && !closing && staged is not null;
        Controls.IsEnabled = !busy && !closing && current is not null;
        Position.IsEnabled = !busy && !closing && current is not null;
    }
    private void ToggleFullscreen(object sender, RoutedEventArgs e)
    {
        if (closing) return;
        if (!fullscreen) { previousState = WindowState; WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized; }
        else { WindowState = WindowState.Normal; WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize; WindowState = previousState; }
        fullscreen = !fullscreen;
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Escape && fullscreen) { ToggleFullscreen(sender, e); e.Handled = true; } }

    private async void ReleaseMedia(object sender, RoutedEventArgs e)
    {
        if (busy || closing) return;
        busy = true;
        UpdateControls();
        command = ReleaseAllAsync();
        try { await command; Status.Text = "모든 미디어 해제 완료. 테스트 복사본의 이동/삭제를 확인하세요."; }
        catch (Exception ex) { Status.Text = $"해제 실패: {ex.Message}"; }
        finally { busy = false; UpdateControls(); }
    }
    private async Task ReleaseAllAsync()
    {
        operation = null;
        cancellation?.Cancel();
        await preparing;
        await DiscardStagedAsync();
        var owned = current;
        current = null;
        visit = null;
        if (owned is not null) await owned.DisposeAsync();
    }
    public Task ShutdownAsync() => shutdown ??= ShutdownCoreAsync();
    private async Task ShutdownCoreAsync()
    {
        closing = true;
        timer.Stop();
        UpdateControls();
        operation = null;
        cancellation?.Cancel();
        await command;
        await ReleaseAllAsync();
        allowClose = true;
        Close();
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        try { await ShutdownAsync(); }
        catch (Exception ex) { Status.Text = $"종료 중 리소스 해제 실패: {ex.Message}"; }
    }
}
