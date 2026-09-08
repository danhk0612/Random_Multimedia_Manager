using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.ViewModels;

public sealed record EditorResult(bool Success, string Message);

public sealed class CategoryEditorViewModel : INotifyPropertyChanged
{
    private readonly LibraryDatabase database;
    private Category? selectedCategory;
    private CategorySource? selectedSource;
    private Guid categoryDraftId;
    private Guid sourceDraftId;
    private string categoryName = string.Empty;
    private MediaType categoryMediaType = MediaType.Comic;
    private bool categoryIsEnabled = true;
    private string sourcePath = string.Empty;
    private bool sourceIncludeSubdirectories = true;
    private bool sourceIsEnabled = true;
    private string statusMessage = string.Empty;

    public CategoryEditorViewModel(LibraryDatabase database)
    {
        this.database = database;
        ReloadCategories();
    }

    public ObservableCollection<Category> Categories { get; } = new();
    public ObservableCollection<CategorySource> Sources { get; } = new();
    public IReadOnlyList<MediaType> MediaTypes { get; } = Enum.GetValues<MediaType>();

    public Category? SelectedCategory
    {
        get => selectedCategory;
        set
        {
            if (selectedCategory?.Id == value?.Id) return;
            selectedCategory = value;
            OnPropertyChanged();
            LoadCategoryDraft(value);
            ReloadSources();
        }
    }

    public CategorySource? SelectedSource
    {
        get => selectedSource;
        set
        {
            if (selectedSource?.Id == value?.Id) return;
            selectedSource = value;
            OnPropertyChanged();
            LoadSourceDraft(value);
        }
    }

    public string CategoryName { get => categoryName; set => Set(ref categoryName, value); }
    public MediaType CategoryMediaType { get => categoryMediaType; set => Set(ref categoryMediaType, value); }
    public bool CategoryIsEnabled { get => categoryIsEnabled; set => Set(ref categoryIsEnabled, value); }
    public string SourcePath { get => sourcePath; set => Set(ref sourcePath, value); }
    public bool SourceIncludeSubdirectories { get => sourceIncludeSubdirectories; set => Set(ref sourceIncludeSubdirectories, value); }
    public bool SourceIsEnabled { get => sourceIsEnabled; set => Set(ref sourceIsEnabled, value); }
    public string StatusMessage { get => statusMessage; private set => Set(ref statusMessage, value); }

    public void BeginNewCategory()
    {
        selectedCategory = null;
        OnPropertyChanged(nameof(SelectedCategory));
        categoryDraftId = Guid.NewGuid();
        CategoryName = string.Empty;
        CategoryMediaType = MediaType.Comic;
        CategoryIsEnabled = true;
        Sources.Clear();
        BeginNewSourceDraft();
        StatusMessage = string.Empty;
    }

    public EditorResult SaveCategory()
    {
        if (categoryDraftId == Guid.Empty) categoryDraftId = SelectedCategory?.Id ?? Guid.NewGuid();
        if (SelectedCategory is not null && SelectedCategory.MediaType != CategoryMediaType
            && database.GetItems(SelectedCategory.Id).Count != 0)
            return SetResult(false, "항목이 등록된 분류의 타입은 변경할 수 없습니다.");

        try
        {
            database.SaveCategory(new Category(categoryDraftId, CategoryName, CategoryMediaType, CategoryIsEnabled));
            var id = categoryDraftId;
            ReloadCategories();
            selectedCategory = null;
            OnPropertyChanged(nameof(SelectedCategory));
            SelectedCategory = Categories.Single(x => x.Id == id);
            return SetResult(true, "분류 설정을 저장했습니다.");
        }
        catch (Exception ex) when (IsExpectedSaveFailure(ex))
        {
            return SetResult(false, $"분류 설정을 저장하지 못했습니다: {ex.Message}");
        }
    }

    public EditorResult BeginNewSource()
    {
        if (SelectedCategory is null)
            return SetResult(false, "먼저 저장된 분류를 선택하세요.");
        BeginNewSourceDraft();
        StatusMessage = string.Empty;
        return new EditorResult(true, string.Empty);
    }

