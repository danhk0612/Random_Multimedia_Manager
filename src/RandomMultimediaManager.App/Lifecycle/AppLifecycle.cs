using System.Windows;
using System.Windows.Threading;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Deletion;

namespace RandomMultimediaManager.App.Lifecycle;

public enum LifecycleState { Visible, Hidden, Closing, ExitBlocked, Exited }

// One dispatcher-owned path for tray, main X, restore, and ordinary graceful exit.
public sealed class AppLifecycle(MainWindow main, LibraryDatabase database, DeletionService deletions,
    Func<bool> ensureTray, Action removeTray, Action shutdown)
{
    private Task<bool>? exiting;
    public LifecycleState State { get; private set; } = LifecycleState.Visible;
    public string? Error { get; private set; }
    private PrivacyWindows? privacy;
    public bool IsPrivacyHidden => privacy?.Hidden == true;
    public bool BlocksNewCommands => IsPrivacyHidden || State is LifecycleState.Closing or LifecycleState.Exited;
    public PrivacyWindows Privacy => privacy ??= new();
    public void ToggleHidden()
    {
        main.Dispatcher.VerifyAccess();
        if (State == LifecycleState.Exited) return;
        if (IsPrivacyHidden) Restore(); else HideAll();
    }
    public void HideAll()
    {
        main.Dispatcher.VerifyAccess();
        if (State == LifecycleState.Exited || IsPrivacyHidden) return;
        if (!ensureTray())
        {
            main.ShowLifecycleError("트레이를 사용할 수 없어 모두 숨기기를 실행하지 않았습니다.");
            return;
        }
        HidePrivateSurfaces();
        if (State == LifecycleState.Visible) State = LifecycleState.Hidden;
    }
    private void HidePrivateSurfaces()
    {
        // Create the window guard before touching audio; failure cannot leave a half-hidden mode.
        var windows = Privacy;
        Video.PreparedVideo.SetPrivacyMuted(true);
        windows.Hide();
    }
    public void TrayUnavailable()
    {
        // A shell restart must not expose private windows. The keys remain available.
        main.ShowLifecycleError("트레이 재등록 실패. 전역 복원 키로 복원할 수 있습니다.");
        if (!IsPrivacyHidden) Restore();
    }
    public void DisposePrivacy() { privacy?.Dispose(); privacy = null; Video.PreparedVideo.SetPrivacyMuted(false); }

    public void HideMain()
    {
        main.Dispatcher.VerifyAccess();
        if (State is LifecycleState.Closing or LifecycleState.Exited) return;
        if (!ensureTray())
        {
            Restore();
            main.ShowLifecycleError("트레이를 사용할 수 없어 창을 숨기지 않았습니다. 종료 버튼으로 정상 종료할 수 있습니다.");
            return;
        }
        main.Hide();
        if (State != LifecycleState.ExitBlocked) State = LifecycleState.Hidden;
    }
    public void Restore()
    {
        main.Dispatcher.VerifyAccess();
        if (State == LifecycleState.Exited) return;
        if (IsPrivacyHidden)
        {
            Privacy.Restore();
            Video.PreparedVideo.SetPrivacyMuted(false);
            if (State == LifecycleState.Hidden) State = LifecycleState.Visible;
            return;
        }
        main.Show();
        if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
        main.Activate();
        // A modal viewing/recovery window must remain accessible above its disabled owner.
        foreach (Window owned in main.OwnedWindows)
        {
            if (owned.WindowState == WindowState.Minimized) owned.WindowState = WindowState.Normal;
            if (owned.IsVisible) owned.Activate();
        }
        if (State is LifecycleState.Hidden or LifecycleState.Visible) State = LifecycleState.Visible;
    }
    public Task<bool> ExitAsync()
    {
        main.Dispatcher.VerifyAccess();
        if (State == LifecycleState.Exited) return exiting ?? Task.FromResult(true);
        if (exiting is { IsCompleted: false }) return exiting;
        try { HidePrivateSurfaces(); }
        catch (Exception ex) { Error = ex.Message; main.ShowLifecycleError(ex.Message); return Task.FromResult(false); }
        State = LifecycleState.Closing; Error = null;
        main.SetExitRequested(true);
        return exiting = ExitCoreAsync();
    }
    private async Task<bool> ExitCoreAsync()
    {
        // Always unwind Closing/menu callbacks, including an empty synchronous session.
        await Dispatcher.Yield(DispatcherPriority.Background);
        try
        {
            var viewingClose = main.Viewing?.RequestCloseAsync();
            await main.StopScanningAsync();
            if (viewingClose is not null && !await viewingClose)
                throw new InvalidOperationException(main.Viewing?.CloseError ?? "감상 창의 저장·삭제 복구를 확인하세요.");
            if (deletions.GloballyBlocked || deletions.HasIncompleteSuccess)
                throw new InvalidOperationException("삭제 복구 확인/재시도로 성공한 삭제의 DB·저널 정리를 완료하세요.");
            await main.CloseAuxiliaryWindowsAsync();
            database.Dispose();
            removeTray();
            DisposePrivacy();
            State = LifecycleState.Exited;
            shutdown();
            return true;
        }
        catch (Exception ex)
        {
            State = LifecycleState.ExitBlocked; Error = ex.Message;
            main.SetExitRequested(false);
            main.ShowLifecycleError("종료하지 않았습니다. " + ex.Message);
            // Keep privacy until an explicit key/tray restore, including save/release failures.
            return false;
        }
    }
}
