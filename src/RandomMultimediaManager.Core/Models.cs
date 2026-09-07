namespace RandomMultimediaManager.Core;

public enum MediaType { Comic, Video }
public enum VisitOrigin { Random, Manual, Back, Forward }
public enum ResumeMode { Resume, FromStart }
public enum CommitStatus { Committed, AlreadyCommitted, Failed }
public sealed record CommitResult(CommitStatus Status, string? Error = null);

public sealed record Category(Guid Id, string Name, MediaType MediaType, bool IsEnabled = true);
public sealed record CategorySource(Guid Id, Guid CategoryId, string RootPath, string RootPathKey,
    bool IncludeSubdirectories = true, bool IsEnabled = true);
public sealed record MediaItem(Guid Id, Guid CategoryId, MediaType MediaType, string Path, string PathKey,
    long FileSize, long LastWriteTimeUtc, bool IsFavorite = false,
    bool IsRandomExcluded = false, bool IsMissing = false);
public sealed record ViewHistory(Guid VisitId, Guid MediaItemId, long ViewedAtUtc, VisitOrigin Origin);

public sealed record PlaybackProgress(MediaType MediaType, int? ComicPageIndex,
    double? ComicPageOffset, long? VideoPositionMs)
{
    public static PlaybackProgress Comic(int page, double offset = 0) =>
        new(MediaType.Comic, page, offset, null);
    public static PlaybackProgress Video(long milliseconds) =>
        new(MediaType.Video, null, null, milliseconds);

    public void Validate()
    {
        bool valid = MediaType switch
        {
            MediaType.Comic => ComicPageIndex is >= 0 && ComicPageOffset is >= 0 and <= 1
                && VideoPositionMs is null,
            MediaType.Video => VideoPositionMs is >= 0 && ComicPageIndex is null && ComicPageOffset is null,
            _ => false
        };
        if (!valid) throw new ArgumentException("진행 위치의 타입 또는 범위가 올바르지 않습니다.");
    }
}
public sealed record StoredProgress(PlaybackProgress Position, long UpdatedAtUtc);
public sealed record VisitCommitRequest(Guid VisitId, Guid ItemId, long ViewedAtUtc,
    VisitOrigin Origin, bool SuppressHistory, PlaybackProgress Progress);
public sealed record AppSettings(int HistoryExclusionDays = 7, ResumeMode ResumeMode = ResumeMode.Resume)
{
    public void Validate()
    {
        if (HistoryExclusionDays is < 0 or > 36500 || !Enum.IsDefined(ResumeMode))
            throw new ArgumentException("설정값이 허용 범위를 벗어났습니다.");
    }
}
