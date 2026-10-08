using Microsoft.Data.Sqlite;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Deletion;
using RandomMultimediaManager.App.Sessions;
using RandomMultimediaManager.Core;

internal static class T18A3Verification
{
    static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    static async Task Reject(Func<Task> action)
    { try { await action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected rejection"); }
    static void Reject(Action action)
    { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected rejection"); }
    static async Task Scenario(string name, Func<Fixture, Task> action)
    { using var f = new Fixture(); await action(f); Console.WriteLine("PASS T18A-3: " + name); }
    public static async Task RunAsync()
    {
        await Scenario("N07 exact OS alias set, no inferred host/IP alias, unknown broad quarantine", async f =>
        {
            var record = await f.Service.PrepareAsync(f.Mapped.PathKey, DeletionMode.Permanent);
            Check(record.Version == 2 && record.Targets.Select(t => t.ItemId).ToHashSet().SetEquals(new[] { f.Mapped.Id, f.Unc.Id, f.OtherCategory.Id }), "exact captured target set");
            Check(record.Bindings!.Length == 2 && record.QuarantineScope == DeletionQuarantineScope.RemoteAndUnknown, "captured proof and scope");
            Check(f.Db.IsDeletionBlocked(f.Inferred.PathKey) && f.Db.IsDeletionBlocked(f.Orphan.PathKey) && !f.Db.IsDeletionBlocked(f.Local.PathKey), "broad scope includes historical unknown, preserves verified Local");
            Reject(() => f.Db.SaveProgress(f.Inferred.Id, PlaybackProgress.Video(99), 1));
            Reject(() => f.Db.SetFavorite(f.Orphan.Id, false));
            Reject(() => f.Db.ApplyObservedItems([f.Inferred], []));
            Reject(() => f.Db.SaveCategory(f.RemoteCategory with { Name = "blocked" }));
            Reject(() => f.Db.AddSource(new(Guid.NewGuid(), f.Local.CategoryId, @"R:\new", @"R:\NEW")));
            var result = await f.Service.RecordResultAsync(record, await f.Service.ExecuteAsync(record));
            Check(result.Resolved && f.OsCalls == 1 && f.Journal.ReadAll().Count == 0, "one durable execution and cleanup");
            foreach (var item in new[] { f.Mapped, f.Unc, f.OtherCategory })
            {
                var actual = f.Db.GetSessionSnapshot().Items.Single(i => i.Id == item.Id);
                Check(actual.IsMissing && actual.IsFavorite && actual.IsRandomExcluded && f.Db.GetHistory(item.Id).Count == 0 && f.Db.GetProgress(item.Id) is null, "target state only cleared");
            }
            Check(f.Db.GetHistory(f.Inferred.Id).Count == 1 && !f.Db.IsDeletionBlocked(f.Orphan.PathKey), "inferred history preserved and broad release");
        });
        await Scenario("N07 Prepared fault and lost acknowledgement issue no OS operation", async f =>
        {
            f.Journal.FailPrepared = true;
            await Reject(() => f.Service.PrepareAsync(f.Mapped.PathKey, DeletionMode.Permanent));
            Check(f.OsCalls == 0 && !f.Db.IsDeletionBlocked(f.Unc.PathKey), "no intent releases isolation");
            f.Journal.FailPrepared = false; f.Journal.LoseAcknowledgement = true;
            await Reject(() => f.Service.PrepareAsync(f.Mapped.PathKey, DeletionMode.Permanent));
            Check(f.OsCalls == 0 && f.Service.Pending.Count == 1 && f.Db.IsDeletionBlocked(f.Orphan.PathKey), "durable Prepared retained");
            f.Journal.LoseAcknowledgement = false;
            Check((await f.Service.ConfirmAsync(f.Service.Pending.Single(), false)).Resolved, "explicit failure preserves records");
        });
        await Scenario("N08 capability Supported/Unsupported/Unknown never selects permanent implicitly", async f =>
        {
            Check(RecycleCapability.For(f.Db.GetStorageBinding(f.Mapped.Path)!).Support == RecycleSupport.Unknown, "unverified backend Unknown");
            foreach (var capability in new[] { new RecycleCapability(RecycleSupport.Unknown, false), new(RecycleSupport.Unsupported, true), new(RecycleSupport.Supported, false) })
            {
                f.Capability = capability;
                await Reject(() => f.Service.PrepareAsync(f.Mapped.PathKey, DeletionMode.Recycle));
                Check(f.OsCalls == 0 && f.Service.Pending.Count == 0, "no unsafe recycle or permanent fallback");
            }
            f.Capability = new(RecycleSupport.Supported, true);
            var record = await f.Service.PrepareAsync(f.Mapped.PathKey, DeletionMode.Recycle);
            Check(record.Mode == DeletionMode.Recycle, "verified test policy keeps recycle");
            f.Outcome = new(DeletionOutcome.Failed, "injected recycle veto");
            Check((await f.Service.RecordResultAsync(record, await f.Service.ExecuteAsync(record))).Resolved && f.OsCalls == 1, "no second permanent call after failure");
        });
        await Scenario("N08 remote NotFound, response loss and absence remain Unknown until explicit confirmation", async f =>
        {
            var record = await f.Service.PrepareAsync(f.Mapped.PathKey, DeletionMode.Permanent);
            f.Outcome = new(DeletionOutcome.Failed, "remote NotFound", true);
            var result = await f.Service.RecordResultAsync(record, await f.Service.ExecuteAsync(record));
            Check(result.Outcome == DeletionOutcome.Unknown && !result.Resolved && f.Db.GetHistory(f.Mapped.Id).Count == 1 && !f.Db.GetItems(f.Mapped.CategoryId).Single(i => i.Id == f.Mapped.Id).IsMissing, "NotFound preserves history, progress and Present");
            await f.Restart();
            Check(f.Service.Pending.Single().Record!.Phase == DeletionPhase.Unknown && f.Db.IsDeletionBlocked(f.Orphan.PathKey), "restart broad hold");
            Check((await f.Service.ConfirmAsync(f.Service.Pending.Single(), false)).Resolved && f.OsCalls == 1, "recovery never retries OS");
            record = await f.Service.PrepareAsync(f.Mapped.PathKey, DeletionMode.Permanent);
            f.ThrowAfterIssue = true;
            result = await f.Service.RecordResultAsync(record, await f.Service.ExecuteAsync(record));
            Check(!result.Resolved && result.Outcome == DeletionOutcome.Unknown, "lost response stays Unknown");
        });
        await Scenario("N08 remap before issue refuses OS; recovery does not resolve stored aliases again", async f =>
        {
            var record = await f.Service.PrepareAsync(f.Mapped.PathKey, DeletionMode.Permanent);
            f.Remapped = true;
            var outcome = await f.Service.ExecuteAsync(record);
            Check(outcome.Outcome == DeletionOutcome.Failed && f.OsCalls == 0, "remap rejected before issue");
            await f.Service.RecordResultAsync(record, new(DeletionOutcome.Unknown));
            await f.Restart();
            Check((await f.Service.ConfirmAsync(f.Service.Pending.Single(), true)).Resolved && f.OsCalls == 0, "explicit captured-set cleanup only");
            Check(f.Db.GetHistory(f.Unc.Id).Count == 0 && f.Db.GetHistory(f.Inferred.Id).Count == 1, "no alias expansion during recovery");
        });
        await Scenario("N08 remap failure never reopens a replacement as the preserved Pending visit", async f =>
        {
            var preparer = new ReopenPreparer();
            var coordinator = new SessionCoordinator(f.Db, preparer);
            Check((await coordinator.OpenManualAsync(Guid.NewGuid(), f.Mapped.Id)).Status == SessionStatus.Completed, "original visit ready");
            var visit = coordinator.View.Pending;
            preparer.OnRelease = () => f.Remapped = true;
            var result = await coordinator.DeleteCurrentAsync(Guid.NewGuid(), f.Service, DeletionMode.Permanent);
            Check(result.Status == SessionStatus.Completed && result.Error!.Contains("BindingChanged") && f.OsCalls == 0,
                "remap discovered after media release prevents OS deletion");
            Check(preparer.Calls == 1 && coordinator.View.Pending == visit && f.Db.GetHistory(f.Mapped.Id).Count == 1,
                "no replacement reopen, old Pending/history retained");
            Check((await coordinator.LeaveAsync(Guid.NewGuid())).Status == SessionStatus.Completed, "old visit can leave with last valid progress");
        });
        await Scenario("N07 target-key validation and late SQL failure rollback whole set and AppliedDeletion", async f =>
        {
            var record = await f.Service.PrepareAsync(f.Mapped.PathKey, DeletionMode.Permanent);
            var bad = record.Targets.Select(t => t.ItemId == f.Unc.Id ? t with { PathKey = f.Inferred.PathKey } : t).ToArray();
            Check(f.Db.ApplyDeletion(Guid.NewGuid(), bad).Status == CommitStatus.Failed, "target key mismatch rejected");
            Check(record.Targets.All(t => f.Db.GetHistory(t.ItemId).Count == 1), "all history retained on key failure");
            f.Sql($"CREATE TRIGGER fail_target BEFORE DELETE ON ViewHistory WHEN OLD.MediaItemId='{f.Unc.Id}' BEGIN SELECT RAISE(ABORT,'injected'); END;");
            var result = await f.Service.RecordResultAsync(record, await f.Service.ExecuteAsync(record));
            Check(!result.Resolved && f.Service.HasIncompleteSuccess && record.Targets.All(t => f.Db.GetHistory(t.ItemId).Count == 1), "late cleanup failure rolls back every target");
            Check(f.Number("SELECT count(*) FROM AppliedDeletion") == 0, "marker rollback");
            f.Sql("DROP TRIGGER fail_target;"); await f.Restart();
            Check(f.Service.Pending.Count == 0 && f.Number("SELECT count(*) FROM AppliedDeletion") == 1 && f.OsCalls == 1, "Succeeded DB replay only");
        });
        await Scenario("N08 durable marker survives removal failure and stale journal without erasing later records", async f =>
        {
            var record = await f.Service.PrepareAsync(f.Mapped.PathKey, DeletionMode.Permanent);
            f.Journal.FailRemove = true;
            var result = await f.Service.RecordResultAsync(record, await f.Service.ExecuteAsync(record));
            Check(!result.Resolved && f.Service.Pending.Count == 1, "applied but isolated until journal removal");
            f.Journal.FailRemove = false; await f.Restart();
            f.Db.ApplyObservedItems([f.Mapped], []);
            var visit = Guid.NewGuid(); f.Db.CommitVisit(new(visit, f.Mapped.Id, 5, VisitOrigin.Manual, false, PlaybackProgress.Video(20)));
            f.Journal.Write(result.Record!); await f.Restart();
            Check(f.Db.GetHistory(f.Mapped.Id).Single().VisitId == visit && f.Db.GetProgress(f.Mapped.Id)!.Position.VideoPositionMs == 20 && f.OsCalls == 1, "AppliedDeletion idempotence preserves replacement visit");
        });
        await Scenario("N07 v1 remote compatibility retains bytes and original target scope; corrupt v2 global hold", async f =>
        {
            var v1 = new DeletionRecord(1, Guid.NewGuid(), f.Mapped.PathKey, f.Mapped.Path, [new(f.Mapped.Id,1,2)], DeletionMode.Permanent,1,DeletionPhase.Prepared);
            f.Journal.Write(v1); var bytes = File.ReadAllBytes(f.Journal.FileFor(v1)); await f.Restart();
            Check(f.Db.IsDeletionBlocked(f.Orphan.PathKey) && bytes.SequenceEqual(File.ReadAllBytes(f.Journal.FileFor(v1))), "v1 conservative scope without rewrite");
            Check((await f.Service.ConfirmAsync(f.Service.Pending.Single(), true)).Resolved && f.Db.GetHistory(f.Unc.Id).Count == 1 && f.OsCalls == 0, "v1 captured target only and no OS replay");
            var record = await f.Service.PrepareAsync(f.Unc.PathKey, DeletionMode.Permanent);
            File.WriteAllText(f.Journal.FileFor(record), File.ReadAllText(f.Journal.FileFor(record)).Replace("RemoteAndUnknown", "bad"));
            await f.Restart();
            Check(f.Service.GloballyBlocked && f.Db.IsDeletionBlocked(f.Local.PathKey), "corrupt target/scope global hold");
            Check(!(await f.Service.ConfirmAsync(f.Service.Pending.Single(), true)).Resolved, "no target guessing");
            Check((await f.Service.ConfirmAsync(f.Service.Pending.Single(), false)).Resolved && f.Db.GetHistory(f.Unc.Id).Count == 1, "explicit failure preserves all data");
        });
        await Scenario("N09 blocked actual IO keeps gate, rejects premature confirmation/replay and drains on Closing", async f =>
        {
            var record = await f.Service.PrepareAsync(f.Mapped.PathKey, DeletionMode.Permanent);
            var entered = new TaskCompletionSource(); var finish = new TaskCompletionSource();
            f.Delay = async () => { entered.SetResult(); await finish.Task; };
            var execution = f.Service.ExecuteAsync(record); await entered.Task;
            Check(!(await f.Service.ConfirmAsync(f.Service.Pending.Single(), true)).Resolved && !(await f.Service.ConfirmAsync(f.Service.Pending.Single(), false)).Resolved, "live IO cannot release isolation");
            await Reject(() => f.Service.ExecuteAsync(record));
            f.Service.BeginClosing(); var drain = f.Service.DrainAsync();
            await Reject(() => f.Service.PrepareAsync(f.Local.PathKey, DeletionMode.Permanent));
            Check(!drain.IsCompleted && !execution.IsCompleted && f.Db.IsDeletionBlocked(f.Orphan.PathKey), "no timeout success or new IO");
            finish.SetResult(); await drain;
            Check((await f.Service.RecordResultAsync(record, await execution)).Resolved && f.OsCalls == 1, "actual return precedes cleanup and release");
        });
    }
    sealed class ReopenPreparer : ISessionMediaPreparer
    {
        public int Calls; public Action? OnRelease;
        public Task<SessionPreparation> PrepareAsync(MediaItem item, PlaybackProgress? progress, SessionToken token, CancellationToken cancellation)
        { Calls++; return Task.FromResult(new SessionPreparation(PreparationStatus.Ready, token, new ReopenMedia(() => OnRelease?.Invoke()))); }
    }
    sealed class ReopenMedia(Action release) : ISessionMedia
    {
        public void Activate(SessionToken token) { }
        public void Resume(SessionToken token) { }
        public PlaybackProgress PauseAndCapture(SessionToken token) => PlaybackProgress.Video(42);
        public ValueTask DisposeAsync() { release(); return ValueTask.CompletedTask; }
    }
    sealed class FaultJournal(string path) : DeletionJournal(path)
    {
        public bool FailPrepared, LoseAcknowledgement, FailRemove;
        public override void Write(DeletionRecord record)
        {
            if (FailPrepared && record.Phase == DeletionPhase.Prepared) throw new InvalidOperationException("injected Prepared failure");
            base.Write(record); if (LoseAcknowledgement) throw new InvalidOperationException("injected lost acknowledgement");
        }
        public override void Remove(string path) { if (FailRemove) throw new IOException("injected removal failure"); base.Remove(path); }
    }
    sealed class Fixture : IDisposable
    {
        readonly string dir = Path.Combine(Path.GetTempPath(), "rmm-t18a3-" + Guid.NewGuid());
        string DbPath => Path.Combine(dir, "library.db");
        public LibraryDatabase Db;
        public FaultJournal Journal;
        public DeletionService Service = null!;
        public MediaItem Mapped, Unc, OtherCategory, Inferred, Orphan, Local;
        public Category RemoteCategory;
        public int OsCalls;
        public bool Remapped, ThrowAfterIssue;
        public Func<Task>? Delay;
        public FileDeletionResult Outcome = new(DeletionOutcome.Succeeded);
        public RecycleCapability Capability = new(RecycleSupport.Unknown, false);
        StorageObservation Evidence(string root) => root switch
        {
            @"Z:\" => StorageObservation.Classify(root, MappingLookup.Mapped, Remapped ? @"\\server\other\media" : @"\\server\share\media"),
            @"C:\" => StorageObservation.Classify(root, MappingLookup.NotMapped, localDriveType: true, device: "test-disk", volume: "test-volume"),
            _ => StorageObservation.Classify(root, MappingLookup.Failed)
        };
        public Fixture()
        {
            Directory.CreateDirectory(dir); Db = LibraryDatabase.Open(DbPath); Journal = new(Path.Combine(dir,"journal"));
            RemoteCategory = new(Guid.NewGuid(),"remote",MediaType.Video); var other = new Category(Guid.NewGuid(),"other",MediaType.Video); var local = new Category(Guid.NewGuid(),"local",MediaType.Video);
            foreach (var cat in new[] { RemoteCategory, other, local }) Db.SaveCategory(cat);
            MediaItem Item(Category cat, string path) => new(Guid.NewGuid(),cat.Id,MediaType.Video,path,path.ToUpperInvariant(),1,2);
            Mapped=Item(RemoteCategory,@"Z:\a.mp4"); Unc=Item(RemoteCategory,@"\\server\share\media\a.mp4"); OtherCategory=Item(other,Mapped.Path);
            Inferred=Item(RemoteCategory,@"\\192.0.2.1\share\media\a.mp4"); Orphan=Item(RemoteCategory,@"R:\old\a.mp4"); Local=Item(local,@"C:\local\a.mp4");
            Db.ApplyObservedItems([Mapped,Unc,OtherCategory,Inferred,Orphan,Local],[]);
            foreach (var item in Db.GetSessionSnapshot().Items)
            {
                Db.SetFavorite(item.Id,true); Db.SetRandomExcluded(item.Id,true);
                Db.CommitVisit(new(Guid.NewGuid(),item.Id,1,VisitOrigin.Manual,false,PlaybackProgress.Video(5)));
            }
            foreach (var root in new[] { @"Z:\", @"\\SERVER\SHARE\", @"\\192.0.2.1\SHARE\", @"C:\" })
                Db.ConfirmStorageBinding(root,0,Evidence(root),true);
            CreateService();
        }
        void CreateService() => Service = new(Db,Journal,async _ =>
        {
            OsCalls++; if(Delay is not null) await Delay();
            if(ThrowAfterIssue) throw new IOException("injected response loss"); return Outcome;
        }, (path, _) => Task.FromResult(Evidence(WindowsPath.Normalize(path).RootKey)), _ => Capability);
        public async Task Restart() { Db.Dispose(); Db=LibraryDatabase.Open(DbPath); CreateService(); await Service.InitializeAsync(); }
        public void Sql(string sql) { using var c = new SqliteConnection($"Data Source={DbPath};Pooling=False"); c.Open(); using var cmd=c.CreateCommand(); cmd.CommandText=sql; cmd.ExecuteNonQuery(); }
        public long Number(string sql) { using var c = new SqliteConnection($"Data Source={DbPath};Pooling=False"); c.Open(); using var cmd=c.CreateCommand(); cmd.CommandText=sql; return (long)cmd.ExecuteScalar()!; }
        public void Dispose() { Db.Dispose(); Directory.Delete(dir,true); }
    }
}
