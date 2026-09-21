using Microsoft.Data.Sqlite;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Deletion;
using RandomMultimediaManager.App.Sessions;
using RandomMultimediaManager.Core;

internal static class T12Verification
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static async Task Done(Task<SessionResult> task) { var r = await task; Check(r.Status == SessionStatus.Completed, r.ToString()); }
    static void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected quarantine rejection"); }
    public static async Task RunAsync()
    {
        await Scenario("success, all categories, tombstones, no auto next, stale checkpoint", async f =>
        {
            await Done(f.C.OpenManualAsync(Guid.NewGuid(), f.B.Id));
            await Done(f.C.OpenManualAsync(Guid.NewGuid(), f.A.Id));
            var token = f.C.View.ActiveToken!;
            await Done(f.C.DeleteCurrentAsync(Guid.NewGuid(), f.Service, DeletionMode.Recycle));
            Check(f.C.View.Pending is null && f.C.View.Cursor == 1 && f.C.View.Slots[1].Deleted, "tombstone/cursor");
            Check(!f.C.ObserveProgress(token, PlaybackProgress.Video(99)), "late callback");
            foreach (var item in new[] { f.A, f.Other })
            {
                Check(f.Db.GetHistory(item.Id).Count == 0 && f.Db.GetProgress(item.Id) is null, "all path histories/progress cleared");
                var actual = f.Db.GetSessionSnapshot().Items.Single(i => i.Id == item.Id);
                Check(actual.IsMissing && actual.IsFavorite && actual.IsRandomExcluded, "flags preserved");
            }
            await Done(f.C.PreviousAsync(Guid.NewGuid()));
            Check(f.C.View.Pending!.ItemId == f.B.Id && f.OsCalls == 1, "previous skips deletion, OS once");
            await Done(f.C.LeaveAsync(Guid.NewGuid()));
        });
        foreach (var outcome in new[] { DeletionOutcome.Failed, DeletionOutcome.Cancelled })
            await Scenario(outcome + " preserves visit, suppression and progress", async f =>
            {
                await Done(f.C.OpenManualAsync(Guid.NewGuid(), f.A.Id));
                await Done(f.C.SetSuppressedAsync(Guid.NewGuid(), f.C.View.ActiveToken!, true));
                var visit = f.C.View.Pending;
                f.Preparer.Last!.Position = PlaybackProgress.Video(321);
                f.Outcome = outcome;
                await Done(f.C.DeleteCurrentAsync(Guid.NewGuid(), f.Service, DeletionMode.Permanent));
                Check(f.C.View.Pending == visit && f.Preparer.Last!.Position.VideoPositionMs == 321, "same visit and progress");
                Check(f.Db.GetHistory(f.A.Id).Count == 1, "old history preserved");
                await Done(f.C.LeaveAsync(Guid.NewGuid()));
                Check(f.Db.GetHistory(f.A.Id).Count == 1, "suppression preserved");
            });
        await Scenario("failure + reopen failure can leave same Pending", async f =>
        {
            await Done(f.C.OpenManualAsync(Guid.NewGuid(), f.A.Id));
            var visit = f.C.View.Pending!.VisitId;
            f.Outcome = DeletionOutcome.Failed; f.Preparer.Fail = true;
            await Done(f.C.DeleteCurrentAsync(Guid.NewGuid(), f.Service, DeletionMode.Recycle));
            Check(f.C.View.Pending!.VisitId == visit, "retain Pending on reopen failure");
            await Done(f.C.LeaveAsync(Guid.NewGuid()));
            Check(f.Db.GetHistory(f.A.Id).Any(h => h.VisitId == visit), "leave commits retained visit");
        });
        await Scenario("resource release failure never enters OS and retains Pending", async f =>
        {
            await Done(f.C.OpenManualAsync(Guid.NewGuid(),f.A.Id)); var visit=f.C.View.Pending;
            f.Preparer.Last!.FailDispose=true;
            var result=await f.C.DeleteCurrentAsync(Guid.NewGuid(),f.Service,DeletionMode.Recycle);
            Check(f.OsCalls==0 && f.C.View.Pending==visit && f.Db.GetHistory(f.A.Id).Count==1,"release failure preserves visit and history");
            Check(result.Error is not null,"release error shown");
        });
        await Scenario("Prepared write failure prevents OS", async f =>
        {
            await Done(f.C.OpenManualAsync(Guid.NewGuid(), f.A.Id));
            f.Journal.FailPhase = DeletionPhase.Prepared;
            var r = await f.C.DeleteCurrentAsync(Guid.NewGuid(), f.Service, DeletionMode.Permanent);
            Check(r.Status == SessionStatus.Failed && f.OsCalls == 0 && f.C.View.Pending is not null, "no OS on journal failure");
            Check(!f.Db.IsDeletionBlocked(f.A.PathKey), "no durable intent lifts isolation");
        });
        await Scenario("lost Prepared acknowledgement confirmation releases still-owned media", async f =>
        {
            await Done(f.C.OpenManualAsync(Guid.NewGuid(),f.A.Id)); var media=f.Preparer.Last!;
            f.Journal.FailAfterWrite=true;
            await f.C.DeleteCurrentAsync(Guid.NewGuid(),f.Service,DeletionMode.Recycle);
            Check(f.OsCalls==0 && !media.Disposed && f.Service.Pending.Count==1,"durable Prepared but no OS/release");
            f.Journal.FailAfterWrite=false; File.Delete(f.A.Path);
            await Done(f.C.ResolveDeletionAsync(Guid.NewGuid(),f.Service,f.Service.Pending.Single(),true));
            Check(media.Disposed && f.C.View.Pending is null,"confirmed success releases existing media");
        });
        await Scenario("OS success + result journal failure requires confirmation", async f =>
        {
            await Done(f.C.OpenManualAsync(Guid.NewGuid(), f.A.Id));
            f.Journal.FailPhase = DeletionPhase.Succeeded;
            var r = await f.C.DeleteCurrentAsync(Guid.NewGuid(), f.Service, DeletionMode.Permanent);
            Check(r.Status == SessionStatus.CommitUnknown && f.C.View.Pending is null, "success immediately suppresses Pending");
            Check(f.Db.GetHistory(f.A.Id).Count == 1 && f.Db.IsDeletionBlocked(f.A.PathKey), "history held until durable result");
            f.Journal.FailPhase = null;
            await f.Restart();
            Check(f.Service.Pending.Single().Record!.Phase == DeletionPhase.Prepared, "Prepared remains unknown");
            Check(f.OsCalls == 1, "recovery never OS");
            await f.Service.ConfirmAsync(f.Service.Pending.Single(), true);
            Check(f.Db.GetHistory(f.A.Id).Count == 0, "explicit success cleans DB");
        });
        await Scenario("OS success + DB failure, restart and recreated file", async f =>
        {
            await Done(f.C.OpenManualAsync(Guid.NewGuid(), f.A.Id));
            f.Sql("CREATE TRIGGER fail_delete BEFORE DELETE ON ViewHistory BEGIN SELECT RAISE(ABORT,'injected'); END;");
            var r = await f.C.DeleteCurrentAsync(Guid.NewGuid(), f.Service, DeletionMode.Permanent);
            Check(r.Status == SessionStatus.CommitUnknown && f.C.View.Pending is null, "DB failure after success");
            Check(f.Db.GetHistory(f.Other.Id).Count == 1, "atomic rollback");
            Reject(() => f.Db.SaveProgress(f.A.Id, PlaybackProgress.Video(9), 1));
            Check(f.Db.CommitVisit(new(Guid.NewGuid(), f.A.Id, 1, VisitOrigin.Manual, false, PlaybackProgress.Video(9))).Status == CommitStatus.Failed, "record write blocked");
            Reject(() => f.Db.ApplyObservedItems([f.A with { Id = Guid.NewGuid() }], []));
            await Done(f.C.OpenManualAsync(Guid.NewGuid(), f.B.Id));
            await Done(f.C.LeaveAsync(Guid.NewGuid()));
            f.Sql("DROP TRIGGER fail_delete;");
            File.WriteAllText(f.A.Path, "replacement must survive recovery");
            await f.Restart();
            Check(f.Service.Pending.Count == 0 && f.Db.GetHistory(f.A.Id).Count == 0, "startup DB replay");
            Check(File.ReadAllText(f.A.Path).StartsWith("replacement") && f.OsCalls == 1, "no automatic redelete");
            f.Db.ApplyObservedItems([f.A], []);
            Check(!f.Db.GetSessionSnapshot().Items.Single(i => i.Id == f.A.Id).IsMissing, "rescan restores existence");
        });
        await Scenario("Applied marker + journal removal failure survives restart", async f =>
        {
            await Done(f.C.OpenManualAsync(Guid.NewGuid(), f.A.Id));
            f.Journal.FailRemove = true;
            await f.C.DeleteCurrentAsync(Guid.NewGuid(), f.Service, DeletionMode.Permanent);
            Check(f.Service.Pending.Count == 1 && f.Db.GetHistory(f.A.Id).Count == 0, "Applied but journal retained");
            f.Journal.FailRemove = false;
            await f.Restart();
            Check(f.Service.Pending.Count == 0 && !f.Db.IsDeletionBlocked(f.A.PathKey) && f.OsCalls == 1, "idempotent cleanup");
        });
        await Scenario("reappearing completed journal cannot erase later visits", async f =>
        {
            var record=await f.Service.PrepareAsync(f.A.PathKey,DeletionMode.Permanent);
            var result=await f.Service.RecordResultAsync(record,await f.Service.ExecuteAsync(record));
            Check(result.Resolved,"initial deletion completed");
            File.WriteAllText(f.A.Path,"new replacement"); f.Db.ApplyObservedItems([f.A],[]);
            var laterVisit=Guid.NewGuid();
            Check(f.Db.CommitVisit(new(laterVisit,f.A.Id,2,VisitOrigin.Manual,false,PlaybackProgress.Video(17))).Status==CommitStatus.Committed,"replacement visit saved");
            f.Journal.Write(result.Record!); await f.Restart();
            Check(f.Db.GetHistory(f.A.Id).Single().VisitId==laterVisit && f.Db.GetProgress(f.A.Id)!.Position.VideoPositionMs==17,"Applied marker prevents stale cleanup");
            Check(!f.Db.GetSessionSnapshot().Items.Single(i=>i.Id==f.A.Id).IsMissing && f.OsCalls==1,"replacement stays present and never re-deleted");
        });
        foreach (var phase in new[] { DeletionPhase.Prepared, DeletionPhase.Unknown, DeletionPhase.Failed, DeletionPhase.Cancelled })
            await Scenario("restart phase " + phase, async f =>
            {
                var record = await f.Service.PrepareAsync(f.A.PathKey, DeletionMode.Recycle);
                f.Journal.Write(record with { Phase = phase });
                File.Delete(f.A.Path); // artificial external absence, never evidence of app success
                await f.Restart();
                Check(f.Db.GetHistory(f.A.Id).Count == 1 && f.OsCalls == 0, "absence not success");
                if (phase is DeletionPhase.Prepared or DeletionPhase.Unknown)
                {
                    Check(f.Db.IsDeletionBlocked(f.A.PathKey), "unknown isolated");
                    var r = await f.Service.ConfirmAsync(f.Service.Pending.Single(), false);
                    Check(r.Resolved && f.Db.GetHistory(f.A.Id).Count == 1, "failure confirmation keeps records");
                }
                else Check(f.Service.Pending.Count == 0, "failed/cancelled cleanup");
            });
        await Scenario("corrupt journal blocks library; no target guessing", async f =>
        {
            Directory.CreateDirectory(f.Journal.DirectoryPath);
            File.WriteAllText(Path.Combine(f.Journal.DirectoryPath, Guid.NewGuid() + ".json"), "{broken");
            await f.Restart();
            Check(f.Service.GloballyBlocked && f.Db.IsDeletionBlocked(f.B.PathKey), "global hold");
            Reject(() => f.Db.ApplyObservedItems([f.B], []));
            var refused = await f.Service.ConfirmAsync(f.Service.Pending.Single(), true);
            Check(!refused.Resolved && f.Db.GetHistory(f.A.Id).Count == 1, "no invented success targets");
            await f.Service.ConfirmAsync(f.Service.Pending.Single(), false);
            Check(!f.Service.GloballyBlocked && !f.Db.IsDeletionBlocked(f.B.PathKey), "explicit failure preserves all");
        });
        await Scenario("live Unknown retains visit and explicit failure reopens it", async f =>
        {
            await Done(f.C.OpenManualAsync(Guid.NewGuid(), f.A.Id));
            await Done(f.C.SetSuppressedAsync(Guid.NewGuid(), f.C.View.ActiveToken!, true));
            var visit=f.C.View.Pending;
            f.Outcome=DeletionOutcome.Unknown;
            Check((await f.C.DeleteCurrentAsync(Guid.NewGuid(),f.Service,DeletionMode.Recycle)).Status==SessionStatus.CommitUnknown,"unknown reported");
            Check(f.C.View.Pending==visit && f.Db.IsDeletionBlocked(f.A.PathKey),"unknown retains suppression and visit");
            Check((await f.C.LeaveAsync(Guid.NewGuid())).Status==SessionStatus.CommitUnknown,"no record while unknown");
            await Done(f.C.ResolveDeletionAsync(Guid.NewGuid(),f.Service,f.Service.Pending.Single(),false));
            Check(f.C.View.Pending==visit && !f.Db.IsDeletionBlocked(f.A.PathKey),"failure confirmation reopens same visit");
            await Done(f.C.LeaveAsync(Guid.NewGuid()));
            Check(f.Db.GetHistory(f.A.Id).Count==1,"past history unchanged");
        });
        await Scenario("failed result journal write keeps quarantine until restart confirmation", async f =>
        {
            await Done(f.C.OpenManualAsync(Guid.NewGuid(),f.A.Id));
            f.Outcome=DeletionOutcome.Failed; f.Journal.FailPhase=DeletionPhase.Failed;
            await f.C.DeleteCurrentAsync(Guid.NewGuid(),f.Service,DeletionMode.Recycle);
            Check(f.Db.IsDeletionBlocked(f.A.PathKey) && f.C.View.Pending is not null,"failed acknowledgement keeps Pending");
            f.Journal.FailPhase=null; await f.Restart();
            Check(f.Service.Pending.Single().Record!.Phase==DeletionPhase.Prepared,"durable phase remains Prepared");
            await f.Service.ConfirmAsync(f.Service.Pending.Single(),false);
            Check(f.Db.GetHistory(f.A.Id).Count==1 && f.OsCalls==1,"no recovery deletion or history removal");
        });
        await Scenario("failed journal removal retry reopens original visit", async f =>
        {
            await Done(f.C.OpenManualAsync(Guid.NewGuid(),f.A.Id)); var visit=f.C.View.Pending;
            f.Outcome=DeletionOutcome.Cancelled; f.Journal.FailRemove=true;
            await f.C.DeleteCurrentAsync(Guid.NewGuid(),f.Service,DeletionMode.Recycle);
            Check(f.Service.Pending.Count==1 && f.Db.IsDeletionBlocked(f.A.PathKey),"terminal phase retained");
            f.Journal.FailRemove=false;
            await Done(f.C.ResolveDeletionAsync(Guid.NewGuid(),f.Service,f.Service.Pending.Single(),false));
            Check(f.C.View.Pending==visit && f.Preparer.Last is not null,"same visit after cleanup retry");
        });
        await Scenario("corrupt target metadata with readable path isolates only that path", async f =>
        {
            var record=await f.Service.PrepareAsync(f.A.PathKey,DeletionMode.Recycle);
            string file=f.Journal.FileFor(record);
            File.WriteAllText(file,File.ReadAllText(file).Replace("\"Version\": 1","\"Version\": 99"));
            await f.Restart();
            Check(!f.Service.GloballyBlocked && f.Db.IsDeletionBlocked(f.A.PathKey) && !f.Db.IsDeletionBlocked(f.B.PathKey),"known corrupt path scoped quarantine");
            await f.Service.ConfirmAsync(f.Service.Pending.Single(),false);
            Check(!f.Db.IsDeletionBlocked(f.A.PathKey),"explicit failure lifts scoped hold");
        });
        await Scenario("duplicate / Busy throughout OS and checkpoint isolation", async f =>
        {
            await Done(f.C.OpenManualAsync(Guid.NewGuid(), f.A.Id));
            var entered = new TaskCompletionSource(); var unblock = new TaskCompletionSource();
            f.BeforeOs = async () => { entered.SetResult(); await unblock.Task; };
            Guid command = Guid.NewGuid();
            var pending = f.C.DeleteCurrentAsync(command, f.Service, DeletionMode.Recycle);
            await entered.Task;
            Check(ReferenceEquals(pending, f.C.DeleteCurrentAsync(command, f.Service, DeletionMode.Recycle)), "duplicate uses same task");
            Check((await f.C.NextAsync(Guid.NewGuid())).Status == SessionStatus.Busy, "navigation not queued");
            Reject(() => f.Db.SaveProgress(f.A.Id, PlaybackProgress.Video(7), 1));
            unblock.SetResult(); await Done(pending);
            Check(f.OsCalls == 1, "exactly one OS operation");
        });
    }
    static async Task Scenario(string name, Func<Fixture,Task> action)
    { using var f = new Fixture(); await action(f); Console.WriteLine("PASS T12: " + name); }
    sealed class FaultJournal(string directory) : DeletionJournal(directory)
    {
        public DeletionPhase? FailPhase; public bool FailRemove, FailAfterWrite;
        public override void Write(DeletionRecord record) { if (record.Phase == FailPhase) throw new IOException("injected journal failure"); base.Write(record); if(FailAfterWrite) throw new IOException("lost write acknowledgement"); }
        public override void Remove(string file) { if (FailRemove) throw new IOException("injected removal failure"); base.Remove(file); }
    }
    sealed class Fixture : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "rmm-t12-" + Guid.NewGuid());
        public string DbPath => Path.Combine(root, "library.db");
        public LibraryDatabase Db;
        public FaultJournal Journal;
        public DeletionService Service = null!;
        public SessionCoordinator C = null!;
        public FakePreparer Preparer = new();
        public MediaItem A, Other, B;
        public int OsCalls;
        public DeletionOutcome Outcome = DeletionOutcome.Succeeded;
        public Func<Task>? BeforeOs;
        public Fixture()
        {
            Directory.CreateDirectory(root); Db = LibraryDatabase.Open(DbPath);
            Journal = new(Path.Combine(root,"delete-journal"));
            Category a = new(Guid.NewGuid(), "a", MediaType.Video), b = new(Guid.NewGuid(),"b",MediaType.Video);
            Db.SaveCategory(a); Db.SaveCategory(b);
            string file = Path.Combine(root,"a.mp4"), second = Path.Combine(root,"b.mp4");
            File.WriteAllText(file,"synthetic"); File.WriteAllText(second,"synthetic");
            A = new(Guid.NewGuid(),a.Id,MediaType.Video,file,file.ToUpperInvariant(),9,1);
            Other = A with { Id = Guid.NewGuid(), CategoryId = b.Id };
            B = A with { Id = Guid.NewGuid(), Path = second, PathKey = second.ToUpperInvariant() };
            Db.ApplyObservedItems([A,Other,B],[]);
            foreach (var item in new[] { A, Other })
            {
                Db.SetFavorite(item.Id,true); Db.SetRandomExcluded(item.Id,true);
                Db.CommitVisit(new(Guid.NewGuid(),item.Id,1,VisitOrigin.Manual,false,PlaybackProgress.Video(5)));
            }
            CreateService();
        }
        void CreateService()
        {
            Service = new(Db,Journal,async r => { OsCalls++; if (BeforeOs is not null) await BeforeOs();
                if (Outcome == DeletionOutcome.Succeeded) File.Delete(r.Path);
                return new(Outcome); });
            C = new(Db,Preparer);
        }
        public async Task Restart() { Db.Dispose(); Db = LibraryDatabase.Open(DbPath); CreateService(); await Service.InitializeAsync(); }
        public void Sql(string sql) { using var c = new SqliteConnection($"Data Source={DbPath};Pooling=False"); c.Open(); using var cmd=c.CreateCommand(); cmd.CommandText=sql; cmd.ExecuteNonQuery(); }
        public void Dispose() { Db.Dispose(); Directory.Delete(root,true); }
    }
    sealed class FakePreparer : ISessionMediaPreparer
    {
        public FakeMedia? Last; public bool Fail;
        public Task<SessionPreparation> PrepareAsync(MediaItem item, PlaybackProgress? progress, SessionToken token, CancellationToken cancel)
        { Last = Fail ? null : new() { Position = progress ?? PlaybackProgress.Video(0) }; return Task.FromResult(new SessionPreparation(Fail ? PreparationStatus.Failed : PreparationStatus.Ready,token,Last)); }
    }
    sealed class FakeMedia : ISessionMedia
    {
        public PlaybackProgress Position = PlaybackProgress.Video(0);
        public bool FailDispose, Disposed;
        public PlaybackProgress PauseAndCapture(SessionToken token) => Position;
        public void Activate(SessionToken token) { }
        public void Resume(SessionToken token) { }
        public ValueTask DisposeAsync() { if(FailDispose) return ValueTask.FromException(new IOException("injected release failure")); Disposed=true; return ValueTask.CompletedTask; }
    }
}
