using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.Video;

public sealed record VideoOperation(Guid SessionId, Guid OperationId, Guid ItemId);
public sealed record VideoVisit(VideoOperation Operation, Guid VisitId);
public enum VideoPreparationStatus { Ready, Failed, Cancelled }
public sealed record VideoPreparation(VideoPreparationStatus Status, PreparedVideo? Video, string? Error);
public sealed record VideoSnapshot(VideoVisit Visit, PlaybackProgress Progress, long DurationMs,
    VLCState State, int Volume, bool Muted, float Rate, bool Seekable, string? Error);

/// <summary>
/// One owned decoder and its permanently attached HWND. All public calls require the owning
/// WPF dispatcher. The caller owns at most current + one candidate; T11 owns session/DB policy.
/// </summary>
public sealed class PreparedVideo : IAsyncDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly Panel parent;
    private readonly LibVLC engine;
    private readonly Media media;
    private readonly MediaPlayer player;
    private readonly VideoView view;
    private readonly ConcurrentQueue<string> logs = new();
    private readonly EventHandler errorHandler;
    private readonly EventHandler<LogEventArgs> logHandler;
    private readonly CancellationToken preparationCancellation;
    private VideoVisit? visit;
    private bool ready;
    private bool retiring;
    private int failed;
    private Task? disposal;

    public VideoOperation Operation { get; }
    public string Path { get; }
    public bool HardwareRequested { get; }
    public string NativeVersion { get; }
    public int PreparedVideoBlocks { get; private set; }
    public int PreparedAudioBlocks { get; private set; }
    public string[] Diagnostics => logs.ToArray();

    private PreparedVideo(Panel parent, VideoOperation operation, string path, bool hardware,
        CancellationToken cancellation, LibVLC engine, Media media, MediaPlayer player)
    {
        this.parent = parent;
        dispatcher = parent.Dispatcher;
        Operation = operation;
        Path = path;
        HardwareRequested = hardware;
        preparationCancellation = cancellation;
        this.engine = engine;
        this.media = media;
        this.player = player;
        NativeVersion = engine.Version;
        // Never call native APIs, dispatch synchronously, or mutate another slot from a VLC callback.
        errorHandler = (_, _) => Interlocked.Exchange(ref failed, 1);
        logHandler = (_, e) =>
        {
            logs.Enqueue($"{e.Level}: {e.Message}");
            while (logs.Count > 160) logs.TryDequeue(out _);
        };
        player.EncounteredError += errorHandler;
        engine.Log += logHandler;
        view = new VideoView { Visibility = Visibility.Hidden };
        parent.Children.Add(view);
    }

    public static async Task<VideoPreparation> PrepareAsync(Panel parent, VideoOperation operation,
        string path, PlaybackProgress? progress, bool hardware, CancellationToken cancellation)
    {
        parent.Dispatcher.VerifyAccess();
        PreparedVideo? owned = null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (operation.SessionId == Guid.Empty || operation.OperationId == Guid.Empty || operation.ItemId == Guid.Empty)
                throw new ArgumentException("미디어 작업 토큰이 비어 있습니다.");
            progress?.Validate();
            if (progress is not null && progress.MediaType != MediaType.Video)
                throw new ArgumentException("영상 진행 위치가 필요합니다.");
            if (!System.IO.Path.IsPathFullyQualified(path) || !File.Exists(path))
                throw new FileNotFoundException("로컬 영상 파일을 찾을 수 없습니다.", path);

            // Separate instances prevent a candidate's volume/configuration from changing current.
            // The selected output starts muted before its first buffer; do not rely on pre-Play Mute.
            var resources = await Task.Run(() => CreateNative(path, hardware));
            try { owned = new PreparedVideo(parent, operation, path, hardware, cancellation,
                resources.Engine, resources.Media, resources.Player); }
            catch { await Task.Run(() => { resources.Player.Dispose(); resources.Media.Dispose(); resources.Engine.Dispose(); }); throw; }
            cancellation.ThrowIfCancellationRequested();
            parent.UpdateLayout();
            await parent.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            owned.view.MediaPlayer = owned.player;
            if (owned.player.Hwnd == IntPtr.Zero)
                throw new InvalidOperationException("숨김 WPF 영상 HWND 생성 실패: T02 준비 계약 검증 필요.");
            if (!owned.player.Play()) throw new InvalidOperationException("엔진이 재생 시작을 거부했습니다.");
            await owned.WaitForDecodedAsync(cancellation);
            await owned.RestorePositionAsync(progress?.VideoPositionMs ?? 0, cancellation);
            owned.player.SetPause(true);
            await owned.WaitAsync(() => owned.player.State == VLCState.Paused, cancellation);
            if (owned.media.Tracks.Any(t => t.TrackType == TrackType.Audio) &&
                (!owned.player.Mute || owned.player.Volume != 0))
                throw new InvalidOperationException("준비 출력의 음소거/볼륨 0 확인 실패.");
            cancellation.ThrowIfCancellationRequested();
            owned.CheckFailure();
            owned.PreparedVideoBlocks = owned.media.Statistics.DecodedVideo;
            owned.PreparedAudioBlocks = owned.media.Statistics.DecodedAudio;
            owned.ready = true;
            return new(VideoPreparationStatus.Ready, owned, null);
        }
        catch (Exception ex)
        {
            if (owned is not null) await owned.DisposeAsync();
            return cancellation.IsCancellationRequested
                ? new(VideoPreparationStatus.Cancelled, null, null)
                : new(VideoPreparationStatus.Failed, null, ex.Message +
                    (owned is null ? "" : "\n" + string.Join("\n", owned.Diagnostics.TakeLast(12))));
        }
    }

    private static (LibVLC Engine, Media Media, MediaPlayer Player) CreateNative(string path, bool hardware)
    {
        LibVLCSharp.Shared.Core.Initialize();
        var engine = new LibVLC(true, "--aout=directsound,none", "--directx-volume=0", "--mute",
            "--no-spdif", "--no-volume-save", "--no-video-title-show", "--no-sub-autodetect-file", "--stats");
        Media? media = null;
        MediaPlayer? player = null;
        try
        {
            media = new Media(engine, path, FromType.FromPath);
            player = new MediaPlayer(engine) { Media = media, EnableHardwareDecoding = hardware,
                EnableKeyInput = false, EnableMouseInput = false, Mute = true, Volume = 0 };
            return (engine, media, player);
        }
        catch { player?.Dispose(); media?.Dispose(); engine.Dispose(); throw; }
    }

    private Task WaitForDecodedAsync(CancellationToken cancellation) => WaitAsync(() =>
    {
        var stats = media.Statistics;
        bool hasAudio = media.Tracks.Any(t => t.TrackType == TrackType.Audio);
        return player.State == VLCState.Playing && player.CanPause && player.VoutCount > 0 &&
            stats.DecodedVideo > 0 && (!hasAudio || (stats.DecodedAudio > 0 && stats.PlayedAudioBuffers > 0));
    }, cancellation);

    private async Task RestorePositionAsync(long milliseconds, CancellationToken cancellation)
    {
        // Restore while still hidden and silent. An unsupported seek falls back to the start.
        if (!player.IsSeekable) return;
        long target = player.Length > 0 ? Math.Clamp(milliseconds, 0, player.Length) : milliseconds;
        var before = media.Statistics.DecodedVideo;
        player.Time = target;
        await WaitAsync(() => media.Statistics.DecodedVideo > before, cancellation);
    }

    private async Task WaitAsync(Func<bool> predicate, CancellationToken cancellation)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            CheckFailure();
            if (predicate()) return;
            if (player.State == VLCState.Ended) throw new InvalidOperationException("준비 완료 전에 영상이 끝났습니다.");
            if (watch.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("영상 준비 응답 시간 초과.");
            await Task.Delay(25, cancellation);
        }
    }

    private void CheckFailure()
    {
        if (Volatile.Read(ref failed) != 0 || player.State == VLCState.Error)
            throw new InvalidOperationException("영상 엔진 오류. 진단은 Windows 검증 결과와 함께 확인하세요.");
    }

    public bool CanActivate(VideoOperation operation)
    {
        dispatcher.VerifyAccess();
        return !retiring && ready && visit is null && operation == Operation &&
            !preparationCancellation.IsCancellationRequested && Volatile.Read(ref failed) == 0 &&
            player.State == VLCState.Paused;
    }

    // Call after T11 has validated its generation and committed the previous visit.
    // This does not replace Media, move HWNDs, stop/reopen, or start another preparation.
    public void Activate(VideoVisit token, int volume, bool muted, UIElement? overlay = null)
    {
        dispatcher.VerifyAccess();
        if (token.VisitId == Guid.Empty || !CanActivate(token.Operation))
            throw new InvalidOperationException("만료되었거나 준비되지 않은 영상입니다.");
        visit = token;
        ready = false;
        view.Content = overlay;
        view.Visibility = Visibility.Visible;
        player.Volume = Math.Clamp(volume, 0, 100);
        player.Mute = muted;
        player.SetPause(false);
    }

    private void RequireVisit(VideoVisit token)
    {
        dispatcher.VerifyAccess();
        if (retiring || token != visit) throw new InvalidOperationException("현재 영상 방문 토큰이 아닙니다.");
    }

    public VideoSnapshot Snapshot(VideoVisit token)
    {
        RequireVisit(token);
        return new(token, PlaybackProgress.Video(Math.Max(0, player.Time)), Math.Max(0, player.Length),
            player.State, player.Volume, player.Mute, player.Rate, player.IsSeekable,
            Volatile.Read(ref failed) == 0 ? null : "현재 영상 재생 오류");
    }

    public PlaybackProgress CaptureProgress(VideoVisit token) => Snapshot(token).Progress;
    public void SetPaused(VideoVisit token, bool paused) { RequireVisit(token); player.SetPause(paused); }
    public void Play(VideoVisit token) { RequireVisit(token); if (!player.Play()) throw new InvalidOperationException("재생 실패"); }
    public bool Seek(VideoVisit token, long milliseconds)
    {
        RequireVisit(token);
        if (!player.IsSeekable) return false;
        player.Time = player.Length > 0 ? Math.Clamp(milliseconds, 0, player.Length) : Math.Max(0, milliseconds);
        return true;
    }
    public void SetVolume(VideoVisit token, int volume) { RequireVisit(token); player.Volume = Math.Clamp(volume, 0, 100); }
    public void SetMuted(VideoVisit token, bool muted) { RequireVisit(token); player.Mute = muted; }
    public bool SetRate(VideoVisit token, float rate) { RequireVisit(token); return player.SetRate(rate) == 0; }
    public TrackDescription[] AudioTracks(VideoVisit token) { RequireVisit(token); return player.AudioTrackDescription; }
    public TrackDescription[] SubtitleTracks(VideoVisit token) { RequireVisit(token); return player.SpuDescription; }
    public bool SetAudioTrack(VideoVisit token, int id) { RequireVisit(token); return player.SetAudioTrack(id); }
    public bool SetSubtitleTrack(VideoVisit token, int id) { RequireVisit(token); return player.SetSpu(id); }

    public async Task StopAsync(VideoVisit token)
    {
        RequireVisit(token);
        // Caller must serialize commands until completion. Polling/native access is suspended.
        retiring = true;
        player.Mute = true;
        try { await Task.Run(player.Stop); }
        finally { retiring = false; }
        // Stop does not end the visit. The validation host reapplies desired volume/mute on Play.
    }

    public ValueTask DisposeAsync()
    {
        dispatcher.VerifyAccess();
        return new ValueTask(disposal ??= ReleaseAsync());
    }

    private async Task ReleaseAsync()
    {
        retiring = true;
        ready = false;
        visit = null;
        view.Visibility = Visibility.Hidden;
        view.Content = null;
        player.Mute = true;
        player.Volume = 0;
        player.EncounteredError -= errorHandler;
        // Stop must finish while the bound HWND still exists. No UI/native callbacks block on it.
        await Task.Run(player.Stop);
        view.MediaPlayer = null;
        parent.Children.Remove(view);
        view.Dispose();
        engine.Log -= logHandler;
        await Task.Run(() => { player.Dispose(); media.Dispose(); engine.Dispose(); });
    }
}
