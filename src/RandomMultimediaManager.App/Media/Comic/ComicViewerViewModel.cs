using System.IO;
using RandomMultimediaManager.Core;
using SkiaSharp;

namespace RandomMultimediaManager.App.Media.Comic;

public enum ComicDisplayMode { SinglePage, TwoPage, VerticalScroll }
public enum ComicReadingDirection { LeftToRight, RightToLeft }
public enum ComicFitMode { Original, FitWindow, FitWidth, FitHeight, Custom }
public enum ComicPrepareStatus { Ready, Cancelled, ArchiveFailed, DecodeFailed }

public sealed record ComicPrepareResult(
    ComicPrepareStatus Status,
    PreparedComic? Comic = null,
    string? Error = null);

public sealed class ComicViewerViewModel : IDisposable
{
    private CancellationTokenSource? _prepareCancellation;
    private long _prepareGeneration;
    private PreparedComic? _activeComic;

    public ComicDisplayMode DisplayMode { get; set; } = ComicDisplayMode.SinglePage;
    public ComicReadingDirection ReadingDirection { get; set; } = ComicReadingDirection.LeftToRight;
    public ComicFitMode FitMode { get; set; } = ComicFitMode.FitWindow;
    public double Zoom { get; private set; } = 1.0;
    public double PanX { get; set; }
    public double PanY { get; set; }
    public int CurrentPageIndex { get; private set; }
    public double CurrentPageOffset { get; private set; }
    public int PageCount => _activeComic?.PageCount ?? 0;
    public string? Path => _activeComic?.Path;
    public bool IsReady => _activeComic is not null;

