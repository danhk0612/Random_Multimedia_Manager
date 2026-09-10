namespace RandomMultimediaManager.Core;

public sealed record CandidateSnapshot(IReadOnlyList<Category> Categories,
    IReadOnlyList<CategorySource> Sources, IReadOnlyList<MediaItem> Items,
    IReadOnlyDictionary<Guid, long> LastViewed);

public static class CandidatePolicy
{
    public static bool Includes(CategorySource source, string pathKey)
    {
        string prefix = source.RootPathKey.TrimEnd('\\') + "\\";
        if (!pathKey.StartsWith(prefix, StringComparison.Ordinal)) return false;
        return source.IncludeSubdirectories || !pathKey[prefix.Length..].Contains('\\');
    }

    public static IReadOnlyList<MediaItem> GetCandidates(CandidateSnapshot snapshot,
        IReadOnlySet<Guid> selected, IReadOnlySet<Guid> seen, IReadOnlySet<string> quarantined,
        long now, int days = 7)
    {
        new AppSettings(days).Validate();
        var categories = snapshot.Categories.Where(c => c.IsEnabled && selected.Contains(c.Id))
            .Select(c => c.Id).ToHashSet();
        // Decimal subtraction also defines the boundary at the limits of Unix millisecond storage.
        decimal boundary = (decimal)now - days * 86400000L;
        return snapshot.Items.Where(i => categories.Contains(i.CategoryId) && !i.IsMissing
            && !i.IsRandomExcluded && !seen.Contains(i.Id) && !quarantined.Contains(i.PathKey)
            && snapshot.Sources.Any(s => s.CategoryId == i.CategoryId && s.IsEnabled && Includes(s, i.PathKey))
            && (days == 0 || !snapshot.LastViewed.TryGetValue(i.Id, out long last) || last <= boundary))
            .DistinctBy(i => i.Id).ToArray();
    }
}

public sealed record SessionSlot(Guid ItemId, string PathKey, bool Deleted = false);
public sealed record PendingVisit(Guid VisitId, Guid ItemId, long OpenedAtUtc,
    VisitOrigin Origin, bool SuppressHistory = false);

// Pure visit/path policy. Only the App coordinator mutates its owned instance.
public sealed class SessionPath
{
    public const int Limit = 10000;
    private readonly List<SessionSlot> slots = [];
    private readonly HashSet<Guid> seen = [];
    private readonly HashSet<Guid> selected;
    public Guid SessionId { get; } = Guid.NewGuid();
    public int Cursor { get; private set; } = -1;
    public IReadOnlyList<SessionSlot> Slots => slots.AsReadOnly();
    public IReadOnlySet<Guid> Seen => new HashSet<Guid>(seen);
    public IReadOnlySet<Guid> Selected => new HashSet<Guid>(selected);
    public PendingVisit? Pending { get; private set; }
    public SessionPath(IEnumerable<Guid> selectedCategories) => selected = selectedCategories.ToHashSet();
    public bool CanInsert => slots.Count < Limit && seen.Count < Limit;
    public int FindNeighbor(int direction, IReadOnlySet<Guid> missing)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        for (int i = Cursor + direction; i >= 0 && i < slots.Count; i += direction)
            if (!slots[i].Deleted && !missing.Contains(slots[i].ItemId)) return i;
        return -1;
    }
    public void Activate(MediaItem item, VisitOrigin origin, long now, int? existingIndex = null)
    {
        if (existingIndex is int index)
        {
            if (index < 0 || index >= slots.Count || slots[index].Deleted || slots[index].ItemId != item.Id)
                throw new InvalidOperationException("Invalid session slot.");
            Cursor = index;
        }
        else
        {
            if (!CanInsert) throw new InvalidOperationException("SessionLimit");
            slots.Insert(++Cursor, new SessionSlot(item.Id, item.PathKey));
        }
        seen.Add(item.Id);
        Pending = new PendingVisit(Guid.NewGuid(), item.Id, now, origin);
    }
    public bool SetSuppressed(Guid visitId, bool desired)
    {
        if (Pending?.VisitId != visitId) return false;
        Pending = Pending with { SuppressHistory = desired };
        return true;
    }
    public VisitCommitRequest Capture(PlaybackProgress progress, long now)
    {
        var visit = Pending ?? throw new InvalidOperationException("No visit.");
        progress.Validate();
        return new(visit.VisitId, visit.ItemId, now, visit.Origin, visit.SuppressHistory, progress);
    }
    // Only a confirmed OS success may call this. No DB/OS deletion is performed here.
    public void DeleteSucceeded(string pathKey)
    {
        for (int i = 0; i < slots.Count; i++)
            if (slots[i].PathKey == pathKey) slots[i] = slots[i] with { Deleted = true };
        if (Cursor >= 0 && slots[Cursor].Deleted) Pending = null;
    }
}
