using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Scanning;
using RandomMultimediaManager.App.ViewModels;
using RandomMultimediaManager.Core;

internal static class T18A6IntegrationVerification
{
    public static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "rmm-t18a6-" + Guid.NewGuid());
        string media = Directory.CreateDirectory(Path.Combine(root, "한글 미디어 폴더")).FullName;
        string[] names = ["책.zip", "책.cbz", "영상.mp4", "영상.srt", "영상.smi"];
        var locks = new List<FileStream>();
        try
        {
            foreach (string name in names)
            {
                string path = Path.Combine(media, name);
                File.WriteAllText(path, "invalid media fixture: metadata only");
                locks.Add(new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
                bool denied = false;
                try { using var read = File.OpenRead(path); }
                catch (IOException) { denied = true; }
                Check(denied, "N12 fixture denies body reads: " + name);
            }

            using var database = LibraryDatabase.Open(Path.Combine(root, "library.db"));
            var scans = new ScanCoordinator(database);
            try
            {
                foreach (var type in new[] { MediaType.Comic, MediaType.Video })
                {
                    var category = new Category(Guid.NewGuid(), type.ToString(), type);
                    database.SaveCategory(category);
                    var editor = new CategoryEditorViewModel(database, scans) { SelectedCategory = category };
                    editor.SourcePath = media;
                    Check(editor.SaveSource().Success, "N01/N12 UI registers local source: " + type);
                    Check((await editor.ConfirmSelectedBindingAsync()).Success,
                        "N03/N12 real Windows binding confirmation: " + type);
                    editor.SourceScanOnStartup = false;
                    editor.SelectedSourceRefreshMode = SourceRefreshMode.Manual;
                    Check((await editor.SaveSelectedSourcePolicyAsync()).Success,
                        "N04/N12 UI saves Manual policy: " + type);
                    var source = editor.SelectedSource!;
                    var result = await scans.ScanSourceAsync(source.Id);
                    int expected = type == MediaType.Comic ? 2 : 1;
                    Check(result.Status == LibraryScanStatus.Completed && result.WarningCount == 0
                        && result.PresentCount == expected && result.MissingCount == 0,
                        "N12 scan succeeds while ZIP/video/subtitle body reads are denied: " + type);
                    var items = database.GetItems(category.Id);
                    Check(items.Count == expected && items.All(item => !item.IsMissing && item.FileSize > 0),
                        "N12 locked invalid media are indexed from metadata only: " + type);
                    var ids = items.ToDictionary(item => item.PathKey, item => item.Id);
                    Check(database.GetEffectiveSourceRefreshPolicy(source.Id).LastCompletedAtUtc is not null,
                        "N04/N12 first successful manual scan persists completion time: " + type);
                    var repeatedResult = await scans.ScanSourceAsync(source.Id);
                    Check(repeatedResult.Status == LibraryScanStatus.Completed && repeatedResult.WarningCount == 0
                        && repeatedResult.PresentCount == expected && repeatedResult.MissingCount == 0,
                        "N02/N12 repeated UI-configured scan completes successfully: " + type);
                    var repeatedItems = database.GetItems(category.Id);
                    Check(repeatedItems.Count == expected
                        && repeatedItems.Select(item => (item.PathKey, item.Id)).ToHashSet()
                            .SetEquals(ids.Select(pair => (pair.Key, pair.Value))),
                        "N02/N12 repeated UI-configured scan preserves exact count and PathKey/ItemId set: " + type);
                    // Successful repeat is proven by its result, not by a timestamp that may be unchanged.
                    Check(database.GetEffectiveSourceRefreshPolicy(source.Id).LastCompletedAtUtc is not null,
                        "N04/N12 completion time remains stored after verified successful repeated scan: " + type);
                }
            }
            finally { await scans.CloseAsync(); }
            Console.WriteLine("PASS: T18A-6 real local UI/binding/policy/metadata integration");
        }
        finally
        {
            foreach (var file in locks) file.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}
