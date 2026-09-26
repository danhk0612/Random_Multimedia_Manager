using RandomMultimediaManager.App.Deletion;
using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.Sessions;

public sealed partial class SessionCoordinator
{
    private MediaItem? releasedItem;
    private object? releasedState;
    public Task<SessionResult> DeleteCurrentAsync(Guid commandId, DeletionService service, DeletionMode mode) =>
        Command(commandId, async () =>
        {
            if (path?.Pending is null || activeToken is null) return new(SessionStatus.NoOp);
            var item = Find(await Task.Run(database.GetSessionSnapshot), path.Pending.ItemId);
            DeletionRecord record;
            try { record = await service.PrepareAsync(item.PathKey, mode); }
            catch (Exception ex) { return new(SessionStatus.Failed, ex.Message); }
            phase = SessionPhase.Deleting;
            FileDeletionResult outcome;
            bool osStarted = false;
            try
            {
                if (current is not null)
                {
                    releasedState = (current as IDeletionMediaState)?.CaptureDeletionState();
                    latestProgress = current.PauseAndCapture(activeToken);
                    releasedItem = item;
                    await ReleaseOwnedAsync(current);
                    current = null;
                }
                osStarted = true;
                outcome = await service.ExecuteAsync(record);
            }
            catch (Exception ex)
            {
                // A failed resource release precedes the OS call; service implementations
                // return Unknown for exceptions after the OS boundary.
                outcome = new(osStarted ? DeletionOutcome.Unknown : DeletionOutcome.Failed, ex.Message);
            }
            if (outcome.Outcome == DeletionOutcome.Succeeded) DeleteSessionPath(item.PathKey);
            var result = await service.RecordResultAsync(record, outcome);
            string? reopenError = null;
            if ((outcome.Outcome is DeletionOutcome.Failed or DeletionOutcome.Cancelled) && result.Resolved)
                reopenError = await ReopenDeletedVisit();
            phase = RestingPhase;
            return new(result.Resolved ? SessionStatus.Completed : SessionStatus.CommitUnknown,
                reopenError ?? result.Error ?? (result.Resolved ? outcome.Outcome switch {
                    DeletionOutcome.Succeeded => "삭제했습니다. 이전/다음으로 이동할 수 있습니다.",
                    DeletionOutcome.Cancelled => "삭제를 취소했습니다. 같은 방문을 유지합니다.",
                    _ => "삭제하지 못했습니다. 같은 방문을 유지합니다." } : "삭제 복구 확인이 필요합니다."));
        });

    private void DeleteSessionPath(string key)
    {
        path?.DeleteSucceeded(key);
        if (path?.Pending is null)
        { current = null; activeToken = null; latestProgress = null; releasedItem = null; releasedState = null; }
    }
    private async Task<string?> ReopenDeletedVisit()
    {
        if (releasedItem is null || path?.Pending is null || activeToken is null) return null;
        if (current is not null) return "미디어 해제가 완료되지 않았습니다.";
        var token = activeToken with { OperationId = Guid.NewGuid(), VisitId = null };
        ISessionMedia? media = null;
        try
        {
            var result = await preparer.PrepareAsync(releasedItem, latestProgress, token, CancellationToken.None);
            media = result.Media;
            if (result.Status != PreparationStatus.Ready || media is null || result.Token != token)
                return result.Error ?? "같은 방문을 다시 열지 못했습니다. 진행과 Pending은 보존됩니다.";
            activeToken = token with { VisitId = path.Pending.VisitId };
            current = media; media = null;
            current.Activate(activeToken);
            (current as IDeletionMediaState)?.RestoreDeletionState(releasedState);
            releasedItem = null; releasedState = null;
            return null;
        }
        catch (Exception ex) { return ex.Message; }
        finally { if (media is not null) await ReleaseOwnedAsync(media); }
    }
    public Task<SessionResult> ResolveDeletionAsync(Guid commandId, DeletionService service,
        DeletionRecovery entry, bool succeeded) => Command(commandId, async () =>
    {
        phase = SessionPhase.Deleting;
        // Durable success must precede cleanup, but in-memory Pending must be cleared even
        // when the DB cleanup fails. A live confirmation can never generate a new visit.
        if (succeeded && entry.Record is { } target && path?.Pending is not null
            && path.Slots[path.Cursor].PathKey == target.PathKey && current is not null)
        {
            // Prepared may survive a lost write acknowledgement before media release. A user
            // confirmation must release that still-owned media too, not merely drop its owner.
            latestProgress = current.PauseAndCapture(activeToken!);
            releasedItem = Find(await Task.Run(database.GetSessionSnapshot), path.Pending.ItemId);
            await ReleaseOwnedAsync(current);
            current = null;
        }
        var result = await service.ConfirmAsync(entry, succeeded);
        if (succeeded && entry.Record is { } r && result.Record?.Phase == DeletionPhase.Succeeded)
            DeleteSessionPath(r.PathKey);
        string? reopenError = null;
        if (!succeeded && result.Resolved) reopenError = await ReopenDeletedVisit();
        phase = RestingPhase;
        return new(result.Resolved ? SessionStatus.Completed : SessionStatus.CommitUnknown, result.Error ?? reopenError);
    });
}

public interface IDeletionMediaState
{
    object? CaptureDeletionState();
    void RestoreDeletionState(object? state);
}
