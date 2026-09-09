using System.Diagnostics;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Scanning;
using RandomMultimediaManager.App.ViewModels;
using RandomMultimediaManager.Core;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("SKIP: T05 scanner verification requires Windows.");
    return;
}
if (args.Length != 1) throw new ArgumentException("Specify one scenario: basic, state, cancel, access, reparse, case.");

switch (args[0].ToLowerInvariant())
{
    case "basic": Run("basic filtering/dedupe/recursion", VerifyBasic); break;
    case "state": Run("Missing/move/reappearance/replacement", VerifyState); break;
    case "cancel": Run("cancellation", VerifyCancellation); break;
    case "access": Run("access denial", VerifyAccessFailure); break;
    case "reparse": Run("reparse partial success", VerifyReparse); break;
    case "case": Run("case-sensitive directory rejection", VerifyCaseSensitive); break;
    default: throw new ArgumentException("Unknown T05 scenario: " + args[0]);
}

static void Run(string name, Action<string, string> test)
{
    string directory = Path.Combine(Path.GetTempPath(), "rmm-t05-" + Guid.NewGuid());
    string media = Path.Combine(directory, "media");
    Directory.CreateDirectory(media);
    Console.WriteLine("START: " + name);
    try
    {
        test(Path.Combine(directory, "library.db"), media);
        Console.WriteLine("PASS: " + name);
    }
    finally
    {
        try { Directory.Delete(directory, recursive: true); } catch { }
    }
}

static void VerifyBasic(string dbPath, string media)
{
    string child = Directory.CreateDirectory(Path.Combine(media, "child")).FullName;
    File.WriteAllText(Path.Combine(media, "ROOT.MP4"), "root");
    File.WriteAllText(Path.Combine(media, "ignored.mp4.tmp"), "ignored");
    File.WriteAllText(Path.Combine(media, "movie.srt"), "subtitle");
    File.WriteAllText(Path.Combine(media, "movie.smi"), "subtitle");
    File.WriteAllText(Path.Combine(media, "noextension"), "ignored");
    File.WriteAllText(Path.Combine(child, "nested.mkv"), "nested");
    File.WriteAllText(Path.Combine(media, "book.ZIP"), "zip placeholder");
    File.WriteAllText(Path.Combine(media, "book.cbz"), "cbz placeholder");

    using var db = LibraryDatabase.Open(dbPath);
    var video = SaveCategory(db, "Video", MediaType.Video);
    AddSource(db, video, media, true, true);
    AddSource(db, video, child, true, true);
    var scanner = new LibraryScanner(db);
    Scan(scanner, video);
    var firstItems = db.GetItems(video.Id);
    Check(firstItems.Count == 2, "Expected ROOT.MP4 and nested.mkv exactly once.");
    var ids = firstItems.ToDictionary(x => x.PathKey, x => x.Id);
    Scan(scanner, video);
    Check(db.GetItems(video.Id).Count == 2 && db.GetItems(video.Id).All(x => ids[x.PathKey] == x.Id),
        "Rescan must not duplicate items or change ItemId.");

    var direct = SaveCategory(db, "Direct", MediaType.Video);
    AddSource(db, direct, media, false, true);
    Scan(scanner, direct);
    Check(db.GetItems(direct.Id).Count == 1 && db.GetItems(direct.Id).Single().Path.EndsWith("ROOT.MP4"));

    var comic = SaveCategory(db, "Comic", MediaType.Comic);
    AddSource(db, comic, media, true, true);
    Scan(scanner, comic);
    Check(db.GetItems(comic.Id).Count == 2, "ZIP/CBZ should be the only comic targets.");
    Check(LibraryScanner.IsTargetExtension(MediaType.Video, "x.MP4"));
    Check(!LibraryScanner.IsTargetExtension(MediaType.Video, "x.mp4.tmp"));
    Check(!LibraryScanner.IsTargetExtension(MediaType.Video, "x.srt"));
    Check(!LibraryScanner.IsTargetExtension(MediaType.Video, "x.smi"));
}

