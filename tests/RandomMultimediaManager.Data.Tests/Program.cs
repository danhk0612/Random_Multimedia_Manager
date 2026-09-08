using Microsoft.Data.Sqlite;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.Core;

int passed = 0;
void Check(bool condition, string message = "Assertion failed")
{
    if (!condition) throw new Exception(message);
}
void Reject(Action action)
{
    try { action(); }
    catch (Exception ex) when (ex is SqliteException or ArgumentException or InvalidOperationException) { return; }
    throw new Exception("Expected failure.");
}
void Run(string name, Action<string> action)
{
    string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rmm-test-" + Guid.NewGuid());
    Directory.CreateDirectory(directory);
    try { action(System.IO.Path.Combine(directory, "library.db")); passed++; Console.WriteLine("PASS: " + name); }
    finally { Directory.Delete(directory, recursive: true); }
}
SqliteConnection Connect(string path)
{
    var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Pooling = false, ForeignKeys = true }.ToString());
    connection.Open(); return connection;
}
void Sql(SqliteConnection connection, string sql)
{
    using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
}
long Number(SqliteConnection connection, string sql)
{
    using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToInt64(command.ExecuteScalar());
}
MediaItem Seed(LibraryDatabase db, string path = @"C:\Media\video.mp4", MediaType type = MediaType.Video)
{
    var category = new Category(Guid.NewGuid(), "Test", type);
    db.SaveCategory(category);
    var item = new MediaItem(Guid.NewGuid(), category.Id, type, path, path.ToUpperInvariant(), 1, 1);
    db.ApplyObservedItems(new[] { item }, Array.Empty<Guid>());
    return item;
}
VisitCommitRequest Visit(MediaItem item, long time = 1000, long position = 10, bool suppress = false) =>
    new(Guid.NewGuid(), item.Id, time, VisitOrigin.Random, suppress, PlaybackProgress.Video(position));