    public EditorResult SaveSource()
    {
        if (SelectedCategory is null)
            return SetResult(false, "먼저 저장된 분류를 선택하세요.");
        if (sourceDraftId == Guid.Empty) sourceDraftId = SelectedSource?.Id ?? Guid.NewGuid();

        try
        {
            var (path, key) = SourcePathRules.NormalizeLocalFolder(SourcePath);
            var candidate = new CategorySource(sourceDraftId, SelectedCategory.Id, path, key,
                SourceIncludeSubdirectories, SourceIsEnabled);

            if (SelectedSource is null)
            {
                var stored = database.AddSource(candidate);
                ReloadSources();
                SelectedSource = Sources.Single(x => x.Id == stored.Id);
                return stored.Id == candidate.Id
                    ? SetResult(true, "소스 폴더를 추가했습니다.")
                    : SetResult(true, "이미 등록된 소스입니다. 기존 설정은 변경하지 않았습니다.");
            }

            database.UpdateSource(candidate);
            ReloadSources();
            SelectedSource = Sources.Single(x => x.Id == candidate.Id);
            return SetResult(true, "소스 폴더 설정을 저장했습니다.");
        }
        catch (Exception ex) when (IsExpectedSaveFailure(ex))
        {
            return SetResult(false, $"소스 폴더 설정을 저장하지 못했습니다: {ex.Message}");
        }
    }

    public EditorResult RemoveSelectedSource()
    {
        if (SelectedSource is null)
            return SetResult(false, "제거할 소스 폴더를 선택하세요.");
        try
        {
            database.RemoveSource(SelectedSource.Id);
            ReloadSources();
            BeginNewSourceDraft();
            return SetResult(true, "소스 폴더 설정을 제거했습니다. 파일과 감상 기록은 변경하지 않았습니다.");
        }
        catch (Exception ex) when (IsExpectedSaveFailure(ex))
        {
            return SetResult(false, $"소스 폴더 설정을 제거하지 못했습니다: {ex.Message}");
        }
    }

    private void ReloadCategories()
    {
        Categories.Clear();
        foreach (var category in database.GetCategories()) Categories.Add(category);
    }

    private void ReloadSources()
    {
        Sources.Clear();
        if (SelectedCategory is not null)
            foreach (var source in database.GetSources(SelectedCategory.Id)) Sources.Add(source);
        selectedSource = null;
        OnPropertyChanged(nameof(SelectedSource));
        BeginNewSourceDraft();
    }

    private void LoadCategoryDraft(Category? category)
    {
        if (category is null)
        {
            categoryDraftId = Guid.Empty;
            CategoryName = string.Empty;
            CategoryMediaType = MediaType.Comic;
            CategoryIsEnabled = true;
            return;
        }
        categoryDraftId = category.Id;
        CategoryName = category.Name;
        CategoryMediaType = category.MediaType;
        CategoryIsEnabled = category.IsEnabled;
        StatusMessage = string.Empty;
    }

    private void LoadSourceDraft(CategorySource? source)
    {
        if (source is null)
        {
            BeginNewSourceDraft();
            return;
        }
        sourceDraftId = source.Id;
        SourcePath = source.RootPath;
        SourceIncludeSubdirectories = source.IncludeSubdirectories;
        SourceIsEnabled = source.IsEnabled;
        StatusMessage = string.Empty;
    }

    private void BeginNewSourceDraft()
    {
        selectedSource = null;
        OnPropertyChanged(nameof(SelectedSource));
        sourceDraftId = Guid.NewGuid();
        SourcePath = string.Empty;
        SourceIncludeSubdirectories = true;
        SourceIsEnabled = true;
    }

    private EditorResult SetResult(bool success, string message)
    {
        StatusMessage = message;
        return new EditorResult(success, message);
    }

    private static bool IsExpectedSaveFailure(Exception ex) =>
        ex is ArgumentException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class SourcePathRules
{
    public static (string Path, string PathKey) NormalizeLocalFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path != path.Trim())
            throw new ArgumentException("소스 경로를 입력하세요.");
        if (path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            throw new ArgumentException("UNC 또는 장치 경로는 지원하지 않습니다.");
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("로컬 드라이브의 절대 경로만 사용할 수 있습니다.");

        string normalized = Path.GetFullPath(path).Replace('/', '\\');
        string? root = Path.GetPathRoot(normalized);
        if (root is null || root.Length != 3 || !char.IsAsciiLetter(root[0]) || root[1] != ':' || root[2] != '\\')
            throw new ArgumentException("로컬 드라이브의 절대 경로만 사용할 수 있습니다.");
        if (normalized.AsSpan(2).Contains(':'))
            throw new ArgumentException("대체 데이터 스트림 경로는 사용할 수 없습니다.");

        foreach (string component in normalized[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
            if (component.EndsWith(' ') || component.EndsWith('.'))
                throw new ArgumentException("경로 구성요소 끝의 공백이나 마침표는 사용할 수 없습니다.");

        if (normalized.Length > root.Length) normalized = normalized.TrimEnd('\\');
        return (normalized, normalized.ToUpperInvariant());
    }
}
