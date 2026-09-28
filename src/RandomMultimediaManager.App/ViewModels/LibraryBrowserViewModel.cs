using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.ViewModels;

public sealed record LibraryBooleanFilter(string Label, bool? Value);

public sealed record LibraryFileRow(MediaItem Item, string CategoryName, long? LastViewedAtUtc)
{
    public string FileName => Path.GetFileName(Item.Path);
    public string TypeLabel => Item.MediaType == MediaType.Comic ? "만화" : "영상";
    public string AvailabilityLabel => Item.IsMissing ? "누락" : "등록됨";
    public string FavoriteLabel => Item.IsFavorite ? "즐겨찾기" : "일반";
    public string ExclusionLabel => Item.IsRandomExcluded ? "랜덤 제외" : "랜덤 포함";
    public string SizeLabel => FormatSize(Item.FileSize);
    public string LastWriteLabel => FormatLocalTime(Item.LastWriteTimeUtc);
    public string LastViewedLabel => LastViewedAtUtc is { } value
        ? DateTimeOffset.FromUnixTimeMilliseconds(value).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)
        : "감상 기록 없음";
    public bool CanOpen => !Item.IsMissing;

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = Math.Max(0, bytes);
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{size:0} {units[unit]}" : $"{size:0.##} {units[unit]}";
    }

    private static string FormatLocalTime(long fileTimeUtc)
    {
        try { return new DateTime(fileTimeUtc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture); }
        catch (ArgumentOutOfRangeException) { return "알 수 없음"; }
    }
}

public sealed class LibraryBrowserViewModel : INotifyPropertyChanged
{
    private static readonly LibraryBooleanFilter[] favoriteFilters =
    [
        new("전체", null), new("즐겨찾기", true), new("즐겨찾기 아님", false)
    ];
    private static readonly LibraryBooleanFilter[] excludedFilters =
    [
        new("전체", null), new("랜덤 제외", true), new("랜덤 포함", false)
    ];
    private static readonly LibraryBooleanFilter[] historyFilters =
    [
        new("전체", null), new("감상 기록 있음", true), new("감상 기록 없음", false)
    ];

    private readonly LibraryDatabase database;
    private readonly List<LibraryFileRow> categoryItems = [];
    private readonly object readGate = new();
    private readonly HashSet<Task> pendingReads = [];
    private Category? selectedCategory;
    private LibraryFileRow? selectedItem;
    private string selectedProgressText = "파일을 선택하세요.";
    private int progressVersion;
    private string searchText = string.Empty;
    private LibraryBooleanFilter selectedFavoriteFilter = favoriteFilters[0];
    private LibraryBooleanFilter selectedExcludedFilter = excludedFilters[0];
    private LibraryBooleanFilter selectedHistoryFilter = historyFilters[0];
    private string statusMessage = "분류를 선택하세요.";
    private int loadVersion;
    private bool exitRequested;

    public LibraryBrowserViewModel(LibraryDatabase database) => this.database = database;

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<Category> Categories { get; } = [];
    public ObservableCollection<LibraryFileRow> VisibleItems { get; } = [];
    public IReadOnlyList<LibraryBooleanFilter> FavoriteFilters => favoriteFilters;
    public IReadOnlyList<LibraryBooleanFilter> ExcludedFilters => excludedFilters;
    public IReadOnlyList<LibraryBooleanFilter> HistoryFilters => historyFilters;

    public Category? SelectedCategory
    {
        get => selectedCategory;
        set
        {
            if (selectedCategory?.Id == value?.Id) return;
            selectedCategory = value;
            categoryItems.Clear();
            VisibleItems.Clear();
            SelectedItem = null;
            OnPropertyChanged();
        }
    }

    public LibraryFileRow? SelectedItem
    {
        get => selectedItem;
        set
        {
            if (!Set(ref selectedItem, value)) return;
            Interlocked.Increment(ref progressVersion);
            SelectedProgressText = value is null ? "파일을 선택하세요." : "불러오는 중입니다.";
            OnPropertyChanged(nameof(HasSelectedItem));
            OnPropertyChanged(nameof(CanOpenSelected));
        }
    }

