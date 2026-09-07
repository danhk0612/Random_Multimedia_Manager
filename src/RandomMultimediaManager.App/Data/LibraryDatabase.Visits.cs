using Microsoft.Data.Sqlite;
using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.Data;

public sealed partial class LibraryDatabase
{
    public CommitResult CommitVisit(VisitCommitRequest request)
    {
        try
        {
            Id(request.VisitId); Id(request.ItemId);
            if (!Enum.IsDefined(request.Origin)) throw new ArgumentException("잘못된 방문 종류입니다.");
            request.Progress.Validate();
            byte[] hash = VisitPayload.Hash(request);
            return Write(transaction =>
            {
                var existing = Scalar(transaction, "SELECT PayloadHash FROM VisitCommit WHERE VisitId=$id;",
                    ("$id", Id(request.VisitId))) as byte[];
                if (existing != null)
                    return existing.AsSpan().SequenceEqual(hash)
                        ? new CommitResult(CommitStatus.AlreadyCommitted)
                        : new CommitResult(CommitStatus.Failed, "같은 VisitId의 저장 내용이 다릅니다.");
                Execute(transaction, "INSERT INTO VisitCommit VALUES($visit,$item,$hash);",
                    ("$visit", Id(request.VisitId)), ("$item", Id(request.ItemId)), ("$hash", hash));
                SaveProgress(transaction, request.ItemId, request.Progress, request.ViewedAtUtc);
                if (!request.SuppressHistory)
                    Execute(transaction, "INSERT INTO ViewHistory VALUES($visit,$item,$time,$origin);",
                        ("$visit", Id(request.VisitId)), ("$item", Id(request.ItemId)),
                        ("$time", request.ViewedAtUtc), ("$origin", request.Origin.ToString()));
                return new CommitResult(CommitStatus.Committed);
            });
        }
        catch (Exception ex) when (ex is SqliteException or ArgumentException or InvalidOperationException)
        {
            return new CommitResult(CommitStatus.Failed, ex.Message);
        }
    }

    public void SaveProgress(Guid itemId, PlaybackProgress progress, long updatedAtUtc)
    {
        progress.Validate();
        Write(transaction => { SaveProgress(transaction, itemId, progress, updatedAtUtc); return 0; });
    }
    private void SaveProgress(SqliteTransaction transaction, Guid itemId,
        PlaybackProgress progress, long updatedAtUtc) => Execute(transaction, """
        INSERT INTO PlaybackProgress VALUES($item,$type,$page,$offset,$position,$time)
        ON CONFLICT(MediaItemId) DO UPDATE SET MediaType=excluded.MediaType,
        ComicPageIndex=excluded.ComicPageIndex, ComicPageOffset=excluded.ComicPageOffset,
        VideoPositionMs=excluded.VideoPositionMs, UpdatedAtUtc=excluded.UpdatedAtUtc;
        """, ("$item", Id(itemId)), ("$type", progress.MediaType.ToString()),
        ("$page", progress.ComicPageIndex), ("$offset", progress.ComicPageOffset),
        ("$position", progress.VideoPositionMs), ("$time", updatedAtUtc));

    // Caller (T12) supplies the durable Succeeded journal snapshot and owns path isolation.
    // This never deletes an OS file and never infers success from a missing path.
    public CommitResult ApplyDeletion(Guid operationId, string pathKey, IReadOnlyCollection<Guid> itemIds)
    {
        try
        {
            Id(operationId);
            if (string.IsNullOrWhiteSpace(pathKey) || itemIds.Count == 0)
                throw new ArgumentException("삭제 대상이 비어 있습니다.");
            var targets = itemIds.Distinct().Select(Id).ToArray();
            return Write(transaction =>
            {
                if (Scalar(transaction, "SELECT OperationId FROM AppliedDeletion WHERE OperationId=$id;",
                    ("$id", Id(operationId))) is string)
                    return new CommitResult(CommitStatus.AlreadyCommitted);
                foreach (string item in targets)
                {
                    RequireOne(Execute(transaction,
                        "UPDATE MediaItem SET IsMissing=1 WHERE Id=$id AND PathKey=$path;",
                        ("$id", item), ("$path", pathKey)));
                    Execute(transaction, """
                        DELETE FROM ViewHistory WHERE MediaItemId=$id;
                        DELETE FROM PlaybackProgress WHERE MediaItemId=$id;
                        DELETE FROM VisitCommit WHERE MediaItemId=$id;
                        """, ("$id", item));
                }
                Execute(transaction, "INSERT INTO AppliedDeletion VALUES($id);", ("$id", Id(operationId)));
                return new CommitResult(CommitStatus.Committed);
            });
        }
        catch (Exception ex) when (ex is SqliteException or ArgumentException or InvalidOperationException)
        {
            return new CommitResult(CommitStatus.Failed, ex.Message);
        }
    }

    // Only after T12 has confirmed the corresponding journal file was removed.
    public void ForgetAppliedDeletion(Guid operationId) => Write(transaction =>
        Execute(transaction, "DELETE FROM AppliedDeletion WHERE OperationId=$id;", ("$id", Id(operationId))));
}