    public async Task<ComicPrepareResult> PrepareAsync(
        string path,
        PlaybackProgress? progress,
        CancellationToken cancellationToken = default)
    {
        long generation = Interlocked.Increment(ref _prepareGeneration);
        CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationTokenSource? previous = Interlocked.Exchange(ref _prepareCancellation, linked);
        previous?.Cancel();
        previous?.Dispose();

        try
        {
            ComicPrepareResult result = await PreparedComic.PrepareAsync(path, progress, linked.Token);
            if (generation != Volatile.Read(ref _prepareGeneration) || linked.IsCancellationRequested)
            {
                result.Comic?.Dispose();
                return new(ComicPrepareStatus.Cancelled);
            }
            return result;
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _prepareCancellation, null, linked), linked))
                linked.Dispose();
        }
    }

    public void Activate(PreparedComic prepared, PlaybackProgress? progress = null)
    {
        PreparedComic? old = Interlocked.Exchange(ref _activeComic, prepared);
        old?.Dispose();

        int page = progress?.MediaType == MediaType.Comic && progress.ComicPageIndex is int requested
            ? Math.Clamp(requested, 0, Math.Max(0, prepared.PageCount - 1))
            : 0;
        CurrentPageIndex = page;
        CurrentPageOffset = progress?.MediaType == MediaType.Comic
            ? Math.Clamp(progress.ComicPageOffset ?? 0, 0, 1)
            : 0;
        ResetTransform();
        prepared.QueueAdjacentPreload(CurrentPageIndex);
    }

    public SKBitmap? GetPage(int index) => _activeComic?.GetCachedPage(index);

    public async Task<bool> EnsurePageAsync(int index, CancellationToken cancellationToken = default)
    {
        PreparedComic? comic = _activeComic;
        if (comic is null || index < 0 || index >= comic.PageCount)
            return false;
        return await comic.EnsurePageAsync(index, cancellationToken);
    }

    public async Task<bool> MoveAsync(int delta, CancellationToken cancellationToken = default)
    {
        if (_activeComic is null || delta == 0)
            return false;

        int step = DisplayMode == ComicDisplayMode.TwoPage ? 2 : 1;
        int target = Math.Clamp(CurrentPageIndex + (delta > 0 ? step : -step), 0, PageCount - 1);
        if (target == CurrentPageIndex)
            return false;

        if (!await EnsurePageAsync(target, cancellationToken))
            return false;

        CurrentPageIndex = target;
        CurrentPageOffset = 0;
        ResetPanForNavigation();
        _activeComic.QueueAdjacentPreload(CurrentPageIndex);
        if (DisplayMode == ComicDisplayMode.TwoPage)
            _ = EnsurePageAsync(GetSecondPageIndex(), cancellationToken);
        return true;
    }

    public int GetSecondPageIndex()
    {
        if (DisplayMode != ComicDisplayMode.TwoPage || PageCount == 0)
            return -1;
        int second = CurrentPageIndex + 1;
        return second < PageCount ? second : -1;
    }

    public (int Left, int Right) GetSpreadOrder()
    {
        int first = CurrentPageIndex;
        int second = GetSecondPageIndex();
        if (ReadingDirection == ComicReadingDirection.RightToLeft)
            return (second, first);
        return (first, second);
    }

    public async Task ScrollVerticalAsync(double delta, CancellationToken cancellationToken = default)
    {
        if (DisplayMode != ComicDisplayMode.VerticalScroll || PageCount == 0)
            return;

        CurrentPageOffset += delta;
        while (CurrentPageOffset >= 1 && CurrentPageIndex < PageCount - 1)
        {
            if (!await EnsurePageAsync(CurrentPageIndex + 1, cancellationToken))
            {
                CurrentPageOffset = 0.999;
                break;
            }
            CurrentPageOffset -= 1;
            CurrentPageIndex++;
            _activeComic?.QueueAdjacentPreload(CurrentPageIndex);
        }
        while (CurrentPageOffset < 0 && CurrentPageIndex > 0)
        {
            if (!await EnsurePageAsync(CurrentPageIndex - 1, cancellationToken))
            {
                CurrentPageOffset = 0;
                break;
            }
            CurrentPageIndex--;
            CurrentPageOffset += 1;
            _activeComic?.QueueAdjacentPreload(CurrentPageIndex);
        }
        CurrentPageOffset = Math.Clamp(CurrentPageOffset, 0, 1);
    }

    public void SetFitMode(ComicFitMode mode)
    {
        FitMode = mode;
        if (mode != ComicFitMode.Custom)
        {
            Zoom = 1.0;
            PanX = 0;
            PanY = 0;
        }
    }

    public void AdjustZoom(int wheelDelta)
    {
        FitMode = ComicFitMode.Custom;
        double factor = wheelDelta > 0 ? 1.1 : 1 / 1.1;
        Zoom = Math.Clamp(Zoom * factor, 0.1, 8.0);
    }

    public PlaybackProgress GetProgress() =>
        PlaybackProgress.Comic(CurrentPageIndex, Math.Clamp(CurrentPageOffset, 0, 1));

    public void Dispose()
    {
        Interlocked.Increment(ref _prepareGeneration);
        CancellationTokenSource? prepare = Interlocked.Exchange(ref _prepareCancellation, null);
        prepare?.Cancel();
        prepare?.Dispose();
        Interlocked.Exchange(ref _activeComic, null)?.Dispose();
    }

    private void ResetTransform()
    {
        Zoom = 1.0;
        PanX = 0;
        PanY = 0;
    }

    private void ResetPanForNavigation()
    {
        PanX = 0;
        PanY = 0;
    }
}

public sealed class PreparedComic : IDisposable
{
    public const int PreloadBefore = 1;
    public const int PreloadAfter = 2;
    public const long MaxCacheBytes = 256L * 1024 * 1024;

    private readonly ComicArchive _archive;
    private readonly object _sync = new();
    private readonly Dictionary<int, CacheEntry> _cache = [];
    private readonly LinkedList<int> _lru = [];
    private readonly CancellationTokenSource _lifetime = new();
    private long _cacheBytes;
    private bool _disposed;

    private PreparedComic(string path, ComicArchive archive)
    {
        Path = path;
        _archive = archive;
    }

    public string Path { get; }
    public int PageCount => _archive.Pages.Count;

