using Microsoft.Data.Sqlite;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Deletion;
using RandomMultimediaManager.App.Sessions;
using RandomMultimediaManager.Core;

internal static class T18A1Verification
{
    static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or SqliteException or InvalidDataException) { return; }
        throw new Exception("Expected rejection");
    }
    static SqliteConnection Connect(string path)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = true }.ToString());
        c.Open(); return c;
    }
    static void Sql(SqliteConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
    static long Number(SqliteConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToInt64(cmd.ExecuteScalar()); }
    static string Dump(SqliteConnection c, string table)
    {
        using var cmd = c.CreateCommand(); cmd.CommandText = $"SELECT * FROM {table} ORDER BY 1";
        using var r = cmd.ExecuteReader(); var rows = new List<string>();
        while (r.Read()) rows.Add(string.Join("|", Enumerable.Range(0, r.FieldCount).Select(i =>
            r.IsDBNull(i) ? "NULL" : r.GetValue(i) is byte[] bytes ? Convert.ToHexString(bytes) : r.GetValue(i).ToString())));
        return string.Join("\n", rows);
    }
    static void CreateV1(string path)
    {
        using var c = Connect(path);
        using var stream = typeof(LibraryDatabase).Assembly.GetManifestResourceStream("SchemaV1.sql")!;
        using var reader = new StreamReader(stream); Sql(c, reader.ReadToEnd() + "PRAGMA user_version=1;");
    }
    static CategorySource Source(LibraryDatabase db, string path)
    {
        var category = new Category(Guid.NewGuid(), "T18A", MediaType.Video); db.SaveCategory(category);
        var n = WindowsPath.Normalize(path);
        return db.AddSource(new(Guid.NewGuid(), category.Id, n.Path, n.PathKey));
    }
    static async Task Scenario(string name, Func<string, Task> test)
    {
        string dir = Path.Combine(Path.GetTempPath(), "rmm-t18a1-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
        try { await test(dir); Console.WriteLine("PASS T18A-1: " + name); }
        finally { Directory.Delete(dir, true); }
    }
    public static async Task RunAsync()
    {
        await Scenario("N01 Windows/UNC syntax and logical identity", dir =>
        {
            var p = WindowsPath.Normalize("//Server/Share/한글 폴더/./A/../B/");
            Check(p.Path == @"\\Server\Share\한글 폴더\B" && p.RootKey == @"\\SERVER\SHARE\", "UNC normalization");
            Check(WindowsPath.Normalize(@"\\server\share").Path == @"\\server\share\", "share root");
            Check(WindowsPath.Normalize(@"c:\Media\file.mp4").PathKey == @"C:\MEDIA\FILE.MP4", "legacy key");
            Check(WindowsPath.IsSameOrDescendant(@"\\S\SHARE\a\x", @"\\s\share\a"), "descendant");
            Check(!WindowsPath.IsSameOrDescendant(@"\\s\share2\a", @"\\s\share"), "share boundary");
            Check(!WindowsPath.IsSameOrDescendant(@"C:\AB", @"C:\A"), "component boundary");
            Check(WindowsPath.Normalize(@"Z:\A").PathKey != WindowsPath.Normalize(@"\\s\share\A").PathKey, "no alias merge");
            Check(WindowsPath.Normalize("C:\\é").PathKey != WindowsPath.Normalize("C:\\e\u0301").PathKey, "no unicode merge");
            foreach (var bad in new[] { @"\\server", @"\\server\", @"\\s\..\a", @"\\s\sh\..\other", @"\\?\C:\x",
                @"\\.\C:\x", "//?/UNC/s/sh", @"C:relative", @"\relative", "https://s/sh", @"C:\x:ads", @"C:\a.\..\b", @"C:\a \b", @"C:\..\x" })
                Reject(() => WindowsPath.Normalize(bad));
            return Task.CompletedTask;
        });
        await Scenario("N02 true v1 migration preserves all legacy rows and orphan roots", dir =>
        {
            string file = Path.Combine(dir, "library.db"); CreateV1(file);
            string category = Guid.NewGuid().ToString(), item = Guid.NewGuid().ToString(), visit = Guid.NewGuid().ToString();
            string[] tables = ["Category", "CategorySource", "MediaItem", "ViewHistory", "PlaybackProgress", "VisitCommit", "AppliedDeletion", "AppSettings"];
            Dictionary<string, string> before;
            using (var c = Connect(file))
            {
                Sql(c, $"""
                    INSERT INTO Category VALUES('{category}','keep','Video',1);
                    INSERT INTO CategorySource VALUES('{Guid.NewGuid()}','{category}','C:\Media','C:\MEDIA',1,1);
                    INSERT INTO MediaItem VALUES('{item}','{category}','Video','Z:\old\a.mp4','Z:\OLD\A.MP4',99,123,1,1,0);
                    INSERT INTO ViewHistory VALUES('{visit}','{item}',1234,'Manual');
                    INSERT INTO PlaybackProgress VALUES('{item}','Video',NULL,NULL,500,1234);
                    INSERT INTO VisitCommit VALUES('{visit}','{item}',zeroblob(32));
                    INSERT INTO AppliedDeletion VALUES('{Guid.NewGuid()}');
                    UPDATE AppSettings SET HistoryExclusionDays=30,ResumeMode='FromStart';
                    """);
                before = tables.ToDictionary(t => t, t => Dump(c, t));
            }
            using (var db = LibraryDatabase.Open(file))
            {
                Check(db.GetStorageBinding(@"Z:\old\a.mp4") is { RequiresConfirmation: true, Revision: 0 }, "orphan binding");
                var source = db.GetSources(Guid.Parse(category)).Single();
                Check(db.GetSourceRefreshPolicy(source.Id) is null && db.GetEffectiveSourceRefreshPolicy(source.Id) == new SourceRefreshPolicy(), "unselected manual");
                db.RemoveSource(source.Id);
                Check(db.GetStorageBinding(@"C:\") is not null, "root survives source removal");
                db.AddSource(source);
            }
            using (var db = LibraryDatabase.Open(file)) Check(db.GetProgress(Guid.Parse(item))!.Position.VideoPositionMs == 500, "restart progress");
            using var check = Connect(file);
            Check(Number(check, "PRAGMA user_version") == 2, "version");
            foreach (var table in tables) Check(Dump(check, table) == before[table], "preserve " + table);
            return Task.CompletedTask;
        });
        await Scenario("N02 migration rollback, retry, corruption and higher version", dir =>
        {
            string file = Path.Combine(dir, "library.db"); CreateV1(file);
            using (var c = Connect(file)) Sql(c, "CREATE TABLE SourceRefreshPolicy(BlockMigration TEXT);");
            Reject(() => { using var db = LibraryDatabase.Open(file); });
            using (var c = Connect(file))
            {
                Check(Number(c, "PRAGMA user_version") == 1, "rollback version");
                Check(Number(c, "SELECT count(*) FROM sqlite_schema WHERE name='StorageBinding'") == 0, "DDL rollback");
                Sql(c, "DROP TABLE SourceRefreshPolicy;");
            }
            using (var db = LibraryDatabase.Open(file)) { }
            using (var c = Connect(file)) Sql(c, "PRAGMA user_version=3;");
            byte[] original = File.ReadAllBytes(file);
            Reject(() => { using var db = LibraryDatabase.Open(file); });
            Check(original.SequenceEqual(File.ReadAllBytes(file)), "higher version unchanged");
            using (var c = Connect(file)) Sql(c, "PRAGMA user_version=2; PRAGMA ignore_check_constraints=ON; UPDATE AppSettings SET ResumeMode='Bad';");
            Reject(() => { using var db = LibraryDatabase.Open(file); });
            return Task.CompletedTask;
        });
        await Scenario("N02 policy independence, defaults, rollback and root retention", dir =>
        {
            string file = Path.Combine(dir, "library.db");
            using var db = LibraryDatabase.Open(file);
            var a = Source(db, @"C:\a"); var b = Source(db, @"D:\b"); var remote = Source(db, @"\\s\share");
            db.SaveSettings(new(42, ResumeMode.FromStart)); db.SaveSourceRefreshPolicy(a.Id, new());
            var local = StorageObservation.Classify(@"C:\", MappingLookup.NotMapped, localDriveType: true, device: "disk1", volume: "vol1");
            db.ConfirmStorageBinding(a.RootPath, 0, local, false);
            Check(db.GetSourceRefreshPolicy(a.Id) == new SourceRefreshPolicy(), "user manual never overwritten");
            db.ConfirmStorageBinding(b.RootPath, 0, local, false);
            Check(db.GetSourceRefreshPolicy(b.Id) == new SourceRefreshPolicy(true, SourceRefreshMode.Events, 24), "local defaults");
            Reject(() => db.SaveSourceRefreshPolicy(remote.Id, new(true, SourceRefreshMode.Events, 24)));
            Reject(() => db.SaveSourceRefreshPolicy(remote.Id, new(false, SourceRefreshMode.Scheduled, 0)));
            Reject(() => db.SaveSourceRefreshPolicy(remote.Id, new(false, SourceRefreshMode.Manual, 24)));
            db.SaveSourceRefreshPolicy(remote.Id, new(false, SourceRefreshMode.Scheduled, 168, 123));
            using (var c = Connect(file)) Sql(c, "CREATE TRIGGER fail_policy BEFORE UPDATE ON SourceRefreshPolicy BEGIN SELECT RAISE(ABORT,'injected'); END;");
            Reject(() => db.SaveSourceRefreshPolicy(remote.Id, new()));
            Check(db.GetSourceRefreshPolicy(remote.Id)!.IntervalHours == 168, "failed save rollback");
            Check(db.GetSettings() == new AppSettings(42, ResumeMode.FromStart), "settings untouched");
            Reject(() => db.UpdateSource(b with { RootPath = @"Z:\b", RootPathKey = @"Z:\B" }));
            Check(db.GetSources(b.CategoryId).Single() == b && db.GetStorageBinding(@"Z:\") is null, "edit rollback incl root");
            db.RemoveSource(remote.Id);
            Check(db.GetSourceRefreshPolicy(remote.Id) is null && db.GetStorageBinding(remote.RootPath) is not null, "policy cascade only");
            return Task.CompletedTask;
        });
        await Scenario("N03 classification, remap, stale revision/generation, orphan and restart", dir =>
        {
            using var db = LibraryDatabase.Open(Path.Combine(dir, "library.db"));
            var source = Source(db, @"Z:\media");
            var failed = StorageObservation.Classify(source.RootPath, MappingLookup.Failed, localDriveType: true, device: "disk", volume: "vol");
            Check(failed.Kind == StorageKind.VirtualOrUnknown, "WNet failure is not Local");
            Check(StorageObservation.Classify(@"R:\", MappingLookup.NotMapped, localDriveType: true).Kind == StorageKind.VirtualOrUnknown, "Fixed alone");
            var original = StorageObservation.Classify(source.RootPath, MappingLookup.Mapped, @"\\server\share");
            Reject(() => db.ConfirmStorageBinding(source.RootPath, 0, original, false));
            var binding = db.ConfirmStorageBinding(source.RootPath, 0, original, true);
            var other = StorageObservation.Classify(source.RootPath, MappingLookup.Mapped, @"\\server\other");
            Reject(() => db.ConfirmStorageBinding(source.RootPath, binding.Revision, other, true));
            Reject(() => db.ConfirmStorageBinding(source.RootPath, 0, original, true));
            Check(db.GetStorageBinding(source.RootPath) == binding, "expected target preserved");
            var state = new BindingVerification(binding);
            Check(!state.CanAccess(@"Z:\old\outside.mp4", 0), "unverified historical path");
            Check(state.Observe(0, original) && state.CanAccess(@"Z:\old\outside.mp4", 0), "root covers historical path");
            state.Invalidate(); Check(!state.Observe(0, original), "late result discarded");
            Check(!state.Observe(1, other) && state.Access == BindingAccess.BindingChanged, "remap blocked");
            Check(!state.CanAccess(@"Z:\media\a.mp4", 2), "changed path denied");
            Check(state.Observe(2, original), "original restored");
            Check(!state.CanAccess(@"Y:\media\a.mp4", 2), "other root denied");
            db.RemoveSource(source.Id); Check(db.GetStorageBinding(@"Z:\old\x") == binding, "orphan retained");
            Check(!new BindingVerification(binding).CanAccess(@"Z:\x", 0), "availability never persisted");
            var unknown = new BindingVerification(new StorageBinding(@"R:\"));
            Check(!unknown.Observe(0, failed) && unknown.Observe(0, failed, true), "unknown explicit confirmation");
            unknown.Invalidate(); Check(!unknown.Observe(1, failed), "reconnect reconfirmation");
            return Task.CompletedTask;
        });
        await Scenario("N02 v1/v2 journal coexistence, v1 bytes preserved, v2 fail closed", async dir =>
        {
            using var db = LibraryDatabase.Open(Path.Combine(dir, "library.db")); var source = Source(db, @"Z:\media");
            var n = WindowsPath.Normalize(@"Z:\media\a.mp4");
            var item = new MediaItem(Guid.NewGuid(), source.CategoryId, MediaType.Video, n.Path, n.PathKey, 1, 2);
            db.ApplyObservedItems([item], []);
            var journal = new DeletionJournal(Path.Combine(dir, "journal"));
            var v1 = new DeletionRecord(1, Guid.NewGuid(), n.PathKey, n.Path, [new(item.Id, 1, 2)], DeletionMode.Recycle, 10, DeletionPhase.Prepared);
            journal.Write(v1); var v1bytes = File.ReadAllBytes(journal.FileFor(v1));
            var v2 = v1 with { Version = 2, OperationId = Guid.NewGuid(), Targets = [new(item.Id, 1, 2, n.PathKey)],
                Bindings = [new(new StorageBinding(n.RootKey), 0)], QuarantineScope = DeletionQuarantineScope.RemoteAndUnknown };
            journal.Write(v2);
            Check(journal.ReadAll().Count == 2 && journal.ReadAll().All(r => r.Record is not null), "both formats readable");
            int osCalls = 0;
            var service = new DeletionService(db, journal, _ => { osCalls++; return Task.FromResult(new FileDeletionResult(DeletionOutcome.Succeeded)); });
            await service.InitializeAsync();
            Check(service.GloballyBlocked && db.IsDeletionBlocked(@"C:\OTHER"), "v2 cannot enter legacy recovery");
            Check(!(await service.ConfirmAsync(service.Pending.Single(p => p.Record?.Version == 2), true)).Resolved, "no v2 partial cleanup");
            Check(osCalls == 0 && v1bytes.SequenceEqual(File.ReadAllBytes(journal.FileFor(v1))), "no OS replay or v1 rewrite");
            File.WriteAllText(journal.FileFor(v1), File.ReadAllText(journal.FileFor(v1)).Replace("\"FileSize\": 1", "\"FileSize\": -1"));
            var corruptV1 = journal.ReadAll().Single(r => r.File == journal.FileFor(v1));
            Check(corruptV1.Record is null && corruptV1.PathKey == n.PathKey, "v1 readable key recovery preserved");
            Reject(() => journal.Write(v2 with { Targets = [new(item.Id, 1, 2)] }));
            Reject(() => journal.Write(v2 with { Bindings = [] }));
            string damaged = File.ReadAllText(journal.FileFor(v2)).Replace("RemoteAndUnknown", "CorruptScope");
            File.WriteAllText(journal.FileFor(v2), damaged);
            Check(journal.ReadAll().Single(r => r.File == journal.FileFor(v2)).PathKey is null, "damaged set globally blocked");
        });
    }
}
