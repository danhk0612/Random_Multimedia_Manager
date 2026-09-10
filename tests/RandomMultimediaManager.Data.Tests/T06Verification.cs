using Microsoft.Data.Sqlite;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Sessions;
using RandomMultimediaManager.Core;

internal static class T06Verification
{
    static Guid Id() => Guid.NewGuid();
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static async Task Expect(Task<SessionResult> task, SessionStatus status = SessionStatus.Completed)
    { var result = await task; Check(result.Status == status, $"Expected {status}: {result}"); }

    public static async Task RunAsync()
    {
        await Scenario("four visits, suppression, past history and progress", async f =>
        {
            await Expect(f.C.OpenManualAsync(Id(), f.A.Id));
            var first = f.C.View.ActiveToken!;
            await Expect(f.C.SetSuppressedAsync(Id(), first, true));
            f.P.Last!.Position = PlaybackProgress.Video(321);
            await Expect(f.C.OpenManualAsync(Id(), f.B.Id));
            Check(f.Db.GetHistory(f.A.Id).Count == 0 && f.Db.GetProgress(f.A.Id)!.Position.VideoPositionMs == 321, "suppression independent of progress");
            await Expect(f.C.PreviousAsync(Id()));
            Check(!f.C.View.Pending!.SuppressHistory, "Back suppression reset");
            Check(!f.C.ObserveProgress(first, PlaybackProgress.Video(8)), "stale visit callback");
            await Expect(f.C.NextAsync(Id()));
            await Expect(f.C.LeaveAsync(Id()));
            var histories = f.Db.GetHistory(f.A.Id).Concat(f.Db.GetHistory(f.B.Id)).ToArray();
            Check(histories.Length == 3 && histories.Select(h => h.VisitId).Distinct().Count() == 3, "four visits, one suppressed");
            Check(histories.Any(h => h.Origin == VisitOrigin.Back) && histories.Any(h => h.Origin == VisitOrigin.Forward), "visit origins");
            await Expect(f.C.OpenManualAsync(Id(), f.A.Id));
            await Expect(f.C.SetSuppressedAsync(Id(), f.C.View.ActiveToken!, true));
            await Expect(f.C.LeaveAsync(Id()));
            Check(f.Db.GetHistory(f.A.Id).Count == 1, "past history retained");
            Check(f.P.Live == 0 && f.P.MaxLive <= 2, "owners released");
        });
        await Scenario("manual insertion / no-op / Forward ignores random filters", async f =>
        {
            await Expect(f.C.OpenManualAsync(Id(), f.A.Id));
            var first = f.C.View.Pending;
            await Expect(f.C.OpenManualAsync(Id(), f.A.Id), SessionStatus.NoOp);
            Check(first == f.C.View.Pending, "same current no-op");
            await Expect(f.C.OpenManualAsync(Id(), f.B.Id));
            await Expect(f.C.PreviousAsync(Id()));
            await Expect(f.C.OpenManualAsync(Id(), f.D.Id));
            Check(f.C.View.Slots.Select(x => x.ItemId).SequenceEqual(new[] { f.A.Id, f.D.Id, f.B.Id }), "Forward preserved");
            f.Db.SetRandomExcluded(f.B.Id, true);
            f.Db.SaveCategory(f.Category with { IsEnabled = false });
            f.Db.RemoveSource(f.Source.Id);
            await Expect(f.C.NextAsync(Id()));
            Check(f.C.View.Pending!.ItemId == f.B.Id, "Forward bypasses filters");
            await Expect(f.C.NextAsync(Id()), SessionStatus.SelectionRequired);
            await Expect(f.C.LeaveAsync(Id()));
        });
        await Scenario("random uniform item draw, fixed selection and exhaustion", async f =>
        {
            var selection = new HashSet<Guid> { f.Category.Id };
            await Expect(f.C.StartRandomAsync(Id(), selection));
            selection.Clear();
            Check(f.DrawCounts.Single() == 3 && f.C.View.Selected.Count == 1, "uniform index range and fixed selection");
            await Expect(f.C.NextAsync(Id())); await Expect(f.C.NextAsync(Id()));
            var before = f.C.View;
            await Expect(f.C.NextAsync(Id()), SessionStatus.NoCandidates);
            Check(f.C.View.Pending == before.Pending && f.C.View.Seen.Count == 3, "exhaustion retains visit");
            f.P.FailNext = true;
            f.Db.SaveSettings(new AppSettings(0));
            await Expect(f.C.StartRandomAsync(Id(), [f.Category.Id]), SessionStatus.Failed);
            Check(f.C.View.SessionId == before.SessionId && f.C.View.Pending == before.Pending, "failed restart keeps session");
            await Expect(f.C.StartRandomAsync(Id(), [f.Category.Id]));
            Check(f.C.View.SessionId != before.SessionId && f.C.View.Seen.Count == 1, "successful restart replaces session");
            await Expect(f.C.LeaveAsync(Id()));
        });
        await Scenario("Busy, duplicate command, cancelled late Ready and stale token", async f =>
        {
            await Expect(f.C.OpenManualAsync(Id(), f.A.Id));
            var before = f.C.View;
            var blocker = f.P.Block();
            var command = Id();
            var transition = f.C.OpenManualAsync(command, f.B.Id);
            await f.P.Entered.Task;
            Check(ReferenceEquals(transition, f.C.OpenManualAsync(command, f.B.Id)), "duplicate in-flight task");
            await Expect(f.C.NextAsync(Id()), SessionStatus.Busy);
            await Expect(f.C.SetSuppressedAsync(Id(), before.ActiveToken!, true), SessionStatus.Busy);
            var token = f.C.PreparingToken!;
            Check(!f.C.CancelOpening(token.SessionId, Id()), "wrong operation token");
            Check(f.C.CancelOpening(token.SessionId, token.OperationId), "cancel admitted");
            blocker.SetResult();
            await Expect(transition, SessionStatus.Cancelled);
            Check(f.C.View.Pending == before.Pending && f.P.Live == 1 && f.Db.GetHistory(f.A.Id).Count == 0, "late Ready disposed without commit");
            await Expect(f.C.OpenManualAsync(command, f.B.Id), SessionStatus.Cancelled);
            f.P.WrongToken = true;
            await Expect(f.C.OpenManualAsync(Id(), f.B.Id), SessionStatus.Cancelled);
            Check(f.C.View.Pending == before.Pending && f.P.Live == 1, "wrong Ready token disposed");
            f.P.FailNext = true;
            await Expect(f.C.OpenManualAsync(Id(), f.B.Id), SessionStatus.Failed);
            Check(f.C.View.Pending == before.Pending && f.C.View.Seen.Count == 1, "failed preparation preserves current");
            await Expect(f.C.LeaveAsync(Id()));
        });
        await Scenario("SQLite rollback, fixed payload retry, exact once", async f =>
        {
            await Expect(f.C.OpenManualAsync(Id(), f.A.Id));
            f.Sql("CREATE TRIGGER fail_visit BEFORE INSERT ON ViewHistory BEGIN SELECT RAISE(ABORT,'test'); END;");
            f.P.Last!.Position = PlaybackProgress.Video(77);
            await Expect(f.C.OpenManualAsync(Id(), f.B.Id), SessionStatus.SaveFailed);
            var frozen = f.C.View.FrozenCommit!;
            Check(f.Db.CheckVisitCommit(frozen) == VisitCommitCheck.NotCommitted && f.Db.GetProgress(f.A.Id) is null, "rollback atomic");
            Check(f.P.Live == 1 && f.C.View.Pending!.ItemId == f.A.Id, "target released on failure");
            await Expect(f.C.NextAsync(Id()), SessionStatus.Busy);
            f.Now += 100;
            f.Sql("DROP TRIGGER fail_visit;");
            await Expect(f.C.RetryAsync(Id()));
            Check(f.Db.GetHistory(f.A.Id).Single().ViewedAtUtc == frozen.ViewedAtUtc, "retry time frozen");
            Check(f.Db.GetProgress(f.A.Id)!.Position.VideoPositionMs == 77, "retry position frozen");
            Check(f.Db.CommitVisit(frozen).Status == CommitStatus.AlreadyCommitted, "idempotent marker");
            Check(f.Db.CheckVisitCommit(frozen with { ViewedAtUtc = frozen.ViewedAtUtc + 1 }) == VisitCommitCheck.PayloadMismatch, "full payload confirmation");
            await Expect(f.C.LeaveAsync(Id()));
        });
        await Scenario("confirmed rollback resume recaptures, lost commit acknowledgement", async f =>
        {
            await Expect(f.C.OpenManualAsync(Id(), f.A.Id));
            f.FailCommit = true;
            await Expect(f.C.OpenManualAsync(Id(), f.B.Id), SessionStatus.SaveFailed);
            var frozen = f.C.View.FrozenCommit!;
            await Expect(f.C.ResumeAfterSaveFailureAsync(Id()));
            Check(f.P.LastActive!.Resumed, "playback state resumed");
            f.Now += 500;
            f.P.LastActive.Position = PlaybackProgress.Video(999);
            f.FailCommit = false; f.LoseAcknowledgement = true;
            await Expect(f.C.OpenManualAsync(Id(), f.B.Id));
            Check(f.Db.GetHistory(f.A.Id).Single().ViewedAtUtc != frozen.ViewedAtUtc, "new capture after confirmed rollback");
            Check(f.Db.GetProgress(f.A.Id)!.Position.VideoPositionMs == 999, "latest position recaptured");
            Check(f.Db.GetHistory(f.A.Id).Count == 1, "lost acknowledgement confirmed by marker");
            await Expect(f.C.LeaveAsync(Id()));
        });
        await Scenario("payload conflict blocks return and new visit", async f =>
        {
            await Expect(f.C.OpenManualAsync(Id(), f.A.Id));
            f.ConflictCommit = true;
            await Expect(f.C.OpenManualAsync(Id(), f.B.Id), SessionStatus.CommitUnknown);
            await Expect(f.C.ResumeAfterSaveFailureAsync(Id()), SessionStatus.CommitUnknown);
            await Expect(f.C.OpenManualAsync(Id(), f.D.Id), SessionStatus.Busy);
            Check(f.C.View.Pending!.ItemId == f.A.Id && f.P.Live == 1, "conflict retains current");
            // Test-only remove the injected conflicting marker and history; never product recovery.
            f.Sql("DELETE FROM ViewHistory; DELETE FROM VisitCommit;");
            f.ConflictCommit = false;
            await Expect(f.C.RetryAsync(Id()));
            await Expect(f.C.LeaveAsync(Id()));
        });
        await Scenario("deletion failure/cancel, tombstone and isolated path", async f =>
        {
            await Expect(f.C.OpenManualAsync(Id(), f.A.Id));
            await Expect(f.C.OpenManualAsync(Id(), f.B.Id));
            var visit = f.C.View.Pending;
            await Expect(f.C.ReportDeletionAsync(Id(), f.B.PathKey, DeletionOutcome.Failed));
            await Expect(f.C.ReportDeletionAsync(Id(), f.B.PathKey, DeletionOutcome.Cancelled));
            Check(f.C.View.Pending == visit && f.Db.GetHistory(f.A.Id).Count == 1, "delete failure/cancel preserves history");
            await Expect(f.C.ReportDeletionAsync(Id(), f.B.PathKey, DeletionOutcome.Succeeded));
            Check(f.C.View.Pending is null && f.C.View.Cursor == 1 && f.C.View.Slots[1].Deleted && f.P.Live == 0, "deleted current no auto advance");
            await Expect(f.C.OpenManualAsync(Id(), f.B.Id), SessionStatus.Failed);
            await Expect(f.C.PreviousAsync(Id()));
            Check(f.C.View.Pending!.ItemId == f.A.Id, "back from tombstone");
            await Expect(f.C.ReportDeletionAsync(Id(), f.D.PathKey, DeletionOutcome.Unknown));
            await Expect(f.C.OpenManualAsync(Id(), f.D.Id), SessionStatus.Failed);
            await Expect(f.C.ReleaseDeletionQuarantineAsync(Id(), f.D.PathKey));
            await Expect(f.C.OpenManualAsync(Id(), f.D.Id));
            await Expect(f.C.LeaveAsync(Id()));
            Check(f.Db.GetHistory(f.B.Id).Count == 0, "deleted pending not committed");
        });
    }