    public static async Task<ComicPrepareResult> PrepareAsync(
        string path,
        PlaybackProgress? progress,
        CancellationToken cancellationToken)
    {
        ComicArchiveOpenResult open;
        try
        {
            open = await Task.Run(() => ComicArchive.Open(path, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new(ComicPrepareStatus.Cancelled);
        }

        if (open.Status == ComicArchiveOpenStatus.Cancelled)
            return new(ComicPrepareStatus.Cancelled);
        if (open.Status != ComicArchiveOpenStatus.Opened || open.Archive is null)
            return new(ComicPrepareStatus.ArchiveFailed, Error: open.Error ?? open.Status.ToString());

        var prepared = new PreparedComic(path, open.Archive);
        int startPage = progress?.MediaType == MediaType.Comic && progress.ComicPageIndex is int page
            ? Math.Clamp(page, 0, prepared.PageCount - 1)
            : 0;

        try
        {
            if (!await prepared.EnsurePageAsync(startPage, cancellationToken))
            {
                prepared.Dispose();
                return new(ComicPrepareStatus.DecodeFailed, Error: $"페이지 {startPage + 1} 디코딩에 실패했습니다.");
            }
            return new(ComicPrepareStatus.Ready, prepared);
        }
        catch (OperationCanceledException)
        {
            prepared.Dispose();
            return new(ComicPrepareStatus.Cancelled);
        }
    }

    public SKBitmap? GetCachedPage(int index)
    {
        lock (_sync)
        {
            if (!_cache.TryGetValue(index, out CacheEntry? entry))
                return null;
            Touch(index, entry);
            return entry.Bitmap;
        }
    }

    public async Task<bool> EnsurePageAsync(int index, CancellationToken cancellationToken = default)
    {
        if ((uint)index >= (uint)PageCount)
            return false;

        lock (_sync)
        {
            ThrowIfDisposed();
            if (_cache.TryGetValue(index, out CacheEntry? existing))
            {
                Touch(index, existing);
                return true;
            }
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        SKBitmap? bitmap = await Task.Run(() => Decode(index, linked.Token), linked.Token);
        if (bitmap is null)
            return false;

        lock (_sync)
        {
            if (_disposed || linked.IsCancellationRequested)
            {
                bitmap.Dispose();
                return false;
            }
            if (_cache.TryGetValue(index, out CacheEntry? winner))
            {
                bitmap.Dispose();
                Touch(index, winner);
                return true;
            }

            long bytes = EstimateBytes(bitmap);
            LinkedListNode<int> node = _lru.AddFirst(index);
            _cache[index] = new CacheEntry(bitmap, bytes, node);
            _cacheBytes += bytes;
            Trim(index);
            return true;
        }
    }

    public void QueueAdjacentPreload(int currentPage)
    {
        for (int i = currentPage - PreloadBefore; i <= currentPage + PreloadAfter; i++)
        {
            if (i < 0 || i >= PageCount || i == currentPage)
                continue;
            _ = PreloadSafeAsync(i);
        }
    }

    public void Dispose()
    {
        CacheEntry[] entries;
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _lifetime.Cancel();
            entries = _cache.Values.ToArray();
            _cache.Clear();
            _lru.Clear();
            _cacheBytes = 0;
        }

        foreach (CacheEntry entry in entries)
            entry.Bitmap.Dispose();
        _archive.Dispose();
        _lifetime.Dispose();
    }

    private async Task PreloadSafeAsync(int index)
    {
        try { await EnsurePageAsync(index, _lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
    }

    private SKBitmap? Decode(int index, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ComicPageOpenResult page = _archive.OpenPage(index, cancellationToken);
        if (page.Status != ComicPageOpenStatus.Opened || page.Stream is null)
            return null;

        using Stream stream = page.Stream;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return SKBitmap.Decode(stream);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException)
        {
            return null;
        }
    }

    private void Touch(int index, CacheEntry entry)
    {
        _lru.Remove(entry.Node);
        entry.Node = _lru.AddFirst(index);
    }

    private void Trim(int protectedIndex)
    {
        while (_cacheBytes > MaxCacheBytes && _lru.Last is LinkedListNode<int> tail)
        {
            if (tail.Value == protectedIndex && _cache.Count == 1)
                break;
            int index = tail.Value;
            if (index == protectedIndex)
            {
                _lru.Remove(tail);
                _lru.AddFirst(tail);
                continue;
            }
            _lru.Remove(tail);
            if (_cache.Remove(index, out CacheEntry? entry))
            {
                _cacheBytes -= entry.Bytes;
                entry.Bitmap.Dispose();
            }
        }
    }

    private static long EstimateBytes(SKBitmap bitmap) =>
        Math.Max(1L, (long)bitmap.RowBytes * bitmap.Height);

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PreparedComic));
    }

    private sealed class CacheEntry(SKBitmap bitmap, long bytes, LinkedListNode<int> node)
    {
        public SKBitmap Bitmap { get; } = bitmap;
        public long Bytes { get; } = bytes;
        public LinkedListNode<int> Node { get; set; } = node;
    }
}
