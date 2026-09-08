using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.ViewModels;
using RandomMultimediaManager.Core;

internal static class T04Verification
{
    [ModuleInitializer]
    internal static void RunAll()
    {
        Run("T04 category/source persistence, duplicate and nested sources", VerifyPersistenceAndSources);
        Run("T04 category type restriction", VerifyTypeRestriction);
        Run("T04 save failure preserves stored data", VerifySaveFailure);
    }

    private static void Run(string name, Action<string> test)
    {
        string directory = Path.Combine(Path.GetTempPath(), "rmm-t04-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            test(Path.Combine(directory, "library.db"));
            Console.WriteLine("PASS: " + name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void VerifyPersistenceAndSources(string path)
    {
        Guid categoryId;
        Guid itemId;
        using (var db = LibraryDatabase.Open(path))
        {
            var vm = new CategoryEditorViewModel(db);
            vm.BeginNewCategory();
            vm.CategoryName = " Videos ";
            vm.CategoryMediaType = MediaType.Video;
            vm.CategoryIsEnabled = false;
            Check(vm.SaveCategory().Success);
            categoryId = vm.SelectedCategory!.Id;

            Check(vm.BeginNewSource().Success);
            vm.SourcePath = @"C:\Media";
            vm.SourceIncludeSubdirectories = false;
            vm.SourceIsEnabled = false;
            Check(vm.SaveSource().Success);

            Check(vm.BeginNewSource().Success);
            vm.SourcePath = @"c:\media\";
            vm.SourceIncludeSubdirectories = true;
            vm.SourceIsEnabled = true;
            Check(vm.SaveSource().Success);
            var first = db.GetSources(categoryId).Single();
            Check(!first.IncludeSubdirectories && !first.IsEnabled,
                "Duplicate registration must not overwrite existing options.");

            Check(vm.BeginNewSource().Success);
            vm.SourcePath = @"C:\Media\Child";
            Check(vm.SaveSource().Success);
            Check(db.GetSources(categoryId).Count == 2, "Nested sources must be allowed.");

            var item = new MediaItem(Guid.NewGuid(), categoryId, MediaType.Video,
                @"C:\Media\video.mp4", @"C:\MEDIA\VIDEO.MP4", 1, 1);
            itemId = item.Id;
            db.ApplyObservedItems(new[] { item }, Array.Empty<Guid>());
            Check(db.CommitVisit(new VisitCommitRequest(Guid.NewGuid(), item.Id, 1000,
                VisitOrigin.Manual, false, PlaybackProgress.Video(10))).Status == CommitStatus.Committed);

            vm.SelectedSource = vm.Sources.Single(x => x.Id == first.Id);
            Check(vm.RemoveSelectedSource().Success);
            Check(!db.GetItems(categoryId).Single().IsMissing);
            Check(db.GetHistory(itemId).Count == 1, "Removing a source must not remove history.");
        }

        using (var reopened = LibraryDatabase.Open(path))
        {
            var category = reopened.GetCategories().Single(x => x.Id == categoryId);
            Check(category.Name == "Videos" && category.MediaType == MediaType.Video && !category.IsEnabled);
            var remaining = reopened.GetSources(categoryId).Single();
            Check(remaining.RootPathKey == @"C:\MEDIA\CHILD" && remaining.IncludeSubdirectories && remaining.IsEnabled);
            Check(reopened.GetItems(categoryId).Single().Id == itemId);
            Check(reopened.GetHistory(itemId).Count == 1);
        }
    }

    private static void VerifyTypeRestriction(string path)
    {
        using var db = LibraryDatabase.Open(path);
        var vm = new CategoryEditorViewModel(db);
        vm.BeginNewCategory();
        vm.CategoryName = "Empty";
        vm.CategoryMediaType = MediaType.Video;
        Check(vm.SaveCategory().Success);
        Guid id = vm.SelectedCategory!.Id;

        vm.CategoryMediaType = MediaType.Comic;
        Check(vm.SaveCategory().Success, "An empty category may change type.");

        var item = new MediaItem(Guid.NewGuid(), id, MediaType.Comic,
            @"C:\Comics\book.cbz", @"C:\COMICS\BOOK.CBZ", 1, 1);
        db.ApplyObservedItems(new[] { item }, Array.Empty<Guid>());
        vm.CategoryMediaType = MediaType.Video;
        Check(!vm.SaveCategory().Success, "A category containing an item must reject type changes.");
        Check(db.GetCategories().Single(x => x.Id == id).MediaType == MediaType.Comic);
    }

    private static void VerifySaveFailure(string path)
    {
        using var db = LibraryDatabase.Open(path);
        var vm = new CategoryEditorViewModel(db);
        vm.BeginNewCategory();
        vm.CategoryName = "Stable";
        Check(vm.SaveCategory().Success);
        Guid id = vm.SelectedCategory!.Id;

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Pooling = false, ForeignKeys = true }.ToString());
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TRIGGER fail_t04 BEFORE UPDATE ON Category BEGIN SELECT RAISE(ABORT,'injected'); END;";
            command.ExecuteNonQuery();
        }

        vm.CategoryName = "Should not persist";
        var result = vm.SaveCategory();
        Check(!result.Success, "Injected storage failure must not be reported as success.");
        Check(db.GetCategories().Single(x => x.Id == id).Name == "Stable");
    }

    private static void Check(bool condition, string message = "T04 assertion failed")
    {
        if (!condition) throw new Exception(message);
    }
}
