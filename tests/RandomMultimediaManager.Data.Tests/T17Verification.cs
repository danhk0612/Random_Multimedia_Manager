using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.ViewModels;
using RandomMultimediaManager.Core;

internal static class T17Verification
{
    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "rmm-t17-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            using var database = LibraryDatabase.Open(Path.Combine(root, "library.db"));
            var category = new Category(Guid.NewGuid(), "Library", MediaType.Video);
            var otherCategory = new Category(Guid.NewGuid(), "Other", MediaType.Comic);
            database.SaveCategory(category);
            database.SaveCategory(otherCategory);

            MediaItem Add(string name, Category owner, bool favorite = false, bool excluded = false, bool missing = false)
            {
                string path = Path.Combine(root, name);
                File.WriteAllBytes(path, [1, 2, 3]);
                var file = new FileInfo(path);
                var item = new MediaItem(Guid.NewGuid(), owner.Id, owner.MediaType,
                    file.FullName, file.FullName.ToUpperInvariant(), file.Length, file.LastWriteTimeUtc.Ticks,
                    favorite, excluded);
                database.ApplyObservedItems([item], []);
                if (favorite) database.SetFavorite(item.Id, true);
                if (excluded) database.SetRandomExcluded(item.Id, true);
                if (missing) database.ApplyObservedItems([], [item.Id]);
                return item;
            }

            var alpha = Add("alpha.mp4", category, favorite: true);
            var beta = Add("beta.mp4", category, excluded: true);
            var gamma = Add("gamma.mp4", category, missing: true);
            var other = Add("other.cbz", otherCategory);
            database.CommitVisit(new(Guid.NewGuid(), alpha.Id, 1000, VisitOrigin.Manual,
                false, PlaybackProgress.Video(50)));
            database.CommitVisit(new(Guid.NewGuid(), gamma.Id, 2000, VisitOrigin.Manual,
                false, PlaybackProgress.Video(70)));
            database.SaveProgress(alpha.Id, PlaybackProgress.Video(65_000), 3000);

            var browser = new LibraryBrowserViewModel(database);
            await browser.LoadCategoriesAsync();
            browser.SelectedCategory = category;
            await browser.LoadSelectedCategoryAsync();
            Check(browser.VisibleItems.Count == 3, "T17 lists only files from the selected category");
            Check(browser.VisibleItems[0].FileName == "alpha.mp4"
                && browser.VisibleItems[0].CategoryName == category.Name
                && browser.VisibleItems[0].SizeLabel == "3 B"
                && browser.VisibleItems[0].LastViewedAtUtc == 1000,
                "T17 file information includes name, category, indexed size and last visit");
            browser.SelectedItem = browser.VisibleItems.Single(row => row.Item.Id == alpha.Id);
            await browser.LoadSelectedProgressAsync();
            Check(browser.SelectedProgressText == "00:01:05", "T17 file information includes stored playback progress");
            Check(browser.VisibleItems.Single(row => row.Item.Id == gamma.Id).CanOpen == false,
                "T17 missing files remain visible but cannot be manually opened");

            browser.SearchText = "ALPHA";
            Check(browser.VisibleItems.Count == 1 && browser.VisibleItems[0].Item.Id == alpha.Id,
                "T17 filename search ignores case");
            browser.SearchText = string.Empty;
            browser.SelectedFavoriteFilter = browser.FavoriteFilters.Single(filter => filter.Value == true);
            Check(browser.VisibleItems.Select(row => row.Item.Id).SequenceEqual([alpha.Id]),
                "T17 favorite filter");
            browser.SelectedFavoriteFilter = browser.FavoriteFilters[0];
            browser.SelectedExcludedFilter = browser.ExcludedFilters.Single(filter => filter.Value == true);
            Check(browser.VisibleItems.Select(row => row.Item.Id).SequenceEqual([beta.Id]),
                "T17 random exclusion filter");
            browser.SelectedExcludedFilter = browser.ExcludedFilters[0];
            browser.SelectedHistoryFilter = browser.HistoryFilters.Single(filter => filter.Value == true);
            Check(browser.VisibleItems.Select(row => row.Item.Id).SequenceEqual([alpha.Id, gamma.Id]),
                "T17 viewed history filter");
            browser.SelectedHistoryFilter = browser.HistoryFilters.Single(filter => filter.Value == false);
            Check(browser.VisibleItems.Select(row => row.Item.Id).SequenceEqual([beta.Id]),
                "T17 no-history filter");

            browser.SelectedHistoryFilter = browser.HistoryFilters[0];
            browser.SelectedCategory = otherCategory;
            await browser.RefreshAsync();
            Check(browser.VisibleItems.Count == 1 && browser.VisibleItems[0].Item.Id == other.Id,
                "T17 category refresh does not mix category-scoped item state");
            Check(database.GetItems(category.Id).Single(item => item.Id == alpha.Id).IsFavorite,
                "T17 browsing filters do not modify stored item flags");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}