    static async Task Scenario(string name, Func<Fixture, Task> test)
    {
        using var f = new Fixture();
        await test(f);
        Check(f.P.MaxLive <= 2, "maximum current + prepared ownership");
        Console.WriteLine("PASS: T06 " + name);
    }

    sealed class Fixture : IDisposable
    {
        public string DirectoryPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rmm-t06-" + Id());
        public LibraryDatabase Db;
        public SessionCoordinator C;
        public FakePreparer P = new();
        public Category Category = new(Id(), "Videos", MediaType.Video);
        public CategorySource Source;
        public MediaItem A, B, D;
        public long Now = 2000000000000;
        public List<int> DrawCounts = [];
        public bool FailCommit, LoseAcknowledgement, ConflictCommit;
        public Fixture()
        {
            Db = LibraryDatabase.Open(System.IO.Path.Combine(DirectoryPath, "library.db"));
            Db.SaveCategory(Category);
            Source = Db.AddSource(new(Id(), Category.Id, @"C:\Media", @"C:\MEDIA"));
            MediaItem Item(string name) => new(Id(), Category.Id, MediaType.Video,
                @"C:\Media\" + name + ".mp4", @"C:\MEDIA\" + name.ToUpperInvariant() + ".MP4", 1, 0);
            A = Item("a"); B = Item("b"); D = Item("d");
            Db.ApplyObservedItems([A, B, D], []);
            C = new(Db, P, () => Now, n => { DrawCounts.Add(n); return 0; }, request =>
            {
                if (FailCommit) return new(CommitStatus.Failed);
                if (ConflictCommit) { Db.CommitVisit(request with { ViewedAtUtc = request.ViewedAtUtc + 1 }); throw new Exception("conflict"); }
                var result = Db.CommitVisit(request);
                if (LoseAcknowledgement) throw new Exception("lost acknowledgement");
                return result;
            });
        }
        public void Sql(string sql)
        {
            using var connection = new SqliteConnection($"Data Source={System.IO.Path.Combine(DirectoryPath, "library.db")};Pooling=False");
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
        }
        public void Dispose() { Db.Dispose(); System.IO.Directory.Delete(DirectoryPath, true); }
    }
    sealed class FakePreparer : ISessionMediaPreparer
    {
        public bool FailNext, WrongToken;
        public int Live, MaxLive;
        public FakeMedia? Last, LastActive;
        TaskCompletionSource? blocked;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Block()
        {
            Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        public async Task<SessionPreparation> PrepareAsync(MediaItem item, PlaybackProgress? progress, SessionToken token, CancellationToken cancel)
        {
            if (FailNext) { FailNext = false; return new(PreparationStatus.Failed, token); }
            Last = new FakeMedia(this, token, progress ?? PlaybackProgress.Video(0));
            var media = Last;
            Live++; MaxLive = Math.Max(MaxLive, Live);
            var wait = blocked; blocked = null;
            Entered.TrySetResult();
            if (wait is not null) await wait.Task; // Deliberately ignore cancellation to model late native completion.
            if (WrongToken) { WrongToken = false; return new(PreparationStatus.Ready, token with { OperationId = Id() }, media); }
            return new(PreparationStatus.Ready, token, media);
        }
    }
    sealed class FakeMedia(FakePreparer owner, SessionToken prepared, PlaybackProgress position) : ISessionMedia
    {
        public PlaybackProgress Position = position;
        public bool Resumed;
        private SessionToken? active;
        private bool disposed;
        public PlaybackProgress PauseAndCapture(SessionToken token)
        { Check(token == active && !disposed, "capture current token"); return Position; }
        public void Resume(SessionToken token) { Check(token == active && !disposed, "resume current token"); Resumed = true; }
        public void Activate(SessionToken token)
        { Check(token with { VisitId = null } == prepared && !disposed, "activate prepared token"); active = token; owner.LastActive = this; }
        public ValueTask DisposeAsync() { if (!disposed) { disposed = true; owner.Live--; } return ValueTask.CompletedTask; }
    }
}