static void VerifyState(string dbPath, string media)
{
    string shared = Path.Combine(media, "shared.mp4");
    File.WriteAllText(shared, "shared");
    using var db = LibraryDatabase.Open(dbPath);
    var a = SaveCategory(db, "A", MediaType.Video);
    var b = SaveCategory(db, "B", MediaType.Video);
    var sourceA = AddSource(db, a, media, true, true);
    AddSource(db, b, media, true, true);
    var scanner = new LibraryScanner(db);
    Scan(scanner, a); Scan(scanner, b);
    var itemA = db.GetItems(a.Id).Single();
    var itemB = db.GetItems(b.Id).Single();
    Check(itemA.Id != itemB.Id, "Same path in different categories needs distinct ItemId.");

    File.Delete(shared);
    Scan(scanner, a);
    Check(db.GetItems(a.Id).Single().IsMissing && db.GetItems(b.Id).Single().IsMissing,
        "Confirmed absence should mark same PathKey across categories Missing.");

    File.WriteAllText(shared, "back");
    Scan(scanner, a);
    Check(!db.GetItems(a.Id).Single().IsMissing && db.GetItems(a.Id).Single().Id == itemA.Id,
        "Same path reappearance should reuse the original category ItemId.");
    db.UpdateSource(sourceA with { IsEnabled = false });
    File.Delete(shared);
    Scan(scanner, a);
    Check(!db.GetItems(a.Id).Single().IsMissing, "Disabled source must not imply Missing.");
    db.RemoveSource(sourceA.Id);
    Scan(scanner, a);
    Check(!db.GetItems(a.Id).Single().IsMissing, "Removed source must not imply Missing.");

    string oldPath = Path.Combine(media, "old.mp4");
    string newPath = Path.Combine(media, "new.mp4");
    File.WriteAllText(oldPath, "one");
    var move = SaveCategory(db, "Move", MediaType.Video);
    AddSource(db, move, media, true, true);
    Scan(scanner, move);
    var old = db.GetItems(move.Id).Single();
    db.SetFavorite(old.Id, true);
    db.SaveProgress(old.Id, PlaybackProgress.Video(123), 1000);
    Check(db.CommitVisit(new VisitCommitRequest(Guid.NewGuid(), old.Id, 1000,
        VisitOrigin.Manual, false, PlaybackProgress.Video(123))).Status == CommitStatus.Committed);

    File.Move(oldPath, newPath);
    Scan(scanner, move);
    var moved = db.GetItems(move.Id);
    var newItem = moved.Single(x => x.Path.EndsWith("new.mp4", StringComparison.OrdinalIgnoreCase));
    Check(moved.Single(x => x.Id == old.Id).IsMissing && newItem.Id != old.Id && !newItem.IsFavorite,
        "Move must create new item without state transfer.");

    File.Move(newPath, oldPath);
    Scan(scanner, move);
    var returned = db.GetItems(move.Id).Single(x => x.Id == old.Id);
    Check(!returned.IsMissing && returned.IsFavorite);
    db.SaveProgress(old.Id, PlaybackProgress.Video(321), 2000);
    File.AppendAllText(oldPath, "changed-content");
    File.SetLastWriteTimeUtc(oldPath, DateTime.UtcNow.AddMinutes(1));
    Scan(scanner, move);
    Check(db.GetProgress(old.Id) is null, "Content metadata change must clear progress.");
    Check(db.GetHistory(old.Id).Count == 1 && db.GetItems(move.Id).Single(x => x.Id == old.Id).IsFavorite,
        "Content replacement must preserve history/preferences.");

    string removedRoot = Directory.CreateDirectory(Path.Combine(media, "removed-root")).FullName;
    File.WriteAllText(Path.Combine(removedRoot, "gone.mp4"), "gone");
    var removedCategory = SaveCategory(db, "Removed root", MediaType.Video);
    AddSource(db, removedCategory, removedRoot, true, true);
    Scan(scanner, removedCategory);
    Directory.Delete(removedRoot, recursive: true);
    Scan(scanner, removedCategory);
    Check(db.GetItems(removedCategory.Id).Single().IsMissing,
        "Missing source beneath supported ancestors must still confirm Missing.");

}

static void VerifyCancellation(string dbPath, string media)
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
    var result = scanner.ScanCategoryAsync(category, progress, cts.Token).GetAwaiter().GetResult();
    Check(result.Status == LibraryScanStatus.Cancelled);
    Check(!db.GetItems(category.Id).Single(x => x.Id == item.Id).IsMissing,
        "Cancelled scan must not persist Missing.");
}

static void VerifyAccessFailure(string dbPath, string media)
{
    string blocked = Directory.CreateDirectory(Path.Combine(media, "blocked")).FullName;
    string path = Path.Combine(blocked, "existing.mp4");
    File.WriteAllText(path, "existing");
    using var db = LibraryDatabase.Open(dbPath);
    var category = SaveCategory(db, "Denied", MediaType.Video);
    AddSource(db, category, blocked, false, true);
    var scanner = new LibraryScanner(db);
    Scan(scanner, category);
    var item = db.GetItems(category.Id).Single();
    RequireRun("icacls", blocked, "/inheritance:r", "/deny", Environment.UserName + ":(OI)(CI)F");
    try
    {
        var result = Scan(scanner, category);
        Check(result.WarningCount != 0, "Access denial must be reported.");
        Check(!db.GetItems(category.Id).Single(x => x.Id == item.Id).IsMissing,
            "Access denial must preserve previous state.");
    }
    finally
    {
        RequireRun("icacls", blocked, "/remove:d", Environment.UserName, "/inheritance:e");
    }
}

