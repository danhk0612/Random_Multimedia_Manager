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
    { get { lock (gate) return deletionPaths.Concat(remoteDeletionBlocked ? GetSessionSnapshot().Items.Where(i => IsRemoteOrUnknown(i.PathKey)).Select(i => i.PathKey) : []).ToHashSet(StringComparer.Ordinal); } }
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
    private bool IsRemoteOrUnknown(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        try { return GetStorageBinding(key) is not { Kind: StorageKind.Local, RequiresConfirmation: false }; }
        catch (ArgumentException) { return true; }
    }
    private void RequireDeletionCategoryWritable(Microsoft.Data.Sqlite.SqliteTransaction transaction, Guid categoryId)
    {
        if (deletionRecoveryBlocked || GetItems(categoryId).Any(i => IsDeletionBlocked(i.PathKey))
            || GetSources(categoryId).Any(s => IsDeletionBlocked(s.RootPathKey)))
            throw new InvalidOperationException("삭제 복구 중인 분류입니다.");
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
    private void RequireDeletionPathWritable(string key)
    {
        if (IsDeletionBlocked(key))
            throw new InvalidOperationException("삭제 복구 중인 경로입니다. 복구 확인을 먼저 완료하세요.");
    }
    private void RequireDeletionItemWritable(Microsoft.Data.Sqlite.SqliteTransaction transaction, Guid id)
    {
        var key = Scalar(transaction, "SELECT PathKey FROM MediaItem WHERE Id=$id;", ("$id", Id(id))) as string;
        RequireDeletionPathWritable(key ?? "");
    }
}
