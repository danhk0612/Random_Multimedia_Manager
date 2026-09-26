using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace RandomMultimediaManager.App.Lifecycle;

public sealed class GlobalHotKeys : IDisposable
{
    private readonly HwndSource source;
    private readonly Action hide, exit;
    private bool disposed;
    public bool HideRegistered { get; }
    public bool ExitRegistered { get; }
    public GlobalHotKeys(Action hide, Action exit)
    {
        this.hide = hide; this.exit = exit;
        source = new(new HwndSourceParameters("RMM global keys") { ParentWindow = new IntPtr(-3) });
        source.AddHook(Message);
        HideRegistered = RegisterHotKey(source.Handle, 1, 0x4006, 0x48);
        ExitRegistered = RegisterHotKey(source.Handle, 2, 0x4006, 0x51);
    }
    private IntPtr Message(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x312 && !disposed)
        {
            if (wParam == (IntPtr)1 && HideRegistered) hide();
            if (wParam == (IntPtr)2 && ExitRegistered) exit();
            handled = true;
        }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if (disposed) return;
        if (HideRegistered) UnregisterHotKey(source.Handle, 1);
        if (ExitRegistered) UnregisterHotKey(source.Handle, 2);
        disposed = true;
        source.RemoveHook(Message); source.Dispose();
    }
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
