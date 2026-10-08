using System.Collections.Frozen;
using RandomMultimediaManager.Core;
using RandomMultimediaManager.App.Deletion;

namespace RandomMultimediaManager.App.Data;

public sealed partial class LibraryDatabase
{
    private readonly HashSet<string> deletionPaths = new(StringComparer.Ordinal);
    private bool deletionRecoveryBlocked;
    private bool remoteDeletionBlocked;
    public bool IsDeletionBlocked(string pathKey)
    { lock (gate) return deletionRecoveryBlocked || deletionPaths.Contains(pathKey) || (remoteDeletionBlocked && IsRemoteOrUnknown(pathKey)); }
    public IReadOnlySet<string> DeletionPaths
    { get { return GetDeletionIsolationSnapshot().Paths; } }
    // Short-lived DB-only decision. Callers reuse it for one candidate/source batch,
    // never across awaits, scan apply, or a later command. No invalidation cache exists.
    public DeletionIsolationSnapshot GetDeletionIsolationSnapshot(bool includeCategories = false)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            using var transaction = connection.BeginTransaction();
            var localRoots = remoteDeletionBlocked ? ReadConfirmedLocalRoots(transaction) : new HashSet<string>(StringComparer.Ordinal);
            var paths = new HashSet<string>(deletionPaths, StringComparer.Ordinal);
            var categories = new HashSet<Guid>();
            var snapshot = new DeletionIsolationSnapshot(false, remoteDeletionBlocked, paths, localRoots, categories);
            if (remoteDeletionBlocked || (includeCategories && paths.Count != 0))
            {
                using var command = Command(transaction, "SELECT CategoryId,PathKey FROM MediaItem;");
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string key = reader.GetString(1);
                    if (!snapshot.IsBlocked(key)) continue;
                    if (remoteDeletionBlocked) paths.Add(key);
                    if (includeCategories) categories.Add(Guid.Parse(reader.GetString(0)));
                }
            }
            if (includeCategories && (remoteDeletionBlocked || paths.Count != 0))
            {
                // Index path ancestors once, instead of paths x sources descendant checks.
                var ancestors = new HashSet<string>(StringComparer.Ordinal);
                foreach (var key in paths)
                {
                    var path = WindowsPath.Normalize(key);
                    ancestors.Add(path.PathKey); ancestors.Add(path.RootKey);
                    for (int i = path.RootKey.Length; i < path.PathKey.Length; i++)
                        if (path.PathKey[i] == '\\') ancestors.Add(path.PathKey[..i]);
                }
                using var command = Command(transaction, "SELECT CategoryId,RootPathKey FROM CategorySource;");
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    if (snapshot.IsBlocked(reader.GetString(1)) || ancestors.Contains(reader.GetString(1)))
                        categories.Add(Guid.Parse(reader.GetString(0)));
            }
            transaction.Commit();
            return new DeletionIsolationSnapshot(deletionRecoveryBlocked, remoteDeletionBlocked,
                paths.ToFrozenSet(StringComparer.Ordinal), localRoots.ToFrozenSet(StringComparer.Ordinal), categories.ToFrozenSet());
        }
    }
    private HashSet<string> ReadConfirmedLocalRoots(Microsoft.Data.Sqlite.SqliteTransaction transaction)
    {
        var roots = new HashSet<string>(StringComparer.Ordinal);
        using var command = Command(transaction, "SELECT RootKey FROM StorageBinding WHERE Kind='Local' AND RequiresConfirmation=0;");
        using var reader = command.ExecuteReader();
        while (reader.Read()) roots.Add(reader.GetString(0));
        return roots;
    }
    public void BlockDeletionRecovery(bool blocked)
    { lock (gate) deletionRecoveryBlocked = blocked; }
    public void QuarantineDeletion(string pathKey)
    { lock (gate) deletionPaths.Add(pathKey); }
    public void ReleaseDeletion(string pathKey)
    { lock (gate) deletionPaths.Remove(pathKey); }
    public MediaItem[] CaptureDeletion(string pathKey)
    {
        lock (gate)
        {
            RequireDeletionPathWritable(pathKey);
            var items = GetSessionSnapshot().Items.Where(i => i.PathKey == pathKey).ToArray();
            if (items.Length == 0) throw new InvalidOperationException("삭제 대상이 없습니다.");
            deletionPaths.Add(pathKey);
            return items;
        }
    }
    // Rebuild from the complete durable pending set; overlapping operations keep isolation.
    public void SetDeletionIsolation(IEnumerable<string> keys, bool remoteAndUnknown, bool global)
    {
        lock (gate)
        {
            deletionPaths.Clear(); deletionPaths.UnionWith(keys);
            remoteDeletionBlocked = remoteAndUnknown; deletionRecoveryBlocked = global;
        }
    }
    private bool IsRemoteOrUnknown(string key, Microsoft.Data.Sqlite.SqliteTransaction? transaction = null)
    {
        if (string.IsNullOrEmpty(key)) return false;
        try
        {
            if (transaction is null) return GetStorageBinding(key) is not { Kind: StorageKind.Local, RequiresConfirmation: false };
            using var command = Command(transaction, "SELECT Kind,RequiresConfirmation FROM StorageBinding WHERE RootKey=$root;",
                ("$root", WindowsPath.Normalize(key).RootKey));
            using var reader = command.ExecuteReader();
            return !reader.Read() || reader.GetString(0) != "Local" || reader.GetBoolean(1);
        }
        catch (ArgumentException) { return true; }
    }
    private void RequireDeletionCategoryWritable(Microsoft.Data.Sqlite.SqliteTransaction transaction, Guid categoryId)
    {
        if (deletionRecoveryBlocked) throw new InvalidOperationException("삭제 복구 중인 분류입니다.");
        var keys = new List<string>();
        using (var command = Command(transaction, "SELECT PathKey FROM MediaItem WHERE CategoryId=$id UNION SELECT RootPathKey FROM CategorySource WHERE CategoryId=$id;", ("$id", Id(categoryId))))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) keys.Add(reader.GetString(0));
        var localRoots = remoteDeletionBlocked ? ReadConfirmedLocalRoots(transaction) : new HashSet<string>(StringComparer.Ordinal);
        var snapshot = new DeletionIsolationSnapshot(deletionRecoveryBlocked, remoteDeletionBlocked,
            deletionPaths, localRoots, new HashSet<Guid>());
        if (keys.Any(snapshot.IsBlocked)) throw new InvalidOperationException("삭제 복구 중인 분류입니다.");
    }
    public MediaItem[] CaptureDeletion(string pathKey, IReadOnlyCollection<string> keys,
        IReadOnlyCollection<DeletionBinding> bindings, bool remoteAndUnknown)
    {
        lock (gate)
        {
            foreach (var evidence in bindings)
                if (GetStorageBinding(evidence.Binding.RootKey) != evidence.Binding)
                    throw new InvalidOperationException("오래된 삭제 바인딩입니다.");
            foreach (var key in keys) RequireDeletionPathWritable(key);
            var items = GetSessionSnapshot().Items.Where(i => keys.Contains(i.PathKey)).ToArray();
            if (!items.Any(i => i.PathKey == pathKey)) throw new InvalidOperationException("삭제 대상이 없습니다.");
            deletionPaths.UnionWith(keys); remoteDeletionBlocked |= remoteAndUnknown;
            return items;
        }
    }
    private void RequireDeletionPathWritable(string key, Microsoft.Data.Sqlite.SqliteTransaction? transaction = null)
    {
        if (deletionRecoveryBlocked || deletionPaths.Contains(key) || (remoteDeletionBlocked && IsRemoteOrUnknown(key, transaction)))
            throw new InvalidOperationException("삭제 복구 중인 경로입니다. 복구 확인을 먼저 완료하세요.");
    }
    private void RequireDeletionItemWritable(Microsoft.Data.Sqlite.SqliteTransaction transaction, Guid id)
    {
        var key = Scalar(transaction, "SELECT PathKey FROM MediaItem WHERE Id=$id;", ("$id", Id(id))) as string;
        RequireDeletionPathWritable(key ?? "", transaction);
    }
}

// Owned collections are private; a completed snapshot cannot be mutated by consumers.
public sealed class DeletionIsolationSnapshot
{
    private readonly bool remote;
    private readonly IReadOnlySet<string> localRoots;
    private readonly IReadOnlySet<Guid> categories;
    public bool GloballyBlocked { get; }
    public IReadOnlySet<string> Paths { get; }
    internal DeletionIsolationSnapshot(bool global, bool remote, IReadOnlySet<string> paths,
        IReadOnlySet<string> localRoots, IReadOnlySet<Guid> categories)
    { GloballyBlocked = global; this.remote = remote; Paths = paths; this.localRoots = localRoots; this.categories = categories; }
    public bool IsBlocked(string key)
    {
        if (GloballyBlocked || Paths.Contains(key)) return true;
        if (!remote || string.IsNullOrEmpty(key)) return false;
        try { return !localRoots.Contains(WindowsPath.Normalize(key).RootKey); }
        catch (ArgumentException) { return true; }
    }
    public bool IsCategoryBlocked(Guid categoryId) => GloballyBlocked || categories.Contains(categoryId);
}
