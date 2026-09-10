using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.Sessions;

// One coordinator owns all session mutations and at most current + prepared media.
// T11 calls this from its dispatcher; async continuations retain that context. Admission is
// thread-safe, rejects Busy immediately, and never queues another navigation operation.
public sealed class SessionCoordinator
{
    private readonly object gate = new();
    private readonly LibraryDatabase database;
    private readonly ISessionMediaPreparer preparer;
    private readonly Func<long> clock;
    private readonly Func<int, int> draw;
    private readonly Func<VisitCommitRequest, CommitResult> commit;
    private readonly Dictionary<Guid, Task<SessionResult>> commands = [];
    private readonly Queue<Guid> commandOrder = [];
    private readonly HashSet<string> quarantined = new(StringComparer.Ordinal);
    private SessionPath? path;
    private ISessionMedia? current;
    private SessionToken? activeToken;
    private SessionPhase phase;
    private Guid runningCommand;
    private Task<SessionResult>? runningTask;
    private CancellationTokenSource? opening;
    private bool cancelled;
    private bool busy;
    private Transition? failed;
    private VisitCommitRequest? frozen;
    private PlaybackProgress? latestProgress;

    private sealed record Transition(SessionPath Destination, MediaItem? Item,
        VisitOrigin Origin, int? Index = null);

    public SessionCoordinator(LibraryDatabase database, ISessionMediaPreparer preparer,
        Func<long>? clock = null, Func<int, int>? draw = null,
        Func<VisitCommitRequest, CommitResult>? commitVisit = null)
    {
        this.database = database;
        this.preparer = preparer;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        this.draw = draw ?? Random.Shared.Next;
        commit = commitVisit ?? database.CommitVisit;
    }

    public SessionView View
    {
        get { lock (gate) return new(path?.SessionId, phase, path?.Cursor ?? -1,
            path?.Slots.ToArray() ?? [], path?.Seen ?? new HashSet<Guid>(),
            path?.Selected ?? new HashSet<Guid>(), path?.Pending, activeToken, frozen); }
    }

    private Task<SessionResult> Command(Guid id, Func<Task<SessionResult>> action, bool recovery = false)
    {
        TaskCompletionSource<SessionResult> completion;
        lock (gate)
        {
            if (id == Guid.Empty) throw new ArgumentException("CommandId is required.");
            if (busy && id == runningCommand) return runningTask!;
            if (commands.TryGetValue(id, out var previous)) return previous;
            if (busy || (phase == SessionPhase.SaveFailed && !recovery))
            {
                var rejected = Task.FromResult(new SessionResult(SessionStatus.Busy));
                Remember(id, rejected);
                return rejected;
            }
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            busy = true;
            runningCommand = id;
            runningTask = completion.Task;
            Remember(id, completion.Task);
        }
        _ = Run();
        return completion.Task;

        async Task Run()
        {
            SessionResult result;
            try { result = await action(); }
            catch (Exception ex)
            {
                lock (gate)
                {
                    // Never discard a frozen commit on an unexpected failure.
                    phase = frozen is null ? RestingPhase : SessionPhase.SaveFailed;
                    result = new(frozen is null ? SessionStatus.Failed : SessionStatus.SaveFailed, ex.Message);
                }
            }
            lock (gate)
            {
                busy = false;
                runningTask = null;
                completion.SetResult(result);
            }
        }
    }
    private void Remember(Guid id, Task<SessionResult> result)
    {
        commands.Add(id, result);
        commandOrder.Enqueue(id);
        while (commandOrder.Count > 256) commands.Remove(commandOrder.Dequeue());
    }
    private SessionPhase RestingPhase => path?.Pending is null ? SessionPhase.Empty : SessionPhase.Active;

    public Task<SessionResult> StartRandomAsync(Guid commandId, IEnumerable<Guid> categories)
    {
        var selection = categories.ToHashSet();
        return Command(commandId, () => ChooseRandom(new SessionPath(selection)));
    }
    public Task<SessionResult> NextAsync(Guid commandId) => Command(commandId, async () =>
    {
        if (path is null) return new(SessionStatus.SelectionRequired);
        var snapshot = await Task.Run(database.GetSessionSnapshot);
        int index = path.FindNeighbor(1, Missing(snapshot));
        if (index >= 0) return await Move(new(path, Find(snapshot, path.Slots[index].ItemId), VisitOrigin.Forward, index));
        return await ChooseRandom(path, snapshot);
    });
    public Task<SessionResult> PreviousAsync(Guid commandId) => Command(commandId, async () =>
    {
        if (path is null) return new(SessionStatus.NoPrevious);
        var snapshot = await Task.Run(database.GetSessionSnapshot);
        int index = path.FindNeighbor(-1, Missing(snapshot));
        return index < 0 ? new(SessionStatus.NoPrevious)
            : await Move(new(path, Find(snapshot, path.Slots[index].ItemId), VisitOrigin.Back, index));
    });
    public Task<SessionResult> OpenManualAsync(Guid commandId, Guid itemId) => Command(commandId, async () =>
    {
        if (path?.Pending?.ItemId == itemId) return new(SessionStatus.NoOp);
        var destination = path ?? new SessionPath([]);
        if (!destination.CanInsert) return new(SessionStatus.SessionLimit);
        var snapshot = await Task.Run(database.GetSessionSnapshot);
        return await Move(new(destination, Find(snapshot, itemId), VisitOrigin.Manual));
    });
    private static MediaItem Find(CandidateSnapshot snapshot, Guid id) =>
        snapshot.Items.Single(i => i.Id == id);
    private static HashSet<Guid> Missing(CandidateSnapshot snapshot) =>
        snapshot.Items.Where(i => i.IsMissing).Select(i => i.Id).ToHashSet();

