using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Deletion;
using RandomMultimediaManager.App.Sessions;
using RandomMultimediaManager.Core;

internal static class T15Verification
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "rmm-t15-data-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            using var db = LibraryDatabase.Open(Path.Combine(root, "test.db"));
            var category = new Category(Guid.NewGuid(), "test", MediaType.Video);
            db.SaveCategory(category);
            var path = Path.Combine(root, "synthetic.mp4");
            File.WriteAllText(path, "synthetic");
            var item = new MediaItem(Guid.NewGuid(), category.Id, MediaType.Video, path, path.ToUpperInvariant(), 9, 1);
            db.ApplyObservedItems([item], []);
            var preparer = new Preparer();
            var session = new SessionCoordinator(db, preparer);
            session.SetExitRequested(true);
            Check((await session.OpenManualAsync(Guid.NewGuid(), item.Id)).Status == SessionStatus.Cancelled && preparer.Calls == 0,
                "exit before Opening prevents late preparation");
            session.SetExitRequested(false);
            preparer.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var opening = session.OpenManualAsync(Guid.NewGuid(), item.Id);
            await preparer.Entered.Task;
            var idle = session.WhenIdleAsync();
            session.SetExitRequested(true);
            Check(!idle.IsCompleted, "idle boundary waits for cancelled preparer ownership");
            preparer.Gate.SetResult();
            Check((await opening).Status == SessionStatus.Cancelled, "Opening exit discards late Ready");
            await idle;
            Check(preparer.Last!.Disposals == 1 && session.View.Pending is null, "cancelled Ready releases once without visit");
            session.SetExitRequested(false); preparer.Gate = null;
            await session.OpenManualAsync(Guid.NewGuid(), item.Id);
            var visit = session.View.Pending!.VisitId;
            preparer.Last!.FailDispose = true;
            var leave = await session.LeaveAsync(Guid.NewGuid());
            Check(leave.Status == SessionStatus.Completed && leave.Error is not null && session.ReleaseError is not null,
                "completed save is not successful media release");
            Check((await session.LeaveAsync(Guid.NewGuid())).Status == SessionStatus.Failed
                && db.GetHistory(item.Id).Single().VisitId == visit && preparer.Last.Disposals == 1,
                "repeated exit cannot forget release error or duplicate commit/dispose");
            var journal = new FaultJournal(Path.Combine(root, "journal"));
            var service = new DeletionService(db, journal, _ => Task.FromResult(new FileDeletionResult(DeletionOutcome.Succeeded)));
            var record = await service.PrepareAsync(item.PathKey, DeletionMode.Recycle);
            journal.FailResult = true;
            await service.RecordResultAsync(record, new(DeletionOutcome.Succeeded));
            Check(service.Pending.Single().Record!.Phase == DeletionPhase.Prepared && service.HasIncompleteSuccess,
                "known success with failed result write blocks exit despite durable Prepared");
            journal.FailResult = false;
            await service.ConfirmAsync(service.Pending.Single(), true);
            Check(!service.HasIncompleteSuccess && service.Pending.Count == 0, "recovery clears success exit barrier");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool ok, string name)
    { if (!ok) throw new Exception(name); Console.WriteLine("PASS T15 " + name); }
    private sealed class FaultJournal(string path) : DeletionJournal(path)
    {
        public bool FailResult;
        public override void Write(DeletionRecord record)
        { if (FailResult && record.Phase == DeletionPhase.Succeeded) throw new IOException("injected result failure"); base.Write(record); }
    }
    private sealed class Preparer : ISessionMediaPreparer
    {
        public int Calls;
        public Media? Last;
        public TaskCompletionSource? Gate;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<SessionPreparation> PrepareAsync(MediaItem item, PlaybackProgress? progress, SessionToken token, CancellationToken cancellation)
        {
            Calls++; Entered.TrySetResult();
            if (Gate is not null) await Gate.Task;
            return new(PreparationStatus.Ready, token, Last = new Media());
        }
    }
    private sealed class Media : ISessionMedia
    {
        public int Disposals;
        public bool FailDispose;
        public void Activate(SessionToken token) { }
        public void Resume(SessionToken token) { }
        public PlaybackProgress PauseAndCapture(SessionToken token) => PlaybackProgress.Video(42);
        public ValueTask DisposeAsync()
        { Disposals++; return FailDispose ? ValueTask.FromException(new IOException("injected release failure")) : ValueTask.CompletedTask; }
    }
}
