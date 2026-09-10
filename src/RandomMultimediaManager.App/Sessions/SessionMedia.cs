using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.Sessions;

public sealed record SessionToken(Guid SessionId, Guid OperationId, Guid ItemId, Guid? VisitId = null);
public enum PreparationStatus { Ready, Failed, Cancelled }
public sealed record SessionPreparation(PreparationStatus Status, SessionToken Token,
    ISessionMedia? Media = null, string? Error = null);

// T11 implements the adapter on its dispatcher. Ready owns a hidden/muted, decoded object.
// Activate is a logical handoff, not a second open. Disposal must release all owned resources.
public interface ISessionMedia : IAsyncDisposable
{
    PlaybackProgress PauseAndCapture(SessionToken visit);
    void Resume(SessionToken visit);
    void Activate(SessionToken visit);
}
public interface ISessionMediaPreparer
{
    Task<SessionPreparation> PrepareAsync(MediaItem item, PlaybackProgress? progress,
        SessionToken token, CancellationToken cancellationToken);
}
public enum SessionStatus
{
    Completed, NoOp, NoCandidates, NoPrevious, SelectionRequired, SessionLimit,
    Busy, Stale, Failed, Cancelled, SaveFailed, CommitUnknown
}
public enum SessionPhase { Empty, Active, Opening, Saving, SaveFailed }
public sealed record SessionResult(SessionStatus Status, string? Error = null);
public sealed record SessionView(Guid? SessionId, SessionPhase Phase, int Cursor,
    IReadOnlyList<SessionSlot> Slots, IReadOnlySet<Guid> Seen, IReadOnlySet<Guid> Selected,
    PendingVisit? Pending, SessionToken? ActiveToken, VisitCommitRequest? FrozenCommit);