    private async Task<SessionResult> ChooseRandom(SessionPath destination, CandidateSnapshot? snapshot = null)
    {
        if (destination.Selected.Count == 0) return new(SessionStatus.SelectionRequired);
        if (!destination.CanInsert) return new(SessionStatus.SessionLimit);
        long now = clock();
        snapshot ??= await Task.Run(database.GetSessionSnapshot);
        var settings = await Task.Run(database.GetSettings);
        var candidates = CandidatePolicy.GetCandidates(snapshot, destination.Selected, destination.Seen,
            quarantined, now, settings.HistoryExclusionDays);
        if (candidates.Count == 0) return new(SessionStatus.NoCandidates);
        return await Move(new(destination, candidates[draw(candidates.Count)], VisitOrigin.Random));
    }

    public Task<SessionResult> SetSuppressedAsync(Guid commandId, SessionToken token, bool desired) =>
        Command(commandId, () => Task.FromResult(Matches(token)
            && path!.SetSuppressed(token.VisitId!.Value, desired)
                ? new SessionResult(SessionStatus.Completed) : new(SessionStatus.Stale)));

    private bool Matches(SessionToken token) => token == activeToken && path?.Pending?.VisitId == token.VisitId;

    // In-memory latest position only. T11 owns periodic DB checkpoints and must serialize them
    // with navigation; a callback from any previous operation/visit cannot replace this position.
    public bool ObserveProgress(SessionToken token, PlaybackProgress progress)
    {
        lock (gate)
        {
            if (busy || phase != SessionPhase.Active || !Matches(token)) return false;
            progress.Validate();
            if (progress.MediaType != latestProgress?.MediaType) return false;
            latestProgress = progress;
            return true;
        }
    }

    // Cancellation invalidates only preparation. Once saving starts, finish that atomic boundary.
    // Busy remains until the preparer returns and its result has been disposed (maximum two owners).
    public bool CancelOpening(Guid sessionId, Guid operationId)
    {
        lock (gate)
        {
            if (phase != SessionPhase.Opening || preparationToken?.SessionId != sessionId
                || preparationToken.OperationId != operationId) return false;
            cancelled = true;
            opening?.Cancel();
            return true;
        }
    }
    private SessionToken? preparationToken;
    public SessionToken? PreparingToken { get { lock (gate) return preparationToken; } }

    private async Task<SessionResult> Move(Transition transition)
    {
        ISessionMedia? ready = null;
        try
        {
            if (path?.Pending is not null && quarantined.Contains(path.Slots[path.Cursor].PathKey))
                return new(SessionStatus.CommitUnknown, "Resolve the current path deletion before saving its visit.");
            var item = transition.Item;
            SessionToken? targetToken = null;
            if (item is not null)
            {
                if (item.IsMissing || quarantined.Contains(item.PathKey))
                    return new(SessionStatus.Failed, "Item is missing or quarantined.");
                var settings = await Task.Run(database.GetSettings);
                var progress = settings.ResumeMode == ResumeMode.Resume
                    ? (await Task.Run(() => database.GetProgress(item.Id)))?.Position : null;
                lock (gate)
                {
                    phase = SessionPhase.Opening;
                    cancelled = false;
                    opening = new CancellationTokenSource();
                    targetToken = preparationToken = new(transition.Destination.SessionId, Guid.NewGuid(), item.Id);
                }
                SessionPreparation result;
                try { result = await preparer.PrepareAsync(item, progress, targetToken, opening.Token); }
                catch (OperationCanceledException) { return new(SessionStatus.Cancelled); }
                ready = result.Media;
                lock (gate)
                {
                    if (cancelled || result.Token != targetToken || result.Status == PreparationStatus.Cancelled)
                        return new(SessionStatus.Cancelled);
                    if (result.Status != PreparationStatus.Ready || ready is null)
                        return new(SessionStatus.Failed, result.Error);
                    // The cancellation boundary and Saving phase change are atomic.
                    phase = SessionPhase.Saving;
                }
            }
            else { lock (gate) phase = SessionPhase.Saving; }

            if (path?.Pending is not null)
            {
                if (frozen is null)
                {
                    var position = current!.PauseAndCapture(activeToken!);
                    position.Validate();
                    lock (gate)
                    {
                        latestProgress = position;
                        frozen = path.Capture(position, clock());
                        failed = transition;
                    }
                }
                var save = await SaveFrozen();
                if (save.Status != SessionStatus.Completed) return save;
            }
            // No awaits between committing the pure state and logical activation. A decoder error
            // from Activate belongs to the new visit; it must never resurrect the committed visit.
            var old = current;
            string? activationError = null;
            lock (gate)
            {
                bool newSession = path != transition.Destination;
                if (item is null)
                {
                    current = null; path = null; activeToken = null; latestProgress = null;
                }
                else
                {
                    transition.Destination.Activate(item, transition.Origin, clock(), transition.Index);
                    path = transition.Destination;
                    activeToken = targetToken! with { VisitId = path.Pending!.VisitId };
                    current = ready;
                    ready = null;
                    latestProgress = item.MediaType == MediaType.Comic ? PlaybackProgress.Comic(0) : PlaybackProgress.Video(0);
                    try { current!.Activate(activeToken); }
                    catch (Exception ex) { activationError = ex.Message; }
                }
                frozen = null; failed = null; phase = RestingPhase;
                if (newSession) { commands.Clear(); commandOrder.Clear(); Remember(runningCommand, runningTask!); }
            }
            if (old is not null)
            {
                try { await old.DisposeAsync(); }
                catch (Exception ex) { activationError ??= ex.Message; }
            }
            return new(SessionStatus.Completed, activationError);
        }
        finally
        {
            try { if (ready is not null) await ready.DisposeAsync(); }
            finally
            {
                lock (gate)
                {
                    opening?.Dispose(); opening = null; preparationToken = null;
                    phase = frozen is null ? RestingPhase : SessionPhase.SaveFailed;
                }
            }
        }
    }

