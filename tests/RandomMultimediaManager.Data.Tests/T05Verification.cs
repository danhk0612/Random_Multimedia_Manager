using System.Diagnostics;
using System.Runtime.CompilerServices;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Scanning;
using RandomMultimediaManager.App.ViewModels;
using RandomMultimediaManager.Core;

internal static class T05Verification
{
    [ModuleInitializer]
    internal static void RunAll()
    {
        if (!OperatingSystem.IsWindows()) return;
        Run("T05 extensions, recursion, nested source dedupe and rescan", VerifyFilteringAndDedupe);
        Run("T05 Missing, cross-category path state and disabled/removed sources", VerifyMissingAndSourceState);
        Run("T05 move, reappearance and content replacement", VerifyMoveReappearanceAndReplacement);
        Run("T05 cancellation preserves database state", VerifyCancellation);
        Run("T05 access failure and partial success preserve blocked scope", VerifyAccessFailureAndPartialSuccess);
        Run("T05 reparse paths are not followed", VerifyReparsePoint);
        Run("T05 case-sensitive directories are rejected", VerifyCaseSensitiveDirectory);
    }

    private static void Run(string name, Action<string, string> test)
    {
        string directory = Path.Combine(Path.GetTempPath(), "rmm-t05-" + Guid.NewGuid());
        string media = Path.Combine(directory, "media");
        Directory.CreateDirectory(media);
        try
        {
            test(Path.Combine(directory, "library.db"), media);
            Console.WriteLine("PASS: " + name);
        }
        finally
        {
            TryRun("icacls", media, "/remove:d", Environment.UserName, "/T", "/C");
            TryRun("fsutil", "file", "SetCaseSensitiveInfo", media, "disable");
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static void VerifyFilteringAndDedupe(string dbPath, string media)
    {
        string child = Directory.CreateDirectory(Path.Combine(media, "child")).FullName;
        File.WriteAllText(Path.Combine(media, "ROOT.MP4"), "root");
        File.WriteAllText(Path.Combine(media, "ignored.mp4.tmp"), "ignored");
        File.WriteAllText(Path.Combine(media, "movie.srt"), "subtitle");
        File.WriteAllText(Path.Combine(media, "noextension"), "ignored");
        File.WriteAllText(Path.Combine(child, "nested.mkv"), "nested");
        File.WriteAllText(Path.Combine(media, "book.ZIP"), "zip placeholder");
        File.WriteAllText(Path.Combine(media, "book.cbz"), "cbz placeholder");

        using var db = LibraryDatabase.Open(dbPath);
        var video = SaveCategory(db, "Video", MediaType.Video);
        AddSource(db, video, media, true, true);
        AddSource(db, video, child, true, true);
        var scanner = new LibraryScanner(db);
        var first = Scan(scanner, video);
        Check(first.Status == LibraryScanStatus.Completed);
        var firstItems = db.GetItems(video.Id);
        Check(firstItems.Count == 2, "Only MP4/MKV video targets should be indexed once.");
        Check(firstItems.Any(x => x.Path.EndsWith("ROOT.MP4", StringComparison.Ordinal)));
        Check(firstItems.Any(x => x.Path.EndsWith("nested.mkv", StringComparison.Ordinal)));
        var ids = firstItems.ToDictionary(x => x.PathKey, x => x.Id);
        Scan(scanner, video);
        Check(db.GetItems(video.Id).All(x => ids[x.PathKey] == x.Id), "Rescan must preserve ItemId.");

        var direct = SaveCategory(db, "Direct", MediaType.Video);
        AddSource(db, direct, media, false, true);
        Scan(scanner, direct);
        Check(db.GetItems(direct.Id).Count == 1 && db.GetItems(direct.Id).Single().Path.EndsWith("ROOT.MP4"));

        var comic = SaveCategory(db, "Comic", MediaType.Comic);
        AddSource(db, comic, media, true, true);
        Scan(scanner, comic);
        Check(db.GetItems(comic.Id).Count == 2);
        Check(db.GetItems(comic.Id).All(x => x.Path.EndsWith(".ZIP", StringComparison.Ordinal)
            || x.Path.EndsWith(".cbz", StringComparison.Ordinal)));

        Check(LibraryScanner.IsTargetExtension(MediaType.Video, "x.MP4"));
        Check(!LibraryScanner.IsTargetExtension(MediaType.Video, "x.mp4.tmp"));
        Check(!LibraryScanner.IsTargetExtension(MediaType.Video, "x.srt"));
        Check(!LibraryScanner.IsTargetExtension(MediaType.Video, "x.smi"));
    }

    private static void VerifyMissingAndSourceState(string dbPath, string media)
    {
        string path = Path.Combine(media, "shared.mp4");
        File.WriteAllText(path, "shared");
        using var db = LibraryDatabase.Open(dbPath);
        var a = SaveCategory(db, "A", MediaType.Video);
        var b = SaveCategory(db, "B", MediaType.Video);
        var sourceA = AddSource(db, a, media, true, true);
        AddSource(db, b, media, true, true);
        var scanner = new LibraryScanner(db);
        Scan(scanner, a);
        Scan(scanner, b);
        var itemA = db.GetItems(a.Id).Single();
        var itemB = db.GetItems(b.Id).Single();
        Check(itemA.Id != itemB.Id, "Categories must keep independent ItemIds.");

        File.Delete(path);
        Scan(scanner, a);
        Check(db.GetItems(a.Id).Single().IsMissing && db.GetItems(b.Id).Single().IsMissing,
            "Confirmed physical absence must mark same PathKey items across categories Missing.");

        File.WriteAllText(path, "back");
        Scan(scanner, a);
        Check(!db.GetItems(a.Id).Single().IsMissing && db.GetItems(a.Id).Single().Id == itemA.Id);

        db.UpdateSource(sourceA with { IsEnabled = false });
        File.Delete(path);
        Scan(scanner, a);
        Check(!db.GetItems(a.Id).Single().IsMissing, "Disabled source must not imply Missing.");
        db.RemoveSource(sourceA.Id);
        Scan(scanner, a);
        Check(!db.GetItems(a.Id).Single().IsMissing, "Removed source must not imply Missing.");
    }

    private static void VerifyMoveReappearanceAndReplacement(string dbPath, string media)
    {
        string oldPath = Path.Combine(media, "old.mp4");
        string newPath = Path.Combine(media, "new.mp4");
        File.WriteAllText(oldPath, "one");
        using var db = LibraryDatabase.Open(dbPath);
        var category = SaveCategory(db, "Move", MediaType.Video);
        AddSource(db, category, media, true, true);
        var scanner = new LibraryScanner(db);
        Scan(scanner, category);
        var old = db.GetItems(category.Id).Single();
        db.SetFavorite(old.Id, true);
        db.SaveProgress(old.Id, PlaybackProgress.Video(123), 1000);
        Check(db.CommitVisit(new VisitCommitRequest(Guid.NewGuid(), old.Id, 1000,
            VisitOrigin.Manual, false, PlaybackProgress.Video(123))).Status == CommitStatus.Committed);

        File.Move(oldPath, newPath);
        Scan(scanner, category);
        var movedItems = db.GetItems(category.Id);
        var oldMissing = movedItems.Single(x => x.PathKey == old.PathKey);
        var fresh = movedItems.Single(x => x.Path.EndsWith("new.mp4", StringComparison.OrdinalIgnoreCase));
        Check(oldMissing.IsMissing && fresh.Id != old.Id && !fresh.IsFavorite,
            "Move must create a new item without state transfer.");

        File.Move(newPath, oldPath);
        Scan(scanner, category);
        var returned = db.GetItems(category.Id).Single(x => x.PathKey == old.PathKey);
        Check(returned.Id == old.Id && !returned.IsMissing && returned.IsFavorite,
            "Same path reappearance must reuse the old ItemId and preferences.");

        db.SaveProgress(old.Id, PlaybackProgress.Video(321), 2000);
        File.AppendAllText(oldPath, "changed-content");
        File.SetLastWriteTimeUtc(oldPath, DateTime.UtcNow.AddMinutes(1));
        Scan(scanner, category);
        Check(db.GetProgress(old.Id) is null, "Metadata replacement must clear progress.");
        Check(db.GetHistory(old.Id).Count == 1 && db.GetItems(category.Id).Single(x => x.Id == old.Id).IsFavorite,
            "Replacement must preserve history and preferences.");
    }

    private static void VerifyCancellation(string dbPath, string media)
    {
        string path = Path.Combine(media, "cancel.mp4");
        File.WriteAllText(path, "cancel");
        using var db = LibraryDatabase.Open(dbPath);
        var category = SaveCategory(db, "Cancel", MediaType.Video);
        AddSource(db, category, media, true, true);
        var scanner = new LibraryScanner(db);
        Scan(scanner, category);
        var item = db.GetItems(category.Id).Single();
        File.Delete(path);

        using var cts = new CancellationTokenSource();
        var progress = new CallbackProgress(_ => cts.Cancel());
        LibraryScanResult result = scanner.ScanCategoryAsync(category, progress, cts.Token).GetAwaiter().GetResult();
        Check(result.Status == LibraryScanStatus.Cancelled);
        Check(!db.GetItems(category.Id).Single(x => x.Id == item.Id).IsMissing,
            "Cancelled scan must not persist partial Missing state.");
    }

    private static void VerifyAccessFailureAndPartialSuccess(string dbPath, string media)
    {
        string blocked = Directory.CreateDirectory(Path.Combine(media, "blocked")).FullName;
        string blockedFile = Path.Combine(blocked, "keep.mp4");
        File.WriteAllText(blockedFile, "keep");
        using var db = LibraryDatabase.Open(dbPath);
        var category = SaveCategory(db, "Partial", MediaType.Video);
        AddSource(db, category, media, true, true);
        var scanner = new LibraryScanner(db);
        Scan(scanner, category);
        var kept = db.GetItems(category.Id).Single();
        File.WriteAllText(Path.Combine(media, "good.mkv"), "good");

        RequireRun("icacls", blocked, "/inheritance:r", "/deny", Environment.UserName + ":(OI)(CI)F");
        try
        {
            LibraryScanResult result = Scan(scanner, category);
            Check(result.WarningCount != 0, "Access failure must be surfaced as a warning.");
            Check(db.GetItems(category.Id).Any(x => x.Path.EndsWith("good.mkv", StringComparison.OrdinalIgnoreCase)),
                "Successful sibling scope should still be applied.");
            Check(!db.GetItems(category.Id).Single(x => x.Id == kept.Id).IsMissing,
                "Blocked scope must preserve previous existence state.");
        }
        finally
        {
            RequireRun("icacls", blocked, "/remove:d", Environment.UserName, "/inheritance:e", "/T", "/C");
        }
    }

    private static void VerifyReparsePoint(string dbPath, string media)
    {
        string target = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(media)!, "target")).FullName;
        File.WriteAllText(Path.Combine(target, "linked.mp4"), "linked");
        string junction = Path.Combine(media, "junction");
        RequireRun("cmd", "/c", "mklink", "/J", junction, target);
        using var db = LibraryDatabase.Open(dbPath);
        var category = SaveCategory(db, "Reparse", MediaType.Video);
        AddSource(db, category, media, true, true);
        var scanner = new LibraryScanner(db);
        var result = Scan(scanner, category);
        Check(result.WarningCount != 0);
        Check(db.GetItems(category.Id).Count == 0, "Junction contents must not be indexed.");
    }

