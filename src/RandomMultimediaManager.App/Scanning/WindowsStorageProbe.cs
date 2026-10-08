using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.Scanning;

// Connection evidence only. Never enumerates a source or opens media content.
public static class WindowsStorageProbe
{
    public static Task<StorageObservation> ObserveAsync(string path, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = WindowsPath.Normalize(path);
            if (root.Root.StartsWith(@"\\"))
                return StorageObservation.Classify(path, MappingLookup.Failed);
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            string drive = root.Root[..2];
            int length = 512;
            var target = new StringBuilder(length);
            int error = WNetGetConnectionW(drive, target, ref length);
            if (error == 234 && length <= 32768)
            { target = new StringBuilder(length); error = WNetGetConnectionW(drive, target, ref length); }
            cancellationToken.ThrowIfCancellationRequested();
            if (error == 0)
                return StorageObservation.Classify(path, MappingLookup.Mapped, target.ToString());
            if (error != 2250) // NOT_CONNECTED is only one part of local evidence.
                return StorageObservation.Classify(path, MappingLookup.Failed);
            var device = new StringBuilder(32768);
            if (QueryDosDeviceW(drive, device, device.Capacity) == 0)
                return StorageObservation.Classify(path, MappingLookup.NotMapped);
            string name = device.ToString();
            const string diskPrefix = @"\Device\HarddiskVolume";
            bool disk = name.StartsWith(diskPrefix, StringComparison.Ordinal)
                && name.Length > diskPrefix.Length && name[diskPrefix.Length..].All(char.IsAsciiDigit);
            uint type = GetDriveTypeW(root.Root);
            if (!disk || type is not (2 or 3))
                return StorageObservation.Classify(path, MappingLookup.NotMapped, provider: name);
            // Do not issue this potentially blocking call for a mapped/unknown provider.
            if (!GetVolumeInformationW(root.Root, null, 0, out uint serial, out _, out _, null, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            cancellationToken.ThrowIfCancellationRequested();
            return StorageObservation.Classify(path, MappingLookup.NotMapped,
                localDriveType: true, device: name, volume: serial.ToString("X8"));
        }, cancellationToken);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetGetConnectionW(string local, StringBuilder remote, ref int length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDeviceW(string name, StringBuilder target, int length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetDriveTypeW(string root);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformationW(string root, StringBuilder? name, int nameSize,
        out uint serial, out uint maxComponent, out uint flags, StringBuilder? fileSystem, int fileSystemSize);
}
