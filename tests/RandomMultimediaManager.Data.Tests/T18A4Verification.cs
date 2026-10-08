using System.IO;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Sessions;
using RandomMultimediaManager.Core;

internal static class T18A4Verification
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    static async Task Scenario(string name, Func<Fixture, Task> test)
    { using var f = new Fixture(); await test(f); Console.WriteLine("PASS T18A-4 " + name); }
    public static async Task RunAsync()
    {
        await Scenario("N06 source overlap, binding mismatch and independent failed-open set", async f =>
        {
            f.Unavailable.Add(f.Source.RootPath);
            var nested = new CategorySource(Guid.NewGuid(), f.Category.Id, @"Z:\media\nested", @"Z:\MEDIA\NESTED");
            f.Db.AddSource(nested);
            var available = await f.Access.AvailableSourcesAsync(f.Db.GetSessionSnapshot().Sources, CancellationToken.None);
            Check(!available.Contains(f.Source.Id) && available.Contains(nested.Id), "one accessible nested source survives sibling failure");
            f.Db.RemoveSource(nested.Id);
            Check((await f.C.StartRandomAsync(Guid.NewGuid(), [f.Category.Id])).Status == SessionStatus.ConnectionUnavailable, "connection exhaustion separate from period exhaustion");
            Check(f.P.Calls == 0 && f.C.View.Seen.Count == 0, "no media open or Seen mutation");
            f.Unavailable.Clear();
            Check((await f.C.StartRandomAsync(Guid.NewGuid(), [f.Category.Id])).Status == SessionStatus.Completed, "one draw succeeds");
            var prior = f.C.View;
            f.P.Fail = true;
            Check((await f.C.NextAsync(Guid.NewGuid())).Status == SessionStatus.Failed && f.P.Calls == 2, "one failing candidate only");
            Check(f.C.View.Pending == prior.Pending && f.C.View.Cursor == prior.Cursor && f.C.View.Seen.SetEquals(prior.Seen), "Pending/cursor/Seen unchanged");
            Check((await f.C.NextAsync(Guid.NewGuid())).Status == SessionStatus.ConnectionUnavailable && f.P.Calls == 2, "failed candidate not drawn again");
            Check(f.Db.GetItems(f.Category.Id).All(i => !i.IsMissing && !i.IsRandomExcluded) && f.Db.GetHistory(f.B.Id).Count == 0, "no persistent flags/history");
            f.P.Fail = false;
            Check((await f.C.OpenManualAsync(Guid.NewGuid(), f.B.Id)).Status == SessionStatus.Completed, "manual retry clears failed item");
            var current = f.C.View;
            f.Evidence = StorageObservation.Classify(f.A.Path, MappingLookup.Mapped, @"\\other\share");
            Check((await f.C.PreviousAsync(Guid.NewGuid())).Status == SessionStatus.Failed, "Back binding recheck");
            Check(f.C.View.Pending == current.Pending && f.C.View.Cursor == current.Cursor, "failed Back leaves Forward/Pending intact");
            f.Evidence = f.Original;
            Check((await f.C.PreviousAsync(Guid.NewGuid())).Status == SessionStatus.Completed, "restored binding explicit Back");
            Check((await f.C.NextAsync(Guid.NewGuid())).Status == SessionStatus.Completed, "Forward preserved");
            await f.C.LeaveAsync(Guid.NewGuid());
        });
        await Scenario("N09 late Ready discarded; actual release keeps Busy and shutdown drain", async f =>
        {
            await f.C.OpenManualAsync(Guid.NewGuid(), f.A.Id);
            var prior = f.C.View;
            f.P.ReadyEntered = Signal(); f.P.ReadyRelease = Signal();
            f.P.DisposeEntered = Signal(); f.P.DisposeRelease = Signal();
            var command = f.C.OpenManualAsync(Guid.NewGuid(), f.B.Id);
            await f.P.ReadyEntered.Task;
            f.Evidence = StorageObservation.Classify(f.B.Path, MappingLookup.Mapped, @"\\other\share");
            f.P.ReadyRelease.SetResult();
            await f.P.DisposeEntered.Task;
            Check(!command.IsCompleted && !f.C.WhenIdleAsync().IsCompleted, "late media still owned during release");
            Check((await f.C.NextAsync(Guid.NewGuid())).Status == SessionStatus.Busy, "no second IO admission");
            Check(f.C.View.Pending == prior.Pending && f.Db.GetHistory(f.A.Id).Count == 0, "no visit commit before safe Ready");
            f.C.SetExitRequested(true);
            f.P.DisposeRelease.SetResult();
            Check((await command).Status == SessionStatus.Failed, "changed late Ready rejected");
            Check(f.P.Activations == 1 && f.P.Releases == 1 && f.C.View.Pending == prior.Pending, "candidate released only, old visit preserved");
            Check((await f.C.OpenManualAsync(Guid.NewGuid(), f.B.Id)).Status == SessionStatus.Cancelled, "Closing opens zero new media");
            await f.C.LeaveAsync(Guid.NewGuid());
        });
        await Scenario("N11 source probe cancellation waits for actual IO", async f =>
        {
            var entered = Signal(); var release = Signal();
            f.Probe = async () => { entered.SetResult(); await release.Task; };
            var start = f.C.StartRandomAsync(Guid.NewGuid(), [f.Category.Id]);
            await entered.Task;
            f.C.SetExitRequested(true);
            Check(!start.IsCompleted && !f.C.WhenIdleAsync().IsCompleted, "cancel is not completion");
            release.SetResult();
            Check((await start).Status == SessionStatus.Cancelled && f.P.Calls == 0, "no media after Closing during metadata");
            Check(!f.Db.IsDisposed, "DB remains owned through drain");
        });
        await Scenario("N03 revision change during Ready rejects same target old generation", async f =>
        {
            f.P.ReadyEntered = Signal(); f.P.ReadyRelease = Signal();
            var command = f.C.OpenManualAsync(Guid.NewGuid(), f.A.Id);
            await f.P.ReadyEntered.Task;
            var binding = f.Db.GetStorageBinding(f.A.Path)!;
            f.Db.ConfirmStorageBinding(f.A.Path, binding.Revision, f.Original, true);
            f.P.ReadyRelease.SetResult();
            Check((await command).Status == SessionStatus.Failed && f.P.Releases == 1 && f.C.View.Pending is null, "revision protects late Ready");
        });
        await Scenario("N06 new session releases suppression without relaxing history", async f =>
        {
            await f.C.StartRandomAsync(Guid.NewGuid(), [f.Category.Id]);
            f.P.Fail = true; await f.C.NextAsync(Guid.NewGuid()); f.P.Fail = false;
            f.Db.SaveSettings(new AppSettings(0));
            Check((await f.C.StartRandomAsync(Guid.NewGuid(), [f.Category.Id])).Status == SessionStatus.Completed, "fresh session resets failures");
            await f.C.LeaveAsync(Guid.NewGuid());
            f.Db.SaveSettings(new AppSettings(7));
            Check((await f.C.StartRandomAsync(Guid.NewGuid(), [f.Category.Id])).Status == SessionStatus.Completed, "unviewed B remains eligible");
            Check((await f.C.NextAsync(Guid.NewGuid())).Status == SessionStatus.NoCandidates, "seen/period exhaustion retains original status");
            await f.C.LeaveAsync(Guid.NewGuid());
        });
    }
    sealed class Fixture : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "rmm-t18a4-" + Guid.NewGuid());
        public LibraryDatabase Db; public Category Category; public CategorySource Source; public MediaItem A, B;
        public StorageObservation Original, Evidence; public ViewingAccess Access; public SessionCoordinator C;
        public readonly Preparer P = new(); public HashSet<string> Unavailable = []; public Func<Task>? Probe;
        public Fixture()
        {
            Directory.CreateDirectory(root); Db = LibraryDatabase.Open(Path.Combine(root, "db.sqlite"));
            Category = new(Guid.NewGuid(), "network", MediaType.Video); Db.SaveCategory(Category);
            Source = Db.AddSource(new(Guid.NewGuid(), Category.Id, @"Z:\media", @"Z:\MEDIA"));
            Original = Evidence = StorageObservation.Classify(Source.RootPath, MappingLookup.Mapped, @"\\server\share");
            Db.ConfirmStorageBinding(Source.RootPath, 0, Original, true);
            A = new(Guid.NewGuid(), Category.Id, MediaType.Video, @"Z:\media\a.mp4", @"Z:\MEDIA\A.MP4", 1, 1);
            B = new(Guid.NewGuid(), Category.Id, MediaType.Video, @"Z:\media\b.mp4", @"Z:\MEDIA\B.MP4", 1, 1);
            Db.ApplyObservedItems(Category.Id, [A, B], 1);
            Access = new(Db, observe: async (_, _) => { if (Probe is { } p) await p(); return Evidence; },
                accessible: (p, _) => Unavailable.Contains(p) ? Task.FromException(new IOException("offline")) : Task.CompletedTask);
            C = new(Db, P, clock: () => 2000, draw: _ => 0, access: Access);
        }
        public void Dispose() { Db.Dispose(); Directory.Delete(root, true); }
    }
    sealed class Preparer : ISessionMediaPreparer
    {
        public int Calls, Activations, Releases; public bool Fail;
        public TaskCompletionSource? ReadyEntered, ReadyRelease, DisposeEntered, DisposeRelease;
        public async Task<SessionPreparation> PrepareAsync(MediaItem item, PlaybackProgress? progress, SessionToken token, CancellationToken ct)
        {
            Calls++; if (Fail) return new(PreparationStatus.Failed, token, Error: "open failure");
            if (ReadyRelease is { } release) { ReadyEntered!.TrySetResult(); await release.Task; ReadyRelease = null; }
            return new(PreparationStatus.Ready, token, new Media(this));
        }
    }
    sealed class Media(Preparer p) : ISessionMedia
    {
        public void Activate(SessionToken t) { p.Activations++; }
        public void Resume(SessionToken t) { }
        public PlaybackProgress PauseAndCapture(SessionToken t) => PlaybackProgress.Video(123);
        public async ValueTask DisposeAsync()
        {
            if (p.DisposeRelease is { } release) { p.DisposeEntered!.TrySetResult(); await release.Task; p.DisposeRelease = null; }
            p.Releases++;
        }
    }
}
