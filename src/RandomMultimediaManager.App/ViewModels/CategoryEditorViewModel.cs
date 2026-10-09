using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Scanning;
using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.ViewModels;

public sealed record EditorResult(bool Success, string Message);

public sealed class CategoryEditorViewModel : INotifyPropertyChanged
{
    private readonly LibraryDatabase database;
    private readonly LibraryScanner scanner;
    public ScanCoordinator? Scans { get; }
    private CancellationTokenSource? scanCancellation;
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
    private bool sourceScanOnStartup;
    private SourceRefreshMode sourceRefreshMode = SourceRefreshMode.Manual;
    private int? sourceIntervalHours;
    private SourceRefreshPolicy loadedSourcePolicy = new();
    private string statusMessage = string.Empty;
    private string selectedSourceBindingMessage = "소스를 선택하세요.";
    private string selectedSourceAccessMessage = string.Empty;
    private string scanProgressMessage = string.Empty;
    private bool isScanning;
    private volatile bool uiAttached = true;

    public CategoryEditorViewModel(LibraryDatabase database, ScanCoordinator? scans = null)
    {
        this.database = database;
        Scans = scans;
        scanner = new LibraryScanner(database);
        ReloadCategories();
    }

    public ObservableCollection<Category> Categories { get; } = new();
    public ObservableCollection<CategorySource> Sources { get; } = new();
    public IReadOnlyList<MediaType> MediaTypes { get; } = Enum.GetValues<MediaType>();
    public IReadOnlyList<SourceRefreshMode> RefreshModes => SelectedSource is not null
        && database.GetStorageBinding(SelectedSource.RootPath) is { Kind: StorageKind.Local, RequiresConfirmation: false }
            ? Enum.GetValues<SourceRefreshMode>()
            : [SourceRefreshMode.Manual, SourceRefreshMode.Scheduled];
    public IReadOnlyList<int> RefreshIntervalHours { get; } = Enumerable.Range(1, 168).ToArray();

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
            OnPropertyChanged(nameof(IsSelectedSource));
            LoadSourceDraft(value);
            RefreshSelectedSourceStatus();
            OnPropertyChanged(nameof(RefreshModes));
        }
    }

    public string CategoryName { get => categoryName; set => Set(ref categoryName, value); }
    public MediaType CategoryMediaType { get => categoryMediaType; set => Set(ref categoryMediaType, value); }
    public bool CategoryIsEnabled { get => categoryIsEnabled; set => Set(ref categoryIsEnabled, value); }
    public string SourcePath { get => sourcePath; set => Set(ref sourcePath, value); }
    public bool SourceIncludeSubdirectories { get => sourceIncludeSubdirectories; set => Set(ref sourceIncludeSubdirectories, value); }
    public bool SourceIsEnabled { get => sourceIsEnabled; set => Set(ref sourceIsEnabled, value); }
    public bool SourceScanOnStartup { get => sourceScanOnStartup; set => Set(ref sourceScanOnStartup, value); }
    public SourceRefreshMode SelectedSourceRefreshMode
    {
        get => sourceRefreshMode;
        set
        {
            if (!Set(ref sourceRefreshMode, value)) return;
            OnPropertyChanged(nameof(CanEditSourceInterval));
        }
    }
    public int? SourceIntervalHours { get => sourceIntervalHours; set => Set(ref sourceIntervalHours, value); }
    public string SelectedSourceBindingMessage { get => selectedSourceBindingMessage; private set => Set(ref selectedSourceBindingMessage, value); }
    public string SelectedSourceAccessMessage { get => selectedSourceAccessMessage; private set => Set(ref selectedSourceAccessMessage, value); }
    public bool IsSelectedSource => SelectedSource is not null;
    public bool CanEditSourceInterval => SelectedSourceRefreshMode != SourceRefreshMode.Manual;
    public void SetUiAttached(bool value) => uiAttached = value;
    public string StatusMessage { get => statusMessage; private set => Set(ref statusMessage, value); }
    public string ScanProgressMessage { get => scanProgressMessage; private set => Set(ref scanProgressMessage, value); }
    public bool IsScanning { get => isScanning; private set => Set(ref isScanning, value); }

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
        ScanProgressMessage = string.Empty;
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
            var (path, key) = SourcePathRules.NormalizeFolder(SourcePath);
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

    public async Task<EditorResult> SaveSelectedSourcePolicyAsync()
    {
        if (SelectedSource is null)
            return SetResult(false, "갱신 정책을 저장할 소스를 선택하세요.");
        if (Scans is null)
            return SetResult(false, "소스 갱신 조정자를 사용할 수 없습니다.");

        var source = SelectedSource;
        var previous = database.GetEffectiveSourceRefreshPolicy(source.Id);
        var policy = new SourceRefreshPolicy(SourceScanOnStartup, SelectedSourceRefreshMode,
            SelectedSourceRefreshMode == SourceRefreshMode.Manual ? null : SourceIntervalHours ?? 24,
            previous.LastCompletedAtUtc);
        try
        {
            await Scans.SavePolicyAsync(source.Id, policy);
            loadedSourcePolicy = policy;
            if (uiAttached && SelectedSource?.Id == source.Id)
            {
                RefreshSelectedSourceStatus();
                OnPropertyChanged(nameof(RefreshModes));
            }
            return uiAttached ? SetResult(true, "소스 갱신 정책을 저장했습니다.")
                : new EditorResult(true, "소스 갱신 정책을 저장했습니다.");
        }
        catch (Exception ex) when (IsExpectedSaveFailure(ex) || ex is IOException or UnauthorizedAccessException)
        {
            // The database/coordinator retain the previous policy and watcher state on failure.
            if (uiAttached && SelectedSource?.Id == source.Id && MatchesPolicy(policy)) LoadSourcePolicy(source.Id);
            if (uiAttached) RefreshSelectedSourceStatus();
            string message = $"소스 갱신 정책을 저장하지 못했습니다. 기존 설정을 유지합니다: {ex.Message}";
            return uiAttached ? SetResult(false, message) : new EditorResult(false, message);
        }
    }

    public async Task<EditorResult> ConfirmSelectedBindingAsync()
    {
        if (SelectedSource is null)
            return SetResult(false, "확인할 소스를 선택하세요.");
        if (Scans is null)
            return SetResult(false, "연결 확인 조정자를 사용할 수 없습니다.");
        var source = SelectedSource;
        try
        {
            await Scans.ConfirmBindingAsync(source.Id);
            if (uiAttached && SelectedSource?.Id == source.Id)
            {
                LoadSourcePolicy(source.Id);
                RefreshSelectedSourceStatus();
            }
            return uiAttached ? SetResult(true, "현재 연결 대상을 관찰하고 이 실행에서 명시적으로 확인했습니다.")
                : new EditorResult(true, "현재 연결 대상을 관찰하고 이 실행에서 명시적으로 확인했습니다.");
        }
        catch (Exception ex) when (IsExpectedSaveFailure(ex) || ex is IOException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            if (uiAttached && SelectedSource?.Id == source.Id) RefreshSelectedSourceStatus();
            string message = $"연결 대상을 확인하지 못했습니다. 기존 확인과 감지 상태를 유지합니다: {ex.Message}";
            return uiAttached ? SetResult(false, message) : new EditorResult(false, message);
        }
    }

    public void RefreshSelectedSourceStatus()
    {
        if (SelectedSource is null)
        {
            SelectedSourceBindingMessage = "소스를 선택하세요.";
            SelectedSourceAccessMessage = string.Empty;
            OnPropertyChanged(nameof(RefreshModes));
            return;
        }

        try
        {
            if (MatchesPolicy(loadedSourcePolicy)) LoadSourcePolicy(SelectedSource.Id);
            var binding = database.GetStorageBinding(SelectedSource.RootPath);
            SelectedSourceBindingMessage = binding is null
                ? "저장된 연결 대상: 없음"
                : $"저장된 연결 대상: {BindingKindLabel(binding.Kind)} · {binding.ExpectedTarget ?? "대상 정보 미확인"} · revision {binding.Revision}";
            var access = Scans?.GetAccess(SelectedSource.Id) ?? SourceAccessState.Unknown;
            SelectedSourceAccessMessage = $"이번 실행의 마지막 관찰/접근 상태: {AccessLabel(access)}";
            OnPropertyChanged(nameof(RefreshModes));
        }
        catch (Exception ex) when (IsExpectedSaveFailure(ex) || ex is ObjectDisposedException)
        {
            SelectedSourceBindingMessage = "저장된 연결 대상: 확인할 수 없음";
            SelectedSourceAccessMessage = "이번 실행의 마지막 관찰/접근 상태: 확인할 수 없음";
            OnPropertyChanged(nameof(RefreshModes));
        }
    }

    public Task<EditorResult> ScanCompletion { get; private set; } = Task.FromResult(new EditorResult(true, ""));
    public Task<EditorResult> ScanSelectedCategoryAsync()
    {
        if (IsScanning) return ScanCompletion;
        return ScanCompletion = ScanCoreAsync();
    }
    private async Task<EditorResult> ScanCoreAsync()
    {
        if (SelectedCategory is null)
            return SetResult(false, "스캔할 저장된 분류를 선택하세요.");
        if (IsScanning)
            return SetResult(false, "이미 스캔 중입니다.");

        IsScanning = true;
        scanCancellation = new CancellationTokenSource();
        Category category = SelectedCategory;
        ScanProgressMessage = "저장된 활성 소스 폴더를 확인하고 있습니다.";
        var owner = scanCancellation;
        var progress = new Progress<LibraryScanProgress>(x =>
        {
            // Progress<T> posts to the dispatcher; ownership must also be checked on delivery.
            if (IsScanning && ReferenceEquals(scanCancellation, owner) && !owner.IsCancellationRequested)
                ScanProgressMessage = x.Message;
        });
        try
        {
            LibraryScanResult result = Scans is null
                ? await scanner.ScanCategoryAsync(category, progress, scanCancellation.Token)
                : await Scans.ScanCategoryAsync(category.Id, progress, scanCancellation.Token);
            if (result.Status == LibraryScanStatus.Cancelled)
            {
                ScanProgressMessage = "스캔을 취소했습니다. 이미 완료한 소스의 반영은 유지됩니다.";
                return SetResult(false, ScanProgressMessage);
            }

            string warning = result.WarningCount == 0 ? string.Empty : $", 경고 {result.WarningCount}건";
            ScanProgressMessage = $"스캔 완료: 미디어 {result.PresentCount}개 반영, Missing {result.MissingCount}개{warning}.";
            if (result.WarningCount != 0)
                ScanProgressMessage += $" 첫 경고: {result.Warnings[0]}";
            RefreshSelectedSourceStatus();
            return SetResult(true, ScanProgressMessage);
        }
        catch (Exception ex) when (IsExpectedScanFailure(ex))
        {
            ScanProgressMessage = $"스캔 결과를 저장하지 못했습니다: {ex.Message}";
            return SetResult(false, ScanProgressMessage);
        }
        finally
        {
            scanCancellation?.Dispose();
            scanCancellation = null;
            IsScanning = false;
        }
    }

    public async Task<EditorResult> EditAsync(Func<EditorResult> edit)
    {
        if (Scans is null) return edit();
        using var admission = await Scans.EnterExclusiveAsync();
        if (admission is null || Scans.IsClosing || database.IsDisposed) return SetResult(false, "종료 중에는 편집할 수 없습니다.");
        var result = edit();
        if (result.Success) Scans.ConfigurationChanged();
        return result;
    }

    public void CancelScan() => scanCancellation?.Cancel();

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
        OnPropertyChanged(nameof(IsSelectedSource));
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
            ScanProgressMessage = string.Empty;
            return;
        }
        categoryDraftId = category.Id;
        CategoryName = category.Name;
        CategoryMediaType = category.MediaType;
        CategoryIsEnabled = category.IsEnabled;
        StatusMessage = string.Empty;
        ScanProgressMessage = string.Empty;
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
        LoadSourcePolicy(source.Id);
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
        SourceScanOnStartup = false;
        SelectedSourceRefreshMode = SourceRefreshMode.Manual;
        SourceIntervalHours = 24;
        loadedSourcePolicy = new();
        SelectedSourceBindingMessage = "저장 후 연결 대상을 확인할 수 있습니다.";
        SelectedSourceAccessMessage = "이번 실행의 마지막 관찰/접근 상태: 미관찰";
    }

    private void LoadSourcePolicy(Guid sourceId)
    {
        var policy = database.GetEffectiveSourceRefreshPolicy(sourceId);
        loadedSourcePolicy = policy;
        SourceScanOnStartup = policy.ScanOnStartup;
        SelectedSourceRefreshMode = policy.RefreshMode;
        SourceIntervalHours = policy.IntervalHours ?? 24;
    }

    private bool MatchesPolicy(SourceRefreshPolicy policy) => SourceScanOnStartup == policy.ScanOnStartup
        && SelectedSourceRefreshMode == policy.RefreshMode
        && (policy.RefreshMode == SourceRefreshMode.Manual || SourceIntervalHours == policy.IntervalHours);

    private static string BindingKindLabel(StorageKind kind) => kind switch
    {
        StorageKind.Local => "로컬",
        StorageKind.RemoteMapped => "매핑 드라이브",
        StorageKind.RemoteUNC => "UNC",
        _ => "가상/불명"
    };

    private static string AccessLabel(SourceAccessState state) => state switch
    {
        SourceAccessState.Available => "접근 가능",
        SourceAccessState.Unavailable => "연결 불가",
        SourceAccessState.AccessDenied => "접근 거부",
        SourceAccessState.BindingChanged => "연결 대상 변경 감지",
        SourceAccessState.Unconfirmed => "명시적 확인 필요",
        _ => "미확인"
    };

    private EditorResult SetResult(bool success, string message)
    {
        StatusMessage = message;
        return new EditorResult(success, message);
    }

    private static bool IsExpectedSaveFailure(Exception ex) =>
        ex is ArgumentException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException;

    private static bool IsExpectedScanFailure(Exception ex) =>
        ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException
        or Microsoft.Data.Sqlite.SqliteException;

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
    public static (string Path, string PathKey) NormalizeFolder(string path)
    {
        var normalized = WindowsPath.Normalize(path);
        return (normalized.Path, normalized.PathKey);
    }

}