    private async Task<SessionResult> SaveFrozen()
    {
        var request = frozen!;
        try
        {
            var result = await Task.Run(() => commit(request));
            if (result.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted)
                return new(SessionStatus.Completed);
        }
        catch (Exception) { /* A lost acknowledgement is resolved through the durable marker. */ }
        try
        {
            var check = await Task.Run(() => database.CheckVisitCommit(request));
            return check switch
            {
                VisitCommitCheck.Committed => new(SessionStatus.Completed),
                VisitCommitCheck.NotCommitted => new(SessionStatus.SaveFailed),
                _ => new(SessionStatus.CommitUnknown, "Visit payload mismatch; keep the frozen visit.")
            };
        }
        catch (Exception ex) { return new(SessionStatus.CommitUnknown, ex.Message); }
    }

    public Task<SessionResult> RetryAsync(Guid commandId) => Command(commandId, async () =>
    {
        if (failed is null || frozen is null) return new(SessionStatus.NoOp);
        // Reuse the exact frozen payload, but prepare a fresh candidate because failure released it.
        return await Move(failed);
    }, recovery: true);

    public Task<SessionResult> ResumeAfterSaveFailureAsync(Guid commandId) => Command(commandId, async () =>
    {
        if (frozen is null) return new(SessionStatus.NoOp);
        VisitCommitCheck check;
        try { check = await Task.Run(() => database.CheckVisitCommit(frozen)); }
        catch (Exception ex) { return new(SessionStatus.CommitUnknown, ex.Message); }
        if (check != VisitCommitCheck.NotCommitted)
            return new(SessionStatus.CommitUnknown, "Committed or conflicting visit: retry the original transition.");
        current!.Resume(activeToken!);
        lock (gate) { frozen = null; failed = null; phase = RestingPhase; }
        return new(SessionStatus.Completed);
    }, recovery: true);

    // Save/leave primitive only. No Window.Close, hiding, process exit, or T14 failure UX.
    public Task<SessionResult> LeaveAsync(Guid commandId) => Command(commandId,
        () => path is null ? Task.FromResult(new SessionResult(SessionStatus.NoOp))
            : Move(new(path, null, VisitOrigin.Manual)));

    // T12 reports a confirmed result after its OS/journal protocol. This boundary does not run
    // OS deletion or clean the DB. Unknown quarantines the path without guessing success.
    public Task<SessionResult> ReportDeletionAsync(Guid commandId, string pathKey, DeletionOutcome outcome) =>
        Command(commandId, async () =>
        {
            ISessionMedia? release = null;
            lock (gate)
            {
                if (outcome == DeletionOutcome.Unknown) quarantined.Add(pathKey);
                else if (outcome is DeletionOutcome.Failed or DeletionOutcome.Cancelled) quarantined.Remove(pathKey);
                else
                {
                    quarantined.Add(pathKey); // T12 releases this only after durable DB cleanup.
                    path?.DeleteSucceeded(pathKey);
                    if (path?.Pending is null)
                    {
                        release = current; current = null; activeToken = null; latestProgress = null;
                    }
                    phase = RestingPhase;
                }
            }
            if (release is not null) await release.DisposeAsync();
            return new(SessionStatus.Completed);
        });

    public Task<SessionResult> ReleaseDeletionQuarantineAsync(Guid commandId, string pathKey) =>
        Command(commandId, () =>
        {
            quarantined.Remove(pathKey);
            return Task.FromResult(new SessionResult(SessionStatus.Completed));
        });
}
public enum DeletionOutcome { Succeeded, Failed, Cancelled, Unknown }
