using System.Runtime.InteropServices;

namespace RandomMultimediaManager.App.Video;

internal static class WindowsAudioEndpoint
{
    // Probe only on preparation, on the same worker that creates the native resources.
    // E_NOTFOUND is absence; unrelated COM failures must not silently disable sound.
    internal static bool IsAvailable()
    {
        object enumerator = Activator.CreateInstance(Type.GetTypeFromCLSID(
            new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), throwOnError: true)!)!;
        IntPtr endpoint = IntPtr.Zero;
        try
        {
            int result = ((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0, 1, out endpoint);
            if (result == unchecked((int)0x80070490)) return false;
            Marshal.ThrowExceptionForHR(result);
            return true;
        }
        finally
        {
            if (endpoint != IntPtr.Zero) Marshal.Release(endpoint);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IntPtr endpoint);
    }
}