Run("initialization, settings persistence, pragmas, foreign keys", path =>
{
    using (var db = LibraryDatabase.Open(path))
    {
        Check(db.GetSettings() == new AppSettings());
        db.SaveSettings(new AppSettings(0, ResumeMode.FromStart));
        Reject(() => db.SaveSettings(new AppSettings(-1)));
    }
    using (var db = LibraryDatabase.Open(path)) Check(db.GetSettings() == new AppSettings(0, ResumeMode.FromStart));
    using var c = Connect(path);
    Check(Number(c, "PRAGMA user_version") == 1);
    Check(Number(c, "SELECT count(*) FROM AppSettings") == 1);
    using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA journal_mode";
    Check((string)cmd.ExecuteScalar()! == "wal");
    Check(Number(c, "PRAGMA foreign_keys") == 1);
    Reject(() => Sql(c, "INSERT INTO CategorySource VALUES('s','absent','x','X',1,1)"));
    Reject(() => Sql(c, "INSERT INTO AppSettings VALUES(2,7,'Resume')"));
    Reject(() => Sql(c, "UPDATE AppSettings SET HistoryExclusionDays=36501"));
});
Run("category and source editing preserves items", path =>
{
    using var db = LibraryDatabase.Open(path);
    var item = Seed(db);
    var source = new CategorySource(Guid.NewGuid(), item.CategoryId, @"C:\Media", @"C:\MEDIA");
    Check(db.AddSource(source) == source);
    Check(db.AddSource(source with { Id = Guid.NewGuid(), IsEnabled = false }) == source);
    db.UpdateSource(source with { IsEnabled = false });
    Check(!db.GetSources(item.CategoryId).Single().IsEnabled);
    db.RemoveSource(source.Id);
    Check(!db.GetItems(item.CategoryId).Single().IsMissing);
    Reject(() => db.SaveCategory(new Category(item.CategoryId, "changed", MediaType.Comic)));
    Check(db.GetCategories().Single().MediaType == MediaType.Video);
    db.ApplyObservedItems(Array.Empty<MediaItem>(), new[] { item.Id });
    Reject(() => db.SaveCategory(new Category(item.CategoryId, "changed", MediaType.Comic)));
    var empty = new Category(Guid.NewGuid(), " Empty ", MediaType.Video);
    db.SaveCategory(empty); db.SaveCategory(empty with { MediaType = MediaType.Comic });
    Check(db.GetCategories().Single(x => x.Id == empty.Id).Name == "Empty");
});
Run("same-category uniqueness, cross-category independence, constraints", path =>
{
    using var db = LibraryDatabase.Open(path);
    var a = Seed(db); var b = Seed(db);
    db.SetFavorite(a.Id, true); db.SetRandomExcluded(a.Id, true);
    Check(!db.GetItems(b.CategoryId).Single().IsFavorite);
    using var c = Connect(path);
    Reject(() => Sql(c, $"INSERT INTO MediaItem SELECT 'duplicate',CategoryId,MediaType,Path,PathKey,FileSize,LastWriteTimeUtc,0,0,0 FROM MediaItem WHERE Id='{a.Id:D}'"));
    Reject(() => Sql(c, $"INSERT INTO PlaybackProgress VALUES('{a.Id:D}','Video',0,NULL,1,0)"));
    Reject(() => Sql(c, $"INSERT INTO PlaybackProgress VALUES('{a.Id:D}','Comic',0,0,NULL,0)"));
    var comic = Seed(db, @"C:\Media\comic.cbz", MediaType.Comic);
    Reject(() => Sql(c, $"INSERT INTO PlaybackProgress VALUES('{comic.Id:D}','Comic',0,NULL,NULL,0)"));
    Reject(() => Sql(c, $"INSERT INTO PlaybackProgress VALUES('{comic.Id:D}','Comic',0,1.1,NULL,0)"));
    db.SaveProgress(comic.Id, PlaybackProgress.Comic(3, 0.5), 100);
    Check(db.GetProgress(comic.Id)!.Position == PlaybackProgress.Comic(3, 0.5));
});
Run("visit retry after newer progress and restart, payload conflict", path =>
{
    VisitCommitRequest first;
    using (var db = LibraryDatabase.Open(path))
    {
        var item = Seed(db); first = Visit(item);
        Check(db.CommitVisit(first).Status == CommitStatus.Committed);
        Check(db.CommitVisit(first).Status == CommitStatus.AlreadyCommitted);
        Check(db.CommitVisit(Visit(item, 2000, 20)).Status == CommitStatus.Committed);
    }
    using var reopened = LibraryDatabase.Open(path);
    Check(reopened.CommitVisit(first).Status == CommitStatus.AlreadyCommitted);
    Check(reopened.GetProgress(first.ItemId)!.Position.VideoPositionMs == 20);
    Check(reopened.GetHistory(first.ItemId).Count == 2);
    foreach (var altered in new[] { first with { Progress = PlaybackProgress.Video(11) },
        first with { ViewedAtUtc = 1001 }, first with { Origin = VisitOrigin.Back },
        first with { SuppressHistory = true }, first with { ItemId = Guid.NewGuid() } })
        Check(reopened.CommitVisit(altered).Status == CommitStatus.Failed);
    Check(reopened.GetProgress(first.ItemId)!.Position.VideoPositionMs == 20);
});
Run("suppression is independent and has durable idempotency", path =>
{
    using var db = LibraryDatabase.Open(path); var item = Seed(db);
    Check(db.CommitVisit(Visit(item)).Status == CommitStatus.Committed);
    var suppressed = Visit(item, 2000, 20, true);
    Check(db.CommitVisit(suppressed).Status == CommitStatus.Committed);
    db.SaveProgress(item.Id, PlaybackProgress.Video(30), 3000);
    Check(db.CommitVisit(suppressed).Status == CommitStatus.AlreadyCommitted);
    Check(db.GetHistory(item.Id).Count == 1);
    Check(db.GetProgress(item.Id)!.Position.VideoPositionMs == 30);
    using var c = Connect(path); Check(Number(c, "SELECT count(*) FROM VisitCommit") == 2);
});
Run("visit failure rolls receipt, progress and history back", path =>
{
    using var db = LibraryDatabase.Open(path); var item = Seed(db);
    db.SaveProgress(item.Id, PlaybackProgress.Video(5), 500);
    using var c = Connect(path);
    Sql(c, "CREATE TRIGGER fail_history BEFORE INSERT ON ViewHistory BEGIN SELECT RAISE(ABORT,'injected'); END;");
    var request = Visit(item);
    Check(db.CommitVisit(request).Status == CommitStatus.Failed);
    Check(db.GetProgress(item.Id)!.Position.VideoPositionMs == 5);
    Check(Number(c, "SELECT count(*) FROM VisitCommit") == 0);
    Check(db.GetHistory(item.Id).Count == 0);
    Sql(c, "DROP TRIGGER fail_history;");
    Check(db.CommitVisit(request).Status == CommitStatus.Committed);
});
Run("missing and metadata replacement keep independent state", path =>
{
    using var db = LibraryDatabase.Open(path); var item = Seed(db);
    db.CommitVisit(Visit(item)); db.SetFavorite(item.Id, true);
    db.ApplyObservedItems(Array.Empty<MediaItem>(), new[] { item.Id });
    Check(db.GetProgress(item.Id) != null && db.GetHistory(item.Id).Count == 1);
    db.ApplyObservedItems(new[] { item with { Id = Guid.NewGuid() } }, Array.Empty<Guid>());
    Check(db.GetItems(item.CategoryId).Single().Id == item.Id && db.GetProgress(item.Id) != null);
    db.ApplyObservedItems(new[] { item with { FileSize = 2 } }, Array.Empty<Guid>());
    Check(db.GetProgress(item.Id) == null);
    Check(db.GetHistory(item.Id).Count == 1 && db.GetItems(item.CategoryId).Single().IsFavorite);
    Reject(() => db.ApplyObservedItems(new[] { item with { FileSize = 3 }, item with { CategoryId = Guid.NewGuid() } }, Array.Empty<Guid>()));
    Check(db.GetItems(item.CategoryId).Single().FileSize == 2);
});
Run("deletion atomically cleans captured categories and retries", path =>
{
    Guid operation = Guid.NewGuid(); MediaItem a; MediaItem b;
    using (var db = LibraryDatabase.Open(path))
    {
        a = Seed(db); b = Seed(db); var unrelated = Seed(db, @"C:\Media\other.mp4");
        foreach (var item in new[] { a,b,unrelated }) { db.CommitVisit(Visit(item)); db.SetFavorite(item.Id,true); db.SetRandomExcluded(item.Id,true); }
        using var c = Connect(path);
        Sql(c, "CREATE TRIGGER fail_deletion BEFORE INSERT ON AppliedDeletion BEGIN SELECT RAISE(ABORT,'injected'); END;");
        Check(db.ApplyDeletion(operation,a.PathKey,new[] {a.Id,b.Id}).Status == CommitStatus.Failed);
        Check(!db.GetItems(a.CategoryId).Single().IsMissing && db.GetHistory(a.Id).Count == 1);
        Check(Number(c,"SELECT count(*) FROM VisitCommit") == 3);
        Sql(c,"DROP TRIGGER fail_deletion;");
        Check(db.ApplyDeletion(operation,a.PathKey,new[] {a.Id,b.Id}).Status == CommitStatus.Committed);
        foreach (var item in new[] { a,b })
        {
            var stored = db.GetItems(item.CategoryId).Single();
            Check(stored.IsMissing && stored.IsFavorite && stored.IsRandomExcluded);
            Check(db.GetProgress(item.Id) == null && db.GetHistory(item.Id).Count == 0);
        }
        Check(db.GetHistory(unrelated.Id).Count == 1);
    }
    using var reopened = LibraryDatabase.Open(path);
    reopened.ApplyObservedItems(new[] { a }, Array.Empty<Guid>());
    reopened.SaveProgress(a.Id,PlaybackProgress.Video(99),9999);
    Check(reopened.ApplyDeletion(operation,a.PathKey,new[] {a.Id,b.Id}).Status == CommitStatus.AlreadyCommitted);
    Check(reopened.GetProgress(a.Id)!.Position.VideoPositionMs == 99);
    Check(!reopened.GetItems(a.CategoryId).Single().IsMissing);
});
Run("migration rollback preserves previous schema and version", path =>
{
    using var c = Connect(path);
    Sql(c,"CREATE TABLE Existing(Value TEXT); INSERT INTO Existing VALUES('keep'); PRAGMA user_version=1;");
    Reject(() =>
    {
        using var tx = c.BeginTransaction();
        LibraryDatabase.ApplyMigration(c,tx,"CREATE TABLE Partial(Id INTEGER); INSERT INTO Missing VALUES(1);",2);
        tx.Commit();
    });
    Check(Number(c,"PRAGMA user_version") == 1);
    Check(Number(c,"SELECT count(*) FROM Existing") == 1);
    Check(Number(c,"SELECT count(*) FROM sqlite_schema WHERE name='Partial'") == 0);
});
Run("initial migration failure leaves empty database", path =>
{
    using var c = Connect(path);
    using var stream = typeof(LibraryDatabase).Assembly.GetManifestResourceStream("SchemaV1.sql")!;
    using var reader = new StreamReader(stream);
    string sql = reader.ReadToEnd();
    Reject(() =>
    {
        using var tx = c.BeginTransaction();
        LibraryDatabase.ApplyMigration(c,tx,sql + "INSERT INTO Missing VALUES(1);",1); tx.Commit();
    });
    Check(Number(c,"PRAGMA user_version") == 0);
    Check(Number(c,"SELECT count(*) FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%'") == 0);
});
Run("higher version refuses writes", path =>
{
    using (var db = LibraryDatabase.Open(path)) db.SaveSettings(new AppSettings(30));
    using (var c = Connect(path)) Sql(c,"PRAGMA user_version=2;");
    var before = File.ReadAllBytes(path);
    Reject(() => { using var db = LibraryDatabase.Open(path); });
    Check(before.AsSpan().SequenceEqual(File.ReadAllBytes(path)));
});
Run("unknown unversioned schema preserved", path =>
{
    using (var c = Connect(path)) Sql(c,"CREATE TABLE Existing(Id INTEGER);");
    Reject(() => { using var db = LibraryDatabase.Open(path); });
    using var check = Connect(path);
    Check(Number(check,"PRAGMA user_version") == 0);
    Check(Number(check,"SELECT count(*) FROM sqlite_schema WHERE name='Existing'") == 1);
});
Run("concurrent duplicate requests create one history row", path =>
{
    using var db = LibraryDatabase.Open(path); var item = Seed(db); var request = Visit(item);
    var results = Enumerable.Range(0,20).Select(_ => Task.Run(() => db.CommitVisit(request))).ToArray();
    Task.WaitAll(results);
    Check(results.Count(x => x.Result.Status == CommitStatus.Committed) == 1);
    Check(results.Count(x => x.Result.Status == CommitStatus.AlreadyCommitted) == 19);
    Check(db.GetHistory(item.Id).Count == 1);
});
Console.WriteLine($"PASS: {passed} SQLite integration scenarios");
