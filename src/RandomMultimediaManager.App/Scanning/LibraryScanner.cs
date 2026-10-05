using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.Scanning;

public sealed record LibraryScanProgress(string Message, int VisitedEntries, int MediaFiles);
public enum LibraryScanStatus { Completed, Cancelled }
public sealed record LibraryScanResult(LibraryScanStatus Status, int PresentCount, int MissingCount,
    int WarningCount, IReadOnlyList<string> Warnings);

public sealed record SourceScanObservation(IReadOnlyList<MediaItem> Present,
    IReadOnlyList<string> MissingKeys, IReadOnlyList<string> Warnings, bool AccessDenied = false);

public sealed class LibraryScanner
{
    private static readonly HashSet<string> ComicExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".zip", ".cbz" };
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".avi", ".webm", ".mov", ".wmv", ".m4v", ".ts" };

    private readonly LibraryDatabase database;

    public LibraryScanner(LibraryDatabase database) => this.database = database;

    // Compatibility entry point for the scanner harness; production owns one coordinator.
    public async Task<LibraryScanResult> ScanCategoryAsync(Category category,
        IProgress<LibraryScanProgress>? progress, CancellationToken cancellationToken)
    {
        var coordinator = new ScanCoordinator(database);
        try { return await coordinator.ScanCategoryAsync(category.Id, progress, cancellationToken); }
        finally { await coordinator.CloseAsync(); }
    }

    public Task<SourceScanObservation> CollectAsync(Category category, CategorySource source,
        bool local, IProgress<LibraryScanProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Collect(category, source, local, progress, cancellationToken), cancellationToken);

    private SourceScanObservation Collect(Category category, CategorySource source, bool local,
        IProgress<LibraryScanProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var existing = database.GetItems(category.Id);
        var present = new Dictionary<string, MediaItem>(StringComparer.Ordinal);
        var warnings = new List<string>();
        int visited = 0;
        var report = ScanSource(category, source, existing.ToDictionary(x => x.PathKey), present,
            warnings, ref visited, progress, cancellationToken, local);
        var missing = new List<string>();
        if (local)
            foreach (var item in existing)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!present.ContainsKey(item.PathKey) && report.ConfirmsMissing(item)
                    && ConfirmLocalAbsence(item.Path)) missing.Add(item.PathKey);
            }
        cancellationToken.ThrowIfCancellationRequested();
        return new(present.Values.ToArray(), missing, warnings, report.AccessDenied);
    }

    internal static Task<SourceScanObservation> RecheckLocalMissingAsync(SourceScanObservation observation,
        CancellationToken cancellationToken) => Task.Run(() =>
        {
            var confirmed = new List<string>();
            foreach (string key in observation.MissingKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ConfirmLocalAbsence(key)) confirmed.Add(key);
            }
            return observation with { MissingKeys = confirmed };
        }, cancellationToken);

    // A successful remote directory listing is never authoritative absence evidence.
    // Recheck local readiness, supported ancestors and the individual target before applying.
    private static bool ConfirmLocalAbsence(string path)
    {
        try
        {
            if (!TryProbeSourceRoot(Path.GetDirectoryName(path)!, out bool missing, out _, true, out _))
                return missing;
            _ = File.GetAttributes(path);
            return false;
        }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
        catch (Exception ex) when (IsObservationFailure(ex)) { return false; }
    }

    private static SourceReport ScanSource(Category category, CategorySource source,
        IReadOnlyDictionary<string, MediaItem> existingByKey, Dictionary<string, MediaItem> present,
        List<string> warnings, ref int visited, IProgress<LibraryScanProgress>? progress,
        CancellationToken cancellationToken, bool local)
    {
        var report = new SourceReport(source);
        progress?.Report(new LibraryScanProgress($"소스 확인: {source.RootPath}", visited, present.Count));

        if (!TryProbeSourceRoot(source.RootPath, out bool missing, out string? problem, local, out bool accessDenied))
        {
            if (missing)
            {
                report.RootMissingConfirmed = true;
                return report;
            }
            report.AccessDenied |= accessDenied;
            warnings.Add($"{source.RootPath}: {problem}");
            report.BlockedPrefixes.Add(source.RootPathKey);
            return report;
        }

        try
        {
            ValidateDirectoryAndAncestors(source.RootPath, local);
        }
        catch (Exception ex) when (IsObservationFailure(ex))
        {
            report.AccessDenied |= IsAccessDenied(ex);
            warnings.Add($"{source.RootPath}: {Friendly(ex)}");
            report.BlockedPrefixes.Add(source.RootPathKey);
            return report;
        }

        var pending = new Stack<string>();
        pending.Push(source.RootPath);
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            string directoryKey = NormalizeDiscoveredPath(directory).PathKey;
            bool complete = true;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    visited++;
                    progress?.Report(new LibraryScanProgress($"스캔 중: {entry.FullName}", visited, present.Count));
                    var normalized = NormalizeDiscoveredPath(entry.FullName);
                    if (!names.Add(normalized.Path))
                        throw new CaseCollisionException();

                    if (entry is DirectoryInfo childDirectory)
                    {
                        report.ObservedDirectories.Add(normalized.PathKey);
                        if (!source.IncludeSubdirectories) continue;
                        try
                        {
                            FileAttributes attributes = childDirectory.Attributes;
                            if ((attributes & FileAttributes.ReparsePoint) != 0)
                            {
                                report.BlockedPrefixes.Add(normalized.PathKey);
                                warnings.Add($"{normalized.Path}: reparse point 폴더는 스캔하지 않습니다.");
                                continue;
                            }
                            if (WindowsDirectoryRules.IsCaseSensitive(normalized.Path, !local))
                            {
                                report.BlockedPrefixes.Add(normalized.PathKey);
                                warnings.Add($"{normalized.Path}: Windows 대소문자 구분 디렉터리는 지원하지 않습니다.");
                                continue;
                            }
                            pending.Push(normalized.Path);
                        }
                        catch (Exception ex) when (IsObservationFailure(ex))
                        {
                            report.AccessDenied |= IsAccessDenied(ex);
                            report.BlockedPrefixes.Add(normalized.PathKey);
                            warnings.Add($"{normalized.Path}: {Friendly(ex)}");
                        }
                        continue;
                    }

                    report.ObservedFiles.Add(normalized.PathKey);
                    try
                    {
                        FileAttributes attributes = entry.Attributes;
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            warnings.Add($"{normalized.Path}: reparse point 파일은 인덱싱하지 않습니다.");
                            continue;
                        }
                        if (!IsTargetExtension(category.MediaType, normalized.Path)) continue;

                        var info = (FileInfo)entry;
                        long size = info.Length;
                        long lastWrite = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds();
                        Guid id = existingByKey.TryGetValue(normalized.PathKey, out var old) ? old.Id : Guid.NewGuid();
                        present[normalized.PathKey] = new MediaItem(id, category.Id, category.MediaType,
                            normalized.Path, normalized.PathKey, size, lastWrite);
                    }
                    catch (Exception ex) when (IsObservationFailure(ex))
                    {
                        report.AccessDenied |= IsAccessDenied(ex);
                        warnings.Add($"{normalized.Path}: 메타데이터를 읽지 못해 기존 상태를 보존합니다. {Friendly(ex)}");
                    }
                }
            }
            catch (CaseCollisionException)
            {
                present.Clear();
                report.BlockedPrefixes.Add(source.RootPathKey);
                warnings.Add("대소문자 경로 충돌로 소스 전체 관찰을 폐기했습니다.");
                return report;
            }
            catch (DirectoryNotFoundException)
            {
                complete = false;
                report.ConfirmedMissingPrefixes.Add(directoryKey);
                warnings.Add($"{directory}: 열거 중 경로가 사라져 부재를 재확인합니다.");
            }
            catch (FileNotFoundException)
            {
                complete = false;
                report.ConfirmedMissingPrefixes.Add(directoryKey);
                warnings.Add($"{directory}: 열거 중 경로가 사라져 부재를 재확인합니다.");
            }
            catch (Exception ex) when (IsObservationFailure(ex))
            {
                complete = false;
                report.AccessDenied |= IsAccessDenied(ex);
                report.BlockedPrefixes.Add(directoryKey);
                warnings.Add($"{directory}: 일부를 관찰하지 못해 해당 범위의 기존 상태를 보존합니다. {Friendly(ex)}");
            }

            if (complete) report.CompletedDirectories.Add(directoryKey);
        }

        return report;
    }

    public static bool IsTargetExtension(MediaType mediaType, string path)
    {
        string extension = Path.GetExtension(path);
        return mediaType switch
        {
            MediaType.Comic => ComicExtensions.Contains(extension),
            MediaType.Video => VideoExtensions.Contains(extension),
            _ => false
        };
    }

    private static (string Path, string PathKey) NormalizeDiscoveredPath(string path)
    {
        var normalized = WindowsPath.Normalize(path);
        return (normalized.Path, normalized.PathKey);
    }

    private static bool TryProbeSourceRoot(string path, out bool missing, out string? problem, bool local, out bool accessDenied)
    {
        missing = false; problem = null; accessDenied = false;
        try
        {
            var normalized = WindowsPath.Normalize(path);
            if (local && !new DriveInfo(normalized.Root).IsReady)
            { problem = "드라이브가 준비되지 않아 상태를 변경하지 않습니다."; return false; }
            // Stop at the UNC share, never query the server or an ancestor outside that share.
            var ancestors = new Stack<string>();
            string current = normalized.Path;
            while (true)
            {
                ancestors.Push(current);
                if (WindowsPath.Normalize(current).PathKey == normalized.RootKey) break;
                current = Path.GetDirectoryName(current)!;
            }
            foreach (string directory in ancestors)
            {
                FileAttributes attributes = File.GetAttributes(directory);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("reparse point 경로는 지원하지 않습니다.");
                if ((attributes & FileAttributes.Directory) == 0)
                    throw new IOException("소스 상위 경로가 폴더가 아닙니다.");
                if (WindowsDirectoryRules.IsCaseSensitive(directory, !local))
                    throw new IOException("대소문자 구분 디렉터리는 지원하지 않습니다.");
            }
            return true;
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException)
        { missing = local; problem = "경로를 확인할 수 없습니다."; return false; }
        catch (Exception ex) when (IsObservationFailure(ex))
        { accessDenied = IsAccessDenied(ex); problem = Friendly(ex); return false; }
    }

    private static void ValidateDirectoryAndAncestors(string path, bool local)
    {
        if (!TryProbeSourceRoot(path, out _, out var problem, local, out _))
            throw new IOException(problem);
    }

    private static bool IsObservationFailure(Exception ex) =>
        ex is UnauthorizedAccessException or IOException or Security.SecurityException or ArgumentException
        or Win32Exception;

    private static bool IsAccessDenied(Exception ex) => ex is UnauthorizedAccessException
        or Security.SecurityException or Win32Exception { NativeErrorCode: 5 };

    private static string Friendly(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "접근이 거부되어 상태를 변경하지 않습니다.",
        _ => ex.Message
    };

    private sealed class CaseCollisionException : Exception { }

    private sealed class SourceReport
    {
        public SourceReport(CategorySource source) => Source = source;
        public CategorySource Source { get; }
        public bool AccessDenied { get; set; }
        public bool RootMissingConfirmed { get; set; }
        public HashSet<string> ObservedFiles { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ObservedDirectories { get; } = new(StringComparer.Ordinal);
        public HashSet<string> CompletedDirectories { get; } = new(StringComparer.Ordinal);
        public HashSet<string> BlockedPrefixes { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ConfirmedMissingPrefixes { get; } = new(StringComparer.Ordinal);

        public bool ConfirmsMissing(MediaItem item)
        {
            if (!Covers(item.PathKey)) return false;
            if (ObservedFiles.Contains(item.PathKey)) return false;
            if (BlockedPrefixes.Any(prefix => IsSameOrDescendant(item.PathKey, prefix))) return false;
            if (RootMissingConfirmed) return true;
            if (ConfirmedMissingPrefixes.Any(prefix => IsSameOrDescendant(item.PathKey, prefix))) return true;

            string? parent = Path.GetDirectoryName(item.PathKey)?.TrimEnd('\\');
            if (parent is null) return false;
            if (CompletedDirectories.Contains(parent)) return true;
            if (!Source.IncludeSubdirectories) return false;

            var chain = new List<string>();
            string? current = parent;
            while (current is not null && IsSameOrDescendant(current, Source.RootPathKey))
            {
                chain.Add(current.TrimEnd('\\'));
                if (string.Equals(current.TrimEnd('\\'), Source.RootPathKey.TrimEnd('\\'), StringComparison.Ordinal)) break;
                current = Path.GetDirectoryName(current)?.TrimEnd('\\');
            }
            chain.Reverse();
            for (int i = 0; i + 1 < chain.Count; i++)
                if (CompletedDirectories.Contains(chain[i]) && !ObservedDirectories.Contains(chain[i + 1]))
                    return true;
            return false;
        }

        private bool Covers(string fileKey)
        {
            string? parent = Path.GetDirectoryName(fileKey)?.TrimEnd('\\');
            if (parent is null) return false;
            string root = Source.RootPathKey.TrimEnd('\\');
            if (!Source.IncludeSubdirectories) return string.Equals(parent, root, StringComparison.Ordinal);
            return IsSameOrDescendant(parent, root);
        }

        private static bool IsSameOrDescendant(string path, string prefix)
        {
            string cleanPrefix = prefix.TrimEnd('\\');
            return string.Equals(path.TrimEnd('\\'), cleanPrefix, StringComparison.Ordinal)
                || path.StartsWith(cleanPrefix + "\\", StringComparison.Ordinal);
        }
    }
}

internal static class WindowsDirectoryRules
{
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const int FileCaseSensitiveInfo = 23;
    private const uint CaseSensitiveFlag = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileCaseSensitiveInformation { public uint Flags; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess,
        uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle hFile, int fileInformationClass,
        out FileCaseSensitiveInformation fileInformation, uint dwBufferSize);

    public static bool IsCaseSensitive(string path, bool allowUnsupported = false)
    {
        if (!OperatingSystem.IsWindows()) return false;
        using SafeFileHandle handle = CreateFileW(path, 0, FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!GetFileInformationByHandleEx(handle, FileCaseSensitiveInfo, out var info,
            (uint)Marshal.SizeOf<FileCaseSensitiveInformation>()))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 87 || (allowUnsupported && error is 1 or 50)) return false;
            throw new Win32Exception(error);
        }
        return (info.Flags & CaseSensitiveFlag) != 0;
    }
}
