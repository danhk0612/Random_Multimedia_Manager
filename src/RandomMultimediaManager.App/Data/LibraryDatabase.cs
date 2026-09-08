using System.IO;
using Microsoft.Data.Sqlite;
using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.Data;

// The application owns one instance. All calls and disposal are serialized, with short DB-only transactions.
public sealed partial class LibraryDatabase : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly object gate = new();
    private bool disposed;
    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RandomMultimediaManager", "library.db");

    private LibraryDatabase(SqliteConnection connection) => this.connection = connection;

    public static LibraryDatabase Open(string? path = null)
    {
        path = System.IO.Path.GetFullPath(path ?? DefaultPath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Pooling = false, ForeignKeys = true, DefaultTimeout = 5
        }.ToString());
        try
        {
            connection.Open();
            var database = new LibraryDatabase(connection);
            database.Initialize();
            return database;
        }
        catch { connection.Dispose(); throw; }
    }

    private void Initialize()
    {
        Execute(null, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
        // Reject unknown versions before changing journal mode or schema.
        var version = (long)Scalar(null, "PRAGMA user_version;")!;
        if (version is < 0 or > 1) throw new InvalidOperationException("지원하지 않는 DB 버전입니다.");
        if (version == 0 && (long)Scalar(null,
            "SELECT count(*) FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%';")! != 0)
            throw new InvalidOperationException("버전 없는 기존 DB는 초기화하지 않습니다.");
        Execute(null, "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;");
        using var transaction = connection.BeginTransaction();
        // Re-read inside the write lock, including when another process initialized the DB.
        version = (long)Scalar(transaction, "PRAGMA user_version;")!;
        if (version == 0)
        {
            if ((long)Scalar(transaction,
                "SELECT count(*) FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%';")! != 0)
                throw new InvalidOperationException("빈 DB만 초기화할 수 있습니다.");
            using var stream = typeof(LibraryDatabase).Assembly.GetManifestResourceStream("SchemaV1.sql")!;
            using var reader = new StreamReader(stream);
            ApplyMigration(connection, transaction, reader.ReadToEnd(), 1);
        }
        else if (version != 1) throw new InvalidOperationException("지원하지 않는 DB 버전입니다.");
        // Reject a malformed v1 (including the old documentation-only schema); never silently rebuild it.
        Execute(transaction, "SELECT VisitId, MediaItemId, PayloadHash FROM VisitCommit LIMIT 0;");
        if ((long)Scalar(transaction, "SELECT count(*) FROM AppSettings WHERE Id=1;")! != 1)
            throw new InvalidOperationException("DB 설정 행이 손상되었습니다.");
        using (var check = Command(transaction, "PRAGMA foreign_key_check;"))
        using (var reader = check.ExecuteReader())
            if (reader.Read()) throw new InvalidOperationException("DB 외래키 검사가 실패했습니다.");
        transaction.Commit();
    }

    // Each later N -> N+1 script uses this same transaction path. No speculative migrations.
    internal static void ApplyMigration(SqliteConnection connection, SqliteTransaction transaction,
        string sql, int nextVersion)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
        command.CommandText = $"PRAGMA user_version={nextVersion};";
        command.ExecuteNonQuery();
    }

    private SqliteCommand Command(SqliteTransaction? transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = 5;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
    private int Execute(SqliteTransaction? transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var command = Command(transaction, sql, parameters);
        return command.ExecuteNonQuery();
    }
    private object? Scalar(SqliteTransaction? transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var command = Command(transaction, sql, parameters);
        return command.ExecuteScalar();
    }
    private T Write<T>(Func<SqliteTransaction, T> operation)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            using var transaction = connection.BeginTransaction();
            var result = operation(transaction);
            transaction.Commit();
            return result;
        }
    }
    private static string Id(Guid value)
    {
        if (value == Guid.Empty) throw new ArgumentException("빈 ID는 허용하지 않습니다.");
        return value.ToString("D");
    }
    private static void RequireOne(int count)
    {
        if (count != 1) throw new InvalidOperationException("대상 항목이 없습니다.");
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            connection.Dispose();
            disposed = true;
        }
    }
}