    public bool HasSelectedItem => SelectedItem is not null;
    public bool CanOpenSelected => SelectedItem?.CanOpen == true;
    public string SelectedProgressText { get => selectedProgressText; private set => Set(ref selectedProgressText, value); }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (!Set(ref searchText, value)) return;
            ApplyFilters();
        }
    }

    public LibraryBooleanFilter SelectedFavoriteFilter
    {
        get => selectedFavoriteFilter;
        set { if (Set(ref selectedFavoriteFilter, value)) ApplyFilters(); }
    }

    public LibraryBooleanFilter SelectedExcludedFilter
    {
        get => selectedExcludedFilter;
        set { if (Set(ref selectedExcludedFilter, value)) ApplyFilters(); }
    }

    public LibraryBooleanFilter SelectedHistoryFilter
    {
        get => selectedHistoryFilter;
        set { if (Set(ref selectedHistoryFilter, value)) ApplyFilters(); }
    }

    public string StatusMessage { get => statusMessage; private set => Set(ref statusMessage, value); }

    public Task LoadCategoriesAsync() => exitRequested
        ? Task.CompletedTask : TrackRead(LoadCategoriesCoreAsync());

    private async Task LoadCategoriesCoreAsync()
    {
        try
        {
            var categories = await Task.Run(database.GetCategories);
            if (exitRequested) return;
            Categories.Clear();
            foreach (var category in categories) Categories.Add(category);
            SelectedCategory = Categories.FirstOrDefault();
        }
        catch (Exception ex)
        {
            if (!exitRequested) StatusMessage = $"분류 목록을 불러오지 못했습니다: {ex.Message}";
        }
    }

    public Task LoadSelectedCategoryAsync() => exitRequested
        ? Task.CompletedTask : TrackRead(LoadSelectedCategoryCoreAsync());

    private async Task LoadSelectedCategoryCoreAsync()
    {
        Category? category = SelectedCategory;
        int requestVersion = Interlocked.Increment(ref loadVersion);
        if (category is null)
        {
            categoryItems.Clear();
            VisibleItems.Clear();
            SelectedItem = null;
            StatusMessage = "분류를 선택하세요.";
            return;
        }

        Guid? preserveId = SelectedItem?.Item.Id;
        StatusMessage = "파일 목록을 불러오는 중입니다.";
        try
        {
            CandidateSnapshot snapshot = await Task.Run(database.GetSessionSnapshot);
            if (exitRequested || requestVersion != loadVersion) return;
            categoryItems.Clear();
            categoryItems.AddRange(snapshot.Items
                .Where(item => item.CategoryId == category.Id)
                .Select(item => new LibraryFileRow(item, category.Name,
                    snapshot.LastViewed.TryGetValue(item.Id, out long viewedAt) ? viewedAt : null))
                .OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Item.Path, StringComparer.OrdinalIgnoreCase));
            ApplyFilters(preserveId);
        }
        catch (Exception ex)
        {
            if (exitRequested || requestVersion != loadVersion) return;
            categoryItems.Clear();
            VisibleItems.Clear();
            SelectedItem = null;
            StatusMessage = $"파일 목록을 불러오지 못했습니다: {ex.Message}";
        }
    }

    public Task RefreshAsync() => LoadSelectedCategoryAsync();

    public Task LoadSelectedProgressAsync() => exitRequested
        ? Task.CompletedTask : TrackRead(LoadSelectedProgressCoreAsync());

    private async Task LoadSelectedProgressCoreAsync()
    {
        LibraryFileRow? row = SelectedItem;
        int requestVersion = Interlocked.Increment(ref progressVersion);
        if (row is null)
        {
            SelectedProgressText = "파일을 선택하세요.";
            return;
        }

        try
        {
            StoredProgress? progress = await Task.Run(() => database.GetProgress(row.Item.Id));
            if (exitRequested || requestVersion != progressVersion || SelectedItem?.Item.Id != row.Item.Id) return;
            SelectedProgressText = progress is null ? "저장된 진행 없음" : FormatProgress(progress.Position);
        }
        catch (Exception ex)
        {
            if (!exitRequested && requestVersion == progressVersion && SelectedItem?.Item.Id == row.Item.Id)
                StatusMessage = $"진행 정보를 불러오지 못했습니다: {ex.Message}";
        }
    }

    public void ReportMessage(string message) => StatusMessage = message;

    public void SetExitRequested(bool value)
    {
        exitRequested = value;
        if (value)
        {
            Interlocked.Increment(ref loadVersion);
            Interlocked.Increment(ref progressVersion);
        }
    }

    public async Task WaitForPendingReadsAsync()
    {
        while (true)
        {
            Task[] reads;
            lock (readGate) reads = pendingReads.ToArray();
            if (reads.Length == 0) return;
            try { await Task.WhenAll(reads); }
            catch { /* The UI operation reports its own read failure; exit still waits for completion. */ }
        }
    }

    private Task TrackRead(Task operation)
    {
        lock (readGate) pendingReads.Add(operation);
        _ = operation.ContinueWith(_ =>
        {
            lock (readGate) pendingReads.Remove(operation);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return operation;
    }

    private void ApplyFilters(Guid? preserveId = null)
    {
        preserveId ??= SelectedItem?.Item.Id;
        IEnumerable<LibraryFileRow> query = categoryItems;
        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(row => row.FileName.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase));
        if (SelectedFavoriteFilter.Value is { } favorite)
            query = query.Where(row => row.Item.IsFavorite == favorite);
        if (SelectedExcludedFilter.Value is { } excluded)
            query = query.Where(row => row.Item.IsRandomExcluded == excluded);
        if (SelectedHistoryFilter.Value is { } viewed)
            query = query.Where(row => (row.LastViewedAtUtc is not null) == viewed);

        var filtered = query.ToArray();
        VisibleItems.Clear();
        foreach (var row in filtered) VisibleItems.Add(row);
        SelectedItem = preserveId is { } id ? filtered.FirstOrDefault(row => row.Item.Id == id) : null;
        StatusMessage = categoryItems.Count == 0
            ? (SelectedCategory is null ? "분류를 선택하세요." : "선택한 분류에 등록된 파일이 없습니다.")
            : filtered.Length == 0 ? "검색 또는 필터 조건에 맞는 파일이 없습니다." : string.Empty;
    }

    private static string FormatProgress(PlaybackProgress position) => position.MediaType switch
    {
        MediaType.Comic when position.ComicPageIndex is { } page => position.ComicPageOffset is > 0 and <= 1
            ? $"페이지 {page + 1} · 세로 위치 {position.ComicPageOffset:P0}"
            : $"페이지 {page + 1}",
        MediaType.Video when position.VideoPositionMs is { } milliseconds =>
            TimeSpan.FromMilliseconds(milliseconds).ToString(@"hh\:mm\:ss", CultureInfo.CurrentCulture),
        _ => "저장된 진행 없음"
    };

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