    private static void VerifyCaseSensitiveDirectory(string dbPath, string media)
    {
        string sensitive = Directory.CreateDirectory(Path.Combine(media, "sensitive")).FullName;
        RequireRun("fsutil", "file", "SetCaseSensitiveInfo", sensitive, "enable");
        try
        {
            File.WriteAllText(Path.Combine(sensitive, "case.mp4"), "case");
            using var db = LibraryDatabase.Open(dbPath);
            var category = SaveCategory(db, "Case", MediaType.Video);
            AddSource(db, category, media, true, true);
            var scanner = new LibraryScanner(db);
            var result = Scan(scanner, category);
            Check(result.WarningCount != 0);
            Check(db.GetItems(category.Id).Count == 0,
                "Case-sensitive directory contents must not be indexed in the initial Windows path contract.");
        }
        finally
        {
            RequireRun("fsutil", "file", "SetCaseSensitiveInfo", sensitive, "disable");
        }
    }

    private static Category SaveCategory(LibraryDatabase db, string name, MediaType type)
    {
        var category = new Category(Guid.NewGuid(), name, type);
        db.SaveCategory(category);
        return category;
    }

    private static CategorySource AddSource(LibraryDatabase db, Category category, string path,
        bool recursive, bool enabled)
    {
        var normalized = SourcePathRules.NormalizeLocalFolder(path);
        var source = new CategorySource(Guid.NewGuid(), category.Id, normalized.Path, normalized.PathKey,
            recursive, enabled);
        return db.AddSource(source);
    }

    private static LibraryScanResult Scan(LibraryScanner scanner, Category category) =>
        scanner.ScanCategoryAsync(category, null, CancellationToken.None).GetAwaiter().GetResult();

    private static void RequireRun(string fileName, params string[] arguments)
    {
        int exit = RunProcess(fileName, arguments);
        if (exit != 0) throw new Exception($"{fileName} failed with exit code {exit}.");
    }

    private static void TryRun(string fileName, params string[] arguments)
    {
        try { RunProcess(fileName, arguments); } catch { }
    }

    private static int RunProcess(string fileName, params string[] arguments)
    {
        var start = new ProcessStartInfo(fileName) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new Exception($"Could not start {fileName}.");
        process.WaitForExit();
        return process.ExitCode;
    }

    private static void Check(bool condition, string message = "T05 assertion failed")
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class CallbackProgress(Action<LibraryScanProgress> callback) : IProgress<LibraryScanProgress>
    {
        public void Report(LibraryScanProgress value) => callback(value);
    }
}
