using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace RandomMultimediaManager.App.Lifecycle;

// Shell acknowledgement is required before hiding the last restore surface.
public sealed class TrayIcon : IDisposable
{
    private const int Callback = 0x8001;
    private readonly HwndSource source;
    private readonly uint taskbarCreated;
    private readonly Action restore;
    private readonly Action unavailable;
    private readonly ContextMenu menu;
    private NotifyData data;
    private bool disposed;
    public bool Available { get; private set; }

    public TrayIcon(Action restore, Action exit, Action unavailable)
    {
        this.restore = restore;
        this.unavailable = unavailable;
        source = new HwndSource(new HwndSourceParameters("Random Multimedia Manager tray")
            { Width = 0, Height = 0, WindowStyle = 0 });
        source.AddHook(Message);
        taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        data = new NotifyData { Size = (uint)Marshal.SizeOf<NotifyData>(), Window = source.Handle,
            Id = 1, Flags = 1 | 2 | 4, CallbackMessage = Callback,
            Icon = LoadIcon(IntPtr.Zero, (IntPtr)32512), Tip = "Random Multimedia Manager",
            Info = "", InfoTitle = "" };
        menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        var open = new MenuItem { Header = "열기/복원" };
        open.Click += (_, _) => restore();
        menu.Items.Add(open);
        menu.Items.Add(new MenuItem { Header = "모두 숨기기/복원 (미구현)", IsEnabled = false });
        menu.Items.Add(new Separator());
        var quit = new MenuItem { Header = "종료" };
        quit.Click += (_, _) => exit();
        menu.Items.Add(quit);
        menu.Closed += (_, _) => { if (!disposed) Shell_NotifyIcon(3, ref data); };
        EnsureAvailable();
    }

    public bool EnsureAvailable()
    {
        if (disposed) return false;
        if (Available && Shell_NotifyIcon(1, ref data)) return true;
        Available = data.Icon != IntPtr.Zero && Shell_NotifyIcon(0, ref data);
        if (Available) { data.Version = 4; Shell_NotifyIcon(4, ref data); }
        return Available;
    }
    private IntPtr Message(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message == taskbarCreated)
        {
            Available = false;
            if (!EnsureAvailable()) unavailable();
        }
        else if (message == Callback)
        {
            int notification = (int)(lParam.ToInt64() & 0xffff);
            if (notification is 0x203 or 0x401) restore(); // double-click / keyboard activation
            else if (notification is 0x7b or 0x205)
            {
                SetForegroundWindow(source.Handle);
                menu.IsOpen = true;
            }
            handled = true;
        }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if (disposed) return;
        menu.IsOpen = false;
        Shell_NotifyIcon(2, ref data);
        disposed = true; Available = false;
        source.RemoveHook(Message);
        source.Dispose();
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
