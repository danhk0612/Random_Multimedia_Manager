using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.Data;

public enum VisitCommitCheck { NotCommitted, Committed, PayloadMismatch }
public sealed partial class LibraryDatabase
{
    // Read-only confirmation: suppressed visits cannot be checked through ViewHistory.
    public VisitCommitCheck CheckVisitCommit(VisitCommitRequest request)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var hash = Scalar(null, "SELECT PayloadHash FROM VisitCommit WHERE VisitId=$id;",
                ("$id", Id(request.VisitId))) as byte[];
            return hash is null ? VisitCommitCheck.NotCommitted
                : hash.AsSpan().SequenceEqual(VisitPayload.Hash(request))
                    ? VisitCommitCheck.Committed : VisitCommitCheck.PayloadMismatch;
        }
    }

    // One short read snapshot; no transaction survives into media preparation.
    public CandidateSnapshot GetSessionSnapshot()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Execute(null, "BEGIN DEFERRED;");
            try
            {
                var categories = GetCategories();
                var sources = categories.SelectMany(c => GetSources(c.Id)).ToArray();
                var items = categories.SelectMany(c => GetItems(c.Id)).ToArray();
                var history = Read("SELECT MediaItemId,MAX(ViewedAtUtc) FROM ViewHistory GROUP BY MediaItemId;",
                    r => (Id: Guid.Parse(r.GetString(0)), Time: r.GetInt64(1)))
                    .ToDictionary(x => x.Id, x => x.Time);
                Execute(null, "COMMIT;");
                return new(categories, sources, items, history);
            }
            catch { Execute(null, "ROLLBACK;"); throw; }
        }
    }
}