static void VerifyReparse(string dbPath, string media)
{
    string target = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(media)!, "target")).FullName;
    File.WriteAllText(Path.Combine(target, "linked.mp4"), "linked");
    string junction = Path.Combine(media, "junction");
    RequireRun("cmd", "/c", "mklink", "/J", junction, target);
    VerifyMissingSourceUnderUnsupportedParent(Path.Combine(Path.GetDirectoryName(media)!, "reparse-missing.db"), junction);
    File.WriteAllText(Path.Combine(media, "good.mkv"), "good");
    using var db = LibraryDatabase.Open(dbPath);
    var category = SaveCategory(db, "Reparse", MediaType.Video);
    AddSource(db, category, media, true, true);
    string oldPath = Path.Combine(junction, "old.mp4");
    var old = new MediaItem(Guid.NewGuid(), category.Id, MediaType.Video, oldPath,
        oldPath.ToUpperInvariant(), 1, 1);
    db.ApplyObservedItems(new[] { old }, Array.Empty<Guid>());
    var result = Scan(new LibraryScanner(db), category);
    Check(result.WarningCount != 0);
    Check(db.GetItems(category.Id).Any(x => x.Path.EndsWith("good.mkv", StringComparison.OrdinalIgnoreCase)));
    Check(!db.GetItems(category.Id).Single(x => x.Id == old.Id).IsMissing,
        "Reparse subtree must preserve old state.");
    Check(!db.GetItems(category.Id).Any(x => x.Path.EndsWith("linked.mp4", StringComparison.OrdinalIgnoreCase)),
        "Junction target must not be followed.");
}

static void VerifyCaseSensitive(string dbPath, string media)
{
    string sensitive = Directory.CreateDirectory(Path.Combine(media, "sensitive")).FullName;
    RequireRun("fsutil", "file", "SetCaseSensitiveInfo", sensitive, "enable");
    try
    {
        VerifyMissingSourceUnderUnsupportedParent(Path.Combine(Path.GetDirectoryName(media)!, "case-missing.db"), sensitive);
        File.WriteAllText(Path.Combine(sensitive, "case.mp4"), "case");
        using var db = LibraryDatabase.Open(dbPath);
        var category = SaveCategory(db, "Case", MediaType.Video);
        AddSource(db, category, media, true, true);
        var result = Scan(new LibraryScanner(db), category);
        Check(result.WarningCount != 0);
        Check(db.GetItems(category.Id).Count == 0, "Case-sensitive subtree must not be indexed.");
    }
    finally
    {
        RequireRun("fsutil", "file", "SetCaseSensitiveInfo", sensitive, "disable");
    }
}

static void VerifyMissingSourceUnderUnsupportedParent(string dbPath, string parent)
{
    string missingSource = Path.Combine(parent, "absent-child");
    string oldPath = Path.Combine(missingSource, "old.mp4");
    using var db = LibraryDatabase.Open(dbPath);
    var category = SaveCategory(db, "Unsupported ancestor", MediaType.Video);
    AddSource(db, category, missingSource, true, true);
    var old = new MediaItem(Guid.NewGuid(), category.Id, MediaType.Video, oldPath,
        oldPath.ToUpperInvariant(), 1, 1);
    db.ApplyObservedItems(new[] { old }, Array.Empty<Guid>());
    var result = Scan(new LibraryScanner(db), category);
    Check(result.WarningCount != 0, "Unsupported ancestor must be reported before descendant absence.");
    Check(!db.GetItems(category.Id).Single().IsMissing,
        "Missing source under unsupported ancestor must preserve existing state.");
}

static Category SaveCategory(LibraryDatabase db, string name, MediaType type)
{
    var category = new Category(Guid.NewGuid(), name, type);
    db.SaveCategory(category);
    return category;
}

static CategorySource AddSource(LibraryDatabase db, Category category, string path, bool recursive, bool enabled)
{
    var normalized = SourcePathRules.NormalizeLocalFolder(path);
    return db.AddSource(new CategorySource(Guid.NewGuid(), category.Id, normalized.Path, normalized.PathKey,
        recursive, enabled));
}

static LibraryScanResult Scan(LibraryScanner scanner, Category category) =>
    scanner.ScanCategoryAsync(category, null, CancellationToken.None).GetAwaiter().GetResult();

static void RequireRun(string fileName, params string[] arguments)
{
    var start = new ProcessStartInfo(fileName) { UseShellExecute = false, CreateNoWindow = true };
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new Exception("Could not start " + fileName);
    if (!process.WaitForExit(10000))
    {
        try { process.Kill(entireProcessTree: true); } catch { }
        throw new Exception(fileName + " timed out.");
    }
    if (process.ExitCode != 0) throw new Exception($"{fileName} failed with exit code {process.ExitCode}.");
}

static void Check(bool condition, string message = "T05 assertion failed")
{
    if (!condition) throw new Exception(message);
}

sealed class CallbackProgress(Action<LibraryScanProgress> callback) : IProgress<LibraryScanProgress>
{
    public void Report(LibraryScanProgress value) => callback(value);
}
