using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.Data;

public sealed partial class LibraryDatabase
{
    private readonly HashSet<string> deletionPaths = new(StringComparer.Ordinal);
    private bool deletionRecoveryBlocked;
    public bool IsDeletionBlocked(string pathKey)
    { lock (gate) return deletionRecoveryBlocked || deletionPaths.Contains(pathKey); }
    public IReadOnlySet<string> DeletionPaths
    { get { lock (gate) return deletionPaths.ToHashSet(StringComparer.Ordinal); } }
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
    private void RequireDeletionPathWritable(string key)
    {
        if (deletionRecoveryBlocked || deletionPaths.Contains(key))
            throw new InvalidOperationException("삭제 복구 중인 경로입니다. 복구 확인을 먼저 완료하세요.");
    }
    private void RequireDeletionItemWritable(Microsoft.Data.Sqlite.SqliteTransaction transaction, Guid id)
    {
        var key = Scalar(transaction, "SELECT PathKey FROM MediaItem WHERE Id=$id;", ("$id", Id(id))) as string;
        RequireDeletionPathWritable(key ?? "");
    }
}
