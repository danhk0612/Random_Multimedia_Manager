using Microsoft.Data.Sqlite;
using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.Data;

public sealed partial class LibraryDatabase
{
    private const string BindingColumns = "RootKey,Kind,ExpectedTarget,EvidenceKind,Revision,RequiresConfirmation,Provider,Device,Volume";
    private static StorageBinding ReadBinding(SqliteDataReader r) => new(r.GetString(0),
        Enum.Parse<StorageKind>(r.GetString(1)), r.IsDBNull(2) ? null : r.GetString(2),
        Enum.Parse<BindingEvidenceKind>(r.GetString(3)), r.GetInt64(4), r.GetBoolean(5),
        r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7),
        r.IsDBNull(8) ? null : r.GetString(8));
    private static SourceRefreshPolicy ReadPolicy(SqliteDataReader r) => new(r.GetBoolean(0),
        Enum.Parse<SourceRefreshMode>(r.GetString(1)), r.IsDBNull(2) ? null : r.GetInt32(2),
        r.IsDBNull(3) ? null : r.GetInt64(3));

    private void EnsureStorageRoot(SqliteTransaction transaction, string path) => Execute(transaction, """
        INSERT INTO StorageBinding(RootKey,Kind,EvidenceKind,Revision,RequiresConfirmation)
        VALUES($root,'VirtualOrUnknown','None',0,1) ON CONFLICT(RootKey) DO NOTHING;
        """, ("$root", WindowsPath.Normalize(path).RootKey));

    // Includes historical items outside any surviving source. Migration performs syntax only.
    private void MigrateStorageRoots(SqliteTransaction transaction)
    {
        var paths = new List<string>();
        using (var command = Command(transaction, "SELECT RootPath FROM CategorySource UNION SELECT Path FROM MediaItem;"))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) paths.Add(reader.GetString(0));
        foreach (string path in paths) EnsureStorageRoot(transaction, path);
    }

    public StorageBinding? GetStorageBinding(string path) => Read(
        $"SELECT {BindingColumns} FROM StorageBinding WHERE RootKey=$root;", ReadBinding,
        ("$root", WindowsPath.Normalize(path).RootKey)).SingleOrDefault();

    private StorageBinding RequireBinding(SqliteTransaction transaction, string path)
    {
        using var command = Command(transaction, $"SELECT {BindingColumns} FROM StorageBinding WHERE RootKey=$root;",
            ("$root", WindowsPath.Normalize(path).RootKey));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("루트 바인딩이 없습니다.");
        return ReadBinding(reader);
    }

    // Compare-and-set: never replace an established target. IO evidence must be gathered before
    // entering this DB-only transaction; process generation is checked by the caller as well.
    public StorageBinding ConfirmStorageBinding(string path, long expectedRevision,
        StorageObservation observation, bool userConfirmed)
    {
        observation.Validate();
        return Write(transaction =>
        {
            RequireDeletionPathWritable(WindowsPath.Normalize(path).PathKey, transaction);
            var old = RequireBinding(transaction, path);
            if (old.Revision != expectedRevision) throw new InvalidOperationException("오래된 바인딩 결과입니다.");
            if ((old.ExpectedTarget is not null && old.Observation != observation)
                || (old.Provider is not null && old.Provider != observation.Provider))
                throw new InvalidOperationException("BindingChanged: 원래 대상 복원 또는 다른 루트 등록이 필요합니다.");
            if (old.RequiresConfirmation && observation.Kind != StorageKind.Local && !userConfirmed)
                throw new InvalidOperationException("기존 원격/불명 대상의 사용자 확인이 필요합니다.");
            var updated = new StorageBinding(old.RootKey, observation.Kind, observation.Target,
                observation.Kind == StorageKind.VirtualOrUnknown ? BindingEvidenceKind.UserConfirmation : observation.EvidenceKind,
                checked(old.Revision + 1), observation.Kind == StorageKind.VirtualOrUnknown,
                observation.Provider, observation.Device, observation.Volume);
            updated.Validate();
            Execute(transaction, """
                UPDATE StorageBinding SET Kind=$kind,ExpectedTarget=$target,EvidenceKind=$evidence,
                Revision=$revision,RequiresConfirmation=$confirmation,Provider=$provider,Device=$device,Volume=$volume
                WHERE RootKey=$root;
                """, ("$kind", updated.Kind.ToString()), ("$target", updated.ExpectedTarget),
                ("$evidence", updated.EvidenceKind.ToString()), ("$revision", updated.Revision),
                ("$confirmation", updated.RequiresConfirmation), ("$provider", updated.Provider),
                ("$device", updated.Device), ("$volume", updated.Volume), ("$root", updated.RootKey));
            var sources = new List<Guid>();
            using (var command = Command(transaction, "SELECT Id,RootPath FROM CategorySource;"))
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                    if (WindowsPath.Normalize(reader.GetString(1)).RootKey == updated.RootKey)
                        sources.Add(Guid.Parse(reader.GetString(0)));
            foreach (var source in sources)
                WritePolicy(transaction, source, SourceRefreshPolicy.DefaultFor(updated), onlyIfAbsent: true);
            return updated;
        });
    }

    // Null is intentional: no user/default choice yet. Effective policy is Manual/startup off.
    public SourceRefreshPolicy? GetSourceRefreshPolicy(Guid sourceId) => Read("""
        SELECT ScanOnStartup,RefreshMode,IntervalHours,LastCompletedAtUtc
        FROM SourceRefreshPolicy WHERE SourceId=$source;
        """, ReadPolicy, ("$source", Id(sourceId))).SingleOrDefault();
    public SourceRefreshPolicy GetEffectiveSourceRefreshPolicy(Guid sourceId) => GetSourceRefreshPolicy(sourceId) ?? new();
    public void SaveSourceRefreshPolicy(Guid sourceId, SourceRefreshPolicy policy) => Write(transaction =>
    {
        string path = Scalar(transaction, "SELECT RootPath FROM CategorySource WHERE Id=$source;",
            ("$source", Id(sourceId))) as string ?? throw new InvalidOperationException("소스가 없습니다.");
        RequireDeletionCategoryWritable(transaction, Guid.Parse((string)Scalar(transaction, "SELECT CategoryId FROM CategorySource WHERE Id=$id;", ("$id", Id(sourceId)))!));
        RequireDeletionPathWritable(WindowsPath.Normalize(path).PathKey, transaction);
        policy.Validate(RequireBinding(transaction, path));
        WritePolicy(transaction, sourceId, policy, onlyIfAbsent: false);
        return 0;
    });
    private void WritePolicy(SqliteTransaction transaction, Guid sourceId, SourceRefreshPolicy policy, bool onlyIfAbsent)
    {
        string conflict = onlyIfAbsent ? "DO NOTHING" : """
            DO UPDATE SET ScanOnStartup=excluded.ScanOnStartup,RefreshMode=excluded.RefreshMode,
            IntervalHours=excluded.IntervalHours,LastCompletedAtUtc=excluded.LastCompletedAtUtc
            """;
        Execute(transaction, $"""
            INSERT INTO SourceRefreshPolicy VALUES($source,$startup,$mode,$hours,$completed)
            ON CONFLICT(SourceId) {conflict};
            """, ("$source", Id(sourceId)), ("$startup", policy.ScanOnStartup),
            ("$mode", policy.RefreshMode.ToString()), ("$hours", policy.IntervalHours),
            ("$completed", policy.LastCompletedAtUtc));
    }

    private void ValidateStorage(SqliteTransaction transaction)
    {
        var bindings = new Dictionary<string, StorageBinding>(StringComparer.Ordinal);
        using (var command = Command(transaction, $"SELECT {BindingColumns} FROM StorageBinding;"))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) { var b = ReadBinding(reader); b.Validate(); bindings.Add(b.RootKey, b); }
        using (var command = Command(transaction, "SELECT RootPath FROM CategorySource UNION SELECT Path FROM MediaItem;"))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
                if (!bindings.ContainsKey(WindowsPath.Normalize(reader.GetString(0)).RootKey))
                    throw new InvalidOperationException("저장된 경로의 바인딩이 없습니다.");
        using (var command = Command(transaction, """
            SELECT p.ScanOnStartup,p.RefreshMode,p.IntervalHours,p.LastCompletedAtUtc,s.RootPath
            FROM SourceRefreshPolicy p JOIN CategorySource s ON s.Id=p.SourceId;
            """))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) ReadPolicy(reader).Validate(bindings[WindowsPath.Normalize(reader.GetString(4)).RootKey]);
    }
}
