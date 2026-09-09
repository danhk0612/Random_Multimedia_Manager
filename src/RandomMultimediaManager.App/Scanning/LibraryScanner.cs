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

public sealed class LibraryScanner
{
    private static readonly HashSet<string> ComicExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".zip", ".cbz" };
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".avi", ".webm", ".mov", ".wmv", ".m4v", ".ts" };

    private readonly LibraryDatabase database;

    public LibraryScanner(LibraryDatabase database) => this.database = database;

    public Task<LibraryScanResult> ScanCategoryAsync(Category category,
        IProgress<LibraryScanProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => ScanCategory(category, progress, cancellationToken), cancellationToken);

    private LibraryScanResult ScanCategory(Category category, IProgress<LibraryScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = database.GetItems(category.Id);
            var existingByKey = existing.ToDictionary(x => x.PathKey, StringComparer.Ordinal);
            var activeSources = database.GetSources(category.Id).Where(x => x.IsEnabled).ToArray();
            if (activeSources.Length == 0)
                return new LibraryScanResult(LibraryScanStatus.Completed, 0, 0, 0, Array.Empty<string>());

            var present = new Dictionary<string, MediaItem>(StringComparer.Ordinal);
            var reports = new List<SourceReport>();
            var warnings = new List<string>();
            int visited = 0;

            foreach (var source in activeSources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var report = ScanSource(category, source, existingByKey, present, warnings,
                    ref visited, progress, cancellationToken);
                reports.Add(report);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var confirmedMissingKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in existing)
            {
                if (present.ContainsKey(item.PathKey)) continue;
                if (reports.Any(report => report.ConfirmsMissing(item)))
                    confirmedMissingKeys.Add(item.PathKey);
            }

            var allItemsByKey = database.GetCategories()
                .SelectMany(x => database.GetItems(x.Id))
                .GroupBy(x => x.PathKey, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);
            var confirmedMissingIds = confirmedMissingKeys
                .Where(allItemsByKey.ContainsKey)
                .SelectMany(key => allItemsByKey[key])
                .Select(x => x.Id)
                .Distinct()
                .ToArray();

            cancellationToken.ThrowIfCancellationRequested();
            database.ApplyObservedItems(present.Values.ToArray(), confirmedMissingIds);
            progress?.Report(new LibraryScanProgress(
                $"스캔 완료: {present.Count}개 반영, {confirmedMissingIds.Length}개 Missing", visited, present.Count));
            return new LibraryScanResult(LibraryScanStatus.Completed, present.Count,
                confirmedMissingIds.Length, warnings.Count, warnings);
        }
        catch (OperationCanceledException)
        {
            return new LibraryScanResult(LibraryScanStatus.Cancelled, 0, 0, 0, Array.Empty<string>());
        }
    }

    private static SourceReport ScanSource(Category category, CategorySource source,
        IReadOnlyDictionary<string, MediaItem> existingByKey, Dictionary<string, MediaItem> present,
        List<string> warnings, ref int visited, IProgress<LibraryScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var report = new SourceReport(source);
        progress?.Report(new LibraryScanProgress($"소스 확인: {source.RootPath}", visited, present.Count));

        if (!TryProbeSourceRoot(source.RootPath, out bool missing, out string? problem))
        {
            if (missing)
            {
                report.RootMissingConfirmed = true;
                return report;
            }
            warnings.Add($"{source.RootPath}: {problem}");
            report.BlockedPrefixes.Add(source.RootPathKey);
            return report;
        }

        try
        {
            ValidateDirectoryAndAncestors(source.RootPath);
        }
        catch (Exception ex) when (IsObservationFailure(ex))
        {
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
            try
            {
                foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    visited++;
                    progress?.Report(new LibraryScanProgress($"스캔 중: {entry.FullName}", visited, present.Count));
                    var normalized = NormalizeDiscoveredPath(entry.FullName);

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
                            if (WindowsDirectoryRules.IsCaseSensitive(normalized.Path))
                            {
                                report.BlockedPrefixes.Add(normalized.PathKey);
                                warnings.Add($"{normalized.Path}: Windows 대소문자 구분 디렉터리는 지원하지 않습니다.");
                                continue;
                            }
                            pending.Push(normalized.Path);
                        }
                        catch (Exception ex) when (IsObservationFailure(ex))
                        {
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
                        warnings.Add($"{normalized.Path}: 메타데이터를 읽지 못해 기존 상태를 보존합니다. {Friendly(ex)}");
                    }
                }
            }
            catch (DirectoryNotFoundException)
            {
                complete = false;
                report.ConfirmedMissingPrefixes.Add(directoryKey);
            }
            catch (FileNotFoundException)
            {
                complete = false;
                report.ConfirmedMissingPrefixes.Add(directoryKey);
            }
            catch (Exception ex) when (IsObservationFailure(ex))
            {
                complete = false;
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
        string normalized = Path.GetFullPath(path).Replace('/', '\\');
        string? root = Path.GetPathRoot(normalized);
        if (root is null || root.Length != 3 || !char.IsAsciiLetter(root[0]) || root[1] != ':' || root[2] != '\\')
            throw new ArgumentException("로컬 드라이브 절대 경로가 아닙니다.");
        if (normalized.AsSpan(2).Contains(':')) throw new ArgumentException("대체 데이터 스트림 경로는 지원하지 않습니다.");
        foreach (string component in normalized[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
            if (component.EndsWith(' ') || component.EndsWith('.'))
                throw new ArgumentException("경로 구성요소 끝 공백/마침표는 지원하지 않습니다.");
        if (normalized.Length > root.Length) normalized = normalized.TrimEnd('\\');
        return (normalized, normalized.ToUpperInvariant());
    }

    private static bool TryProbeSourceRoot(string path, out bool missing, out string? problem)
    {
        missing = false;
        problem = null;
        try
        {
            string root = Path.GetPathRoot(path)!;
            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                problem = "드라이브가 준비되지 않아 상태를 변경하지 않습니다.";
                return false;
            }
            // Check existing ancestors from the drive down before a missing descendant
            // can be evidence of absence. Unsupported ancestors must preserve stored state.
            var ancestors = new Stack<DirectoryInfo>();
            for (DirectoryInfo? directory = new(Path.GetFullPath(path)); directory is not null; directory = directory.Parent)
                ancestors.Push(directory);
            foreach (DirectoryInfo directory in ancestors)
            {
                FileAttributes ancestorAttributes = File.GetAttributes(directory.FullName);
                if ((ancestorAttributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("reparse point 경로는 지원하지 않습니다.");
                if ((ancestorAttributes & FileAttributes.Directory) == 0)
                    throw new IOException("소스 상위 경로가 폴더가 아닙니다.");
                if (WindowsDirectoryRules.IsCaseSensitive(directory.FullName))
                    throw new IOException("Windows 대소문자 구분 디렉터리는 지원하지 않습니다.");
            }
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) == 0)
            {
                problem = "소스 경로가 폴더가 아닙니다.";
                return false;
            }
            return true;
        }
        catch (DirectoryNotFoundException) { missing = true; return false; }
        catch (FileNotFoundException) { missing = true; return false; }
        catch (Exception ex) when (IsObservationFailure(ex))
        {
            problem = Friendly(ex);
            return false;
        }
    }

    private static void ValidateDirectoryAndAncestors(string path)
    {
        DirectoryInfo? directory = new(Path.GetFullPath(path));
        while (directory is not null)
        {
            FileAttributes attributes = directory.Attributes;
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("reparse point 경로는 지원하지 않습니다.");
            if (WindowsDirectoryRules.IsCaseSensitive(directory.FullName))
                throw new IOException("Windows 대소문자 구분 디렉터리는 지원하지 않습니다.");
            directory = directory.Parent;
        }
    }

    private static bool IsObservationFailure(Exception ex) =>
        ex is UnauthorizedAccessException or IOException or Security.SecurityException or ArgumentException
        or Win32Exception;

    private static string Friendly(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "접근이 거부되어 상태를 변경하지 않습니다.",
        _ => ex.Message
    };

    private sealed class SourceReport
    {
        public SourceReport(CategorySource source) => Source = source;
        public CategorySource Source { get; }
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

    public static bool IsCaseSensitive(string path)
    {
        if (!OperatingSystem.IsWindows()) return false;
        using SafeFileHandle handle = CreateFileW(path, 0, FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!GetFileInformationByHandleEx(handle, FileCaseSensitiveInfo, out var info,
            (uint)Marshal.SizeOf<FileCaseSensitiveInformation>()))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 87) return false;
            throw new Win32Exception(error);
        }
        return (info.Flags & CaseSensitiveFlag) != 0;
    }
}
