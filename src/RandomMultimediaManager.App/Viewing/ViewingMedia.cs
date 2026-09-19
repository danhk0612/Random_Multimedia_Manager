using System.Windows;
using System.Windows.Controls;
using LibVLCSharp.Shared;
using RandomMultimediaManager.App.Media.Comic;
using RandomMultimediaManager.App.Sessions;
using RandomMultimediaManager.App.Video;
using RandomMultimediaManager.Core;
using MediaType = RandomMultimediaManager.Core.MediaType;

namespace RandomMultimediaManager.App.Viewing;

// Only the coordinator owns returned adapters. The fixed video panel is never reparented.
public sealed class ViewingMediaPreparer(Grid videoSurface, Action<ViewingMedia> activated,
    Action<ViewingMedia> released) : ISessionMediaPreparer
{
    public async Task<SessionPreparation> PrepareAsync(MediaItem item, PlaybackProgress? progress,
        SessionToken token, CancellationToken cancellationToken)
    {
        videoSurface.Dispatcher.VerifyAccess();
        if (item.MediaType == MediaType.Comic)
        {
            var result = await PreparedComic.PrepareAsync(item.Path, progress, cancellationToken);
            return new(result.Status == ComicPrepareStatus.Ready ? PreparationStatus.Ready :
                result.Status == ComicPrepareStatus.Cancelled ? PreparationStatus.Cancelled : PreparationStatus.Failed,
                token, result.Comic is null ? null : new ViewingMedia(item, token, result.Comic, progress, activated, released), result.Error);
        }
        var operation = new VideoOperation(token.SessionId, token.OperationId, token.ItemId);
        var video = await PreparedVideo.PrepareAsync(videoSurface, operation, item.Path, progress, true, cancellationToken);
        return new(video.Status == VideoPreparationStatus.Ready ? PreparationStatus.Ready :
            video.Status == VideoPreparationStatus.Cancelled ? PreparationStatus.Cancelled : PreparationStatus.Failed,
            token, video.Video is null ? null : new ViewingMedia(item, token, video.Video, activated, released), video.Error);
    }
}

public sealed class ViewingMedia : ISessionMedia
{
    private readonly SessionToken preparation;
    private readonly Action<ViewingMedia> activated;
    private readonly Action<ViewingMedia> released;
    private PreparedComic? comic;
    private readonly PlaybackProgress? restored;
    private ComicViewerWindow? comicWindow;
    private VideoSnapshot? paused;
    private bool disposed;
    public MediaItem Item { get; }
    public SessionToken? Token { get; private set; }
    public PreparedVideo? Video { get; }
    public VideoVisit VideoVisit => new(Video!.Operation, Token!.VisitId!.Value);
    public FrameworkElement? ComicContent { get; private set; }

    internal ViewingMedia(MediaItem item, SessionToken token, PreparedComic comic, PlaybackProgress? progress,
        Action<ViewingMedia> activated, Action<ViewingMedia> released)
    { Item = item; preparation = token; this.comic = comic; restored = progress; this.activated = activated; this.released = released; }
    internal ViewingMedia(MediaItem item, SessionToken token, PreparedVideo video,
        Action<ViewingMedia> activated, Action<ViewingMedia> released)
    { Item = item; preparation = token; Video = video; this.activated = activated; this.released = released; }

    public void Activate(SessionToken visit)
    {
        if (disposed || Token is not null || visit.VisitId is null || (visit with { VisitId = null }) != preparation)
            throw new InvalidOperationException("미디어 활성화 토큰이 일치하지 않습니다.");
        Token = visit;
        if (comic is not null)
        {
            comicWindow = new ComicViewerWindow();
            ComicContent = comicWindow.TakeSessionContent(comic, restored);
            comic = null;
        }
        activated(this);
        Video?.Activate(VideoVisit, 70, false);
    }
    private void Require(SessionToken token)
    {
        if (disposed || token != Token) throw new InvalidOperationException("현재 감상 토큰이 아닙니다.");
    }
    public Task WhenIdleAsync() => comicWindow?.WhenIdleAsync() ?? Task.CompletedTask;

    public PlaybackProgress Capture(SessionToken token)
    {
        Require(token);
        return Video is not null ? Video.CaptureProgress(VideoVisit) : comicWindow!.GetProgress();
    }
    public PlaybackProgress PauseAndCapture(SessionToken visit)
    {
        Require(visit);
        if (ComicContent is not null) ComicContent.IsEnabled = false;
        if (Video is not null)
        {
            paused = Video.Snapshot(VideoVisit);
            Video.SetPaused(VideoVisit, true);
            Video.SetMuted(VideoVisit, true);
        }
        return Capture(visit);
    }
    public void Resume(SessionToken visit)
    {
        Require(visit);
        if (ComicContent is not null) ComicContent.IsEnabled = true;
        if (Video is not null && paused is not null)
        {
            Video.SetMuted(VideoVisit, paused.Muted);
            if (paused.State == VLCState.Playing) Video.SetPaused(VideoVisit, false);
        }
        paused = null;
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        released(this);
        comic?.Dispose(); comic = null;
        comicWindow?.ReleaseSessionContent(); comicWindow = null;
        if (Video is not null) await Video.DisposeAsync();
    }
}
