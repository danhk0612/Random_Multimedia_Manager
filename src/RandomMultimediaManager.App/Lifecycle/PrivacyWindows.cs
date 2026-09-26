using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RandomMultimediaManager.App.Lifecycle;

// UI-thread HWND visibility only: Window.Hide would terminate a WPF ShowDialog loop.
// Retaining HWND style/state also retains fullscreen, minimized state, and disabled modal owners.
public sealed class PrivacyWindows : IDisposable
{
    private readonly Hook callback;
    private readonly Subclass subclass;
    private readonly IntPtr hook;
    private readonly HashSet<IntPtr> attached = [];
    private readonly HashSet<IntPtr> exempt = [];
    private readonly List<IntPtr> restore = [];
    private bool hidden, disposed;
    public bool Hidden => hidden;
    public bool AllowTrayWindowCreation { get; set; }
    public PrivacyWindows()
    {
        callback = OnHook; subclass = OnMessage;
        hook = SetWindowsHookEx(5, callback, IntPtr.Zero, GetCurrentThreadId());
        if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        EnumThreadWindows(GetCurrentThreadId(), (hwnd, _) => { Attach(hwnd); return true; }, IntPtr.Zero);
    }
    public void Exempt(IntPtr hwnd) => exempt.Add(hwnd);
    private void Attach(IntPtr hwnd)
    {
        if (attached.Contains(hwnd) || exempt.Contains(hwnd)) return;
        if (!SetWindowSubclass(hwnd, subclass, 16, 0))
            throw new InvalidOperationException("앱 창 숨김 연결에 실패했습니다.");
        attached.Add(hwnd);
    }
    private IntPtr OnHook(int code, IntPtr hwnd, IntPtr data)
    {
        if (code == 3)
        {
            var creation = Marshal.PtrToStructure<CreateWindow>(Marshal.ReadIntPtr(data));
            if ((creation.Style & 0x40000000) == 0 && creation.Parent != new IntPtr(-3))
            {
                if (AllowTrayWindowCreation) exempt.Add(hwnd);
                else
                {
                    // No exception may cross an unmanaged callback. Reject creation if the
                    // privacy guard cannot be attached, rather than expose a new private window.
                    try { Attach(hwnd); }
                    catch { return (IntPtr)1; }
                    if (hidden && (creation.Style & 0x10000000) != 0)
                    {
                        Remember(hwnd);
                        creation.Style &= ~0x10000000;
                        Marshal.StructureToPtr(creation, Marshal.ReadIntPtr(data), false);
                    }
                }
            }
        }
        if (code == 5 && hidden && attached.Contains(hwnd) && !exempt.Contains(hwnd)) return (IntPtr)1;
        return CallNextHookEx(hook, code, hwnd, data);
    }
    private void Remember(IntPtr hwnd) { if (!restore.Contains(hwnd)) restore.Add(hwnd); }
    private IntPtr OnMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data)
    {
        if (message == 0x46 && hidden && !exempt.Contains(hwnd)) // WM_WINDOWPOSCHANGING, before first paint
        {
            var position = Marshal.PtrToStructure<WindowPosition>(lParam);
            if ((position.Flags & 0x40) != 0)
            {
                Remember(hwnd);
                position.Flags = (position.Flags & ~0x40u) | 0x80u | 0x10u;
                Marshal.StructureToPtr(position, lParam, false);
            }
        }
        if (message == 0x82)
        {
            attached.Remove(hwnd); exempt.Remove(hwnd); restore.Remove(hwnd);
            RemoveWindowSubclass(hwnd, subclass, 16);
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }
    public void Hide()
    {
        if (hidden) return;
        // Capture every visible surface before hiding any owner (which can hide owned windows).
        EnumThreadWindows(GetCurrentThreadId(), (hwnd, _) =>
        {
            if (!exempt.Contains(hwnd)) { Attach(hwnd); if (IsWindowVisible(hwnd)) Remember(hwnd); }
            return true;
        }, IntPtr.Zero);
        hidden = true;
        foreach (var hwnd in restore.ToArray()) SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x97);
    }
    public void Restore()
    {
        if (!hidden) return;
        hidden = false;
        foreach (var hwnd in restore.AsEnumerable().Reverse().ToArray())
            if (IsWindow(hwnd)) SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x57);
        var foreground = restore.FirstOrDefault(hwnd => IsWindow(hwnd) && IsWindowEnabled(hwnd));
        restore.Clear();
        if (foreground != IntPtr.Zero) SetForegroundWindow(foreground);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        UnhookWindowsHookEx(hook);
        foreach (var hwnd in attached.ToArray()) RemoveWindowSubclass(hwnd, subclass, 16);
        attached.Clear(); restore.Clear(); exempt.Clear();
    }
    [StructLayout(LayoutKind.Sequential)] private struct CreateWindow
    {
        public IntPtr Parameters, Instance, Menu, Parent;
        public int Height, Width, Y, X, Style;
        public IntPtr Name, Class;
        public uint ExtendedStyle;
    }
    [StructLayout(LayoutKind.Sequential)] private struct WindowPosition
    { public IntPtr Window, After; public int X, Y, Width, Height; public uint Flags; }
    private delegate IntPtr Hook(int code, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr Subclass(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, nuint id, nuint data);
    private delegate bool EnumWindow(IntPtr hwnd, IntPtr data);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int kind, Hook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, EnumWindow callback, IntPtr data);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr hwnd, Subclass callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr hwnd, Subclass callback, nuint id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
