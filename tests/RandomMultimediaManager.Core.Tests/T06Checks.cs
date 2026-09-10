using RandomMultimediaManager.Core;

internal static class T06Checks
{
    public static void Run()
    {
        static void Check(bool ok, string name) { if (!ok) throw new Exception(name); }
        var category = new Category(Guid.NewGuid(), "Comic", MediaType.Comic);
        var video = new Category(Guid.NewGuid(), "Video", MediaType.Video);
        var source = new CategorySource(Guid.NewGuid(), category.Id, @"C:\A", @"C:\A");
        var a = new MediaItem(Guid.NewGuid(), category.Id, MediaType.Comic, @"C:\A\a.zip", @"C:\A\A.ZIP", 1, 0);
        long now = 2000000000000;
        long boundary = now - 7 * 86400000L;
        var history = new Dictionary<Guid, long>();
        var snapshot = new CandidateSnapshot([category], [source, source with { Id = Guid.NewGuid() }], [a, a], history);
        var selected = new HashSet<Guid> { category.Id, video.Id };
        IReadOnlyList<MediaItem> Candidates(int days = 7) => CandidatePolicy.GetCandidates(snapshot, selected, new HashSet<Guid>(), new HashSet<string>(), now, days);
        foreach (var (time, count) in new[] { (boundary - 1, 1), (boundary, 1), (boundary + 1, 0), (now + 1, 0) })
        { history[a.Id] = time; Check(Candidates().Count == count, "UTC boundary/future"); }
        Check(Candidates(0).Count == 1, "zero days ignores future history, overlapping sources do not duplicate");
        history.Clear();
        Check(!CandidatePolicy.Includes(source, @"C:\AB\X.ZIP"), "component boundary");
        Check(!CandidatePolicy.Includes(source with { IncludeSubdirectories = false }, @"C:\A\B\X.ZIP"), "direct children");
        Check(CandidatePolicy.Includes(source with { RootPathKey = @"C:\" }, @"C:\X.ZIP"), "drive root");
        var b = a with { Id = Guid.NewGuid(), CategoryId = video.Id, MediaType = MediaType.Video };
        snapshot = snapshot with { Categories = [category, video], Sources = [source, source with { CategoryId = video.Id }], Items = [a, b] };
        Check(Candidates().Count == 2, "same path independent ItemIds, mixed categories");
        history[a.Id] = now;
        Check(Candidates().Single().Id == b.Id, "category history independence");
        history.Clear();
        snapshot = snapshot with { Items = [a with { IsFavorite = true }, b] };
        Check(Candidates().Count == 2, "favorite does not filter or duplicate");
        Check(CandidatePolicy.GetCandidates(snapshot, selected, new HashSet<Guid> { a.Id }, new HashSet<string>(), now).Single().Id == b.Id, "Seen");
        Check(CandidatePolicy.GetCandidates(snapshot, selected, new HashSet<Guid>(), new HashSet<string> { a.PathKey }, now).Count == 0, "quarantine all categories");
        snapshot = snapshot with { Items = [a with { IsRandomExcluded = true }, b with { IsMissing = true }] };
        Check(Candidates(0).Count == 0, "zero days retains excluded/missing");
        snapshot = snapshot with { Items = [a], Categories = [category with { IsEnabled = false }] };
        Check(Candidates().Count == 0, "disabled category");
        snapshot = snapshot with { Categories = [category], Sources = [source with { IsEnabled = false }] };
        Check(Candidates().Count == 0, "disabled source");
        snapshot = snapshot with { Sources = [] };
        Check(Candidates().Count == 0, "removed source");
        Console.WriteLine("PASS: T06 candidate boundaries, mixed categories, source scope, filters");

        var session = new SessionPath(selected);
        selected.Clear();
        Check(session.Selected.Count == 2, "fixed selected set");
        var visits = new HashSet<Guid>();
        session.Activate(a, VisitOrigin.Random, now); visits.Add(session.Pending!.VisitId);
        session.SetSuppressed(session.Pending.VisitId, true);
        Check(session.Capture(PlaybackProgress.Comic(3), now).SuppressHistory, "suppression captured");
        session.Activate(b, VisitOrigin.Random, now); visits.Add(session.Pending!.VisitId);
        session.Activate(a, VisitOrigin.Back, now, 0); visits.Add(session.Pending!.VisitId);
        Check(!session.Pending.SuppressHistory, "suppression reset");
        session.Activate(b, VisitOrigin.Forward, now, 1); visits.Add(session.Pending!.VisitId);
        Check(visits.Count == 4 && session.Slots.Count == 2, "four visits / two slots");
        session.Activate(a, VisitOrigin.Back, now, 0);
        var c = a with { Id = Guid.NewGuid(), PathKey = @"C:\A\C.ZIP" };
        session.Activate(c, VisitOrigin.Manual, now);
        Check(session.Slots.Select(s => s.ItemId).SequenceEqual(new[] { a.Id, c.Id, b.Id }), "manual preserves forward");
        Check(session.FindNeighbor(1, new HashSet<Guid> { b.Id }) == -1, "skip missing");
        session.DeleteSucceeded(a.PathKey);
        Check(session.Slots[0].Deleted && session.Slots[2].Deleted && session.Pending?.ItemId == c.Id, "same-path tombstones");
        session.DeleteSucceeded(c.PathKey);
        Check(session.Pending is null && session.Cursor == 1 && session.Seen.Count == 3, "deleted current keeps cursor and seen");

        var limit = new SessionPath([]);
        for (int i = 0; i < SessionPath.Limit; i++) limit.Activate(a, VisitOrigin.Manual, now);
        Check(!limit.CanInsert && limit.Slots.Count == 10000 && limit.Seen.Count == 1, "slot limit");
        limit.Activate(a, VisitOrigin.Back, now, 9998);
        Check(limit.Cursor == 9998, "Back allowed at limit");
        var unique = new SessionPath([]);
        for (int i = 0; i < SessionPath.Limit; i++) unique.Activate(a with { Id = Guid.NewGuid() }, VisitOrigin.Manual, now);
        Check(!unique.CanInsert && unique.Seen.Count == 10000, "Seen limit");
        Console.WriteLine("PASS: T06 visit identity, Forward preservation, deletion and exact limits");
    }
}
