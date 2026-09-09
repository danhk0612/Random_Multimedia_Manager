using System.IO.Compression;
using RandomMultimediaManager.App.Media.Comic;
using RandomMultimediaManager.Core;
using SkiaSharp;

namespace RandomMultimediaManager.Comic.Tests;

internal static class T08Checks
{
    public static void Run(string root)
    {
        PrepareReadyRequiresDecodedStartPage(root);
        CorruptStartPageFailsAndUnlocks(root);
        ProgressAndReadingDirection(root);
        PreparationCancellationDoesNotReplaceActive(root);
        Console.WriteLine("PASS T08 prepared comic checks");
    }

    private static void PrepareReadyRequiresDecodedStartPage(string root)
    {
        string path = Path.Combine(root, "t08-ready.cbz");
        CreateImageArchive(path, 4);

        ComicPrepareResult result = PreparedComic.PrepareAsync(
            path, PlaybackProgress.Comic(2, 0.35), CancellationToken.None).GetAwaiter().GetResult();
        Equal(ComicPrepareStatus.Ready, result.Status, "T08 start page decode produces Ready");
        using PreparedComic prepared = result.Comic!;
        True(prepared.GetCachedPage(2) is not null, "T08 restored start page is decoded before Ready");
        True(!CanOpenExclusive(path), "T08 prepared comic owns archive while active");

        prepared.Dispose();
        True(CanOpenExclusive(path), "T08 prepared comic disposal releases archive file");
    }

    private static void CorruptStartPageFailsAndUnlocks(string root)
    {
        string path = Path.Combine(root, "t08-corrupt.cbz");
        using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("1.png");
            using Stream stream = entry.Open();
            stream.Write([1, 2, 3, 4, 5]);
        }

        ComicPrepareResult result = PreparedComic.PrepareAsync(
            path, PlaybackProgress.Comic(0), CancellationToken.None).GetAwaiter().GetResult();
        Equal(ComicPrepareStatus.DecodeFailed, result.Status, "T08 corrupt start image is not Ready");
        True(result.Comic is null, "T08 corrupt image returns no prepared owner");
        True(CanOpenExclusive(path), "T08 decode failure releases archive file");
    }

    private static void ProgressAndReadingDirection(string root)
    {
        string path = Path.Combine(root, "t08-progress.cbz");
        CreateImageArchive(path, 4);
        ComicPrepareResult result = PreparedComic.PrepareAsync(
            path, PlaybackProgress.Comic(1, 0.4), CancellationToken.None).GetAwaiter().GetResult();
        Equal(ComicPrepareStatus.Ready, result.Status, "T08 progress fixture prepares");

        using var vm = new ComicViewerViewModel();
        vm.Activate(result.Comic!, PlaybackProgress.Comic(1, 0.4));
        Equal(1, vm.CurrentPageIndex, "T08 page progress restored");
        True(Math.Abs(vm.CurrentPageOffset - 0.4) < 0.0001, "T08 vertical offset restored");

        vm.DisplayMode = ComicDisplayMode.TwoPage;
        vm.ReadingDirection = ComicReadingDirection.LeftToRight;
        Equal((1, 2), vm.GetSpreadOrder(), "T08 LTR spread order");
        vm.ReadingDirection = ComicReadingDirection.RightToLeft;
        Equal((2, 1), vm.GetSpreadOrder(), "T08 RTL spread order");

        vm.DisplayMode = ComicDisplayMode.VerticalScroll;
        vm.ScrollVerticalAsync(0.7).GetAwaiter().GetResult();
        PlaybackProgress progress = vm.GetProgress();
        Equal(2, progress.ComicPageIndex!.Value, "T08 vertical scroll crosses page boundary");
        True(progress.ComicPageOffset is >= 0 and <= 1, "T08 returned offset stays normalized");

        vm.Dispose();
        True(CanOpenExclusive(path), "T08 active viewer disposal releases file");
    }

    private static void PreparationCancellationDoesNotReplaceActive(string root)
    {
        string activePath = Path.Combine(root, "t08-active.cbz");
        string cancelledPath = Path.Combine(root, "t08-cancelled.cbz");
        CreateImageArchive(activePath, 2);
        CreateImageArchive(cancelledPath, 2);

        using var vm = new ComicViewerViewModel();
        ComicPrepareResult active = vm.PrepareAsync(activePath, null).GetAwaiter().GetResult();
        Equal(ComicPrepareStatus.Ready, active.Status, "T08 active preparation succeeds");
        vm.Activate(active.Comic!);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        ComicPrepareResult cancelled = vm.PrepareAsync(cancelledPath, null, cts.Token).GetAwaiter().GetResult();
        Equal(ComicPrepareStatus.Cancelled, cancelled.Status, "T08 cancelled preparation is distinct");
        Equal(activePath, vm.Path!, "T08 cancelled preparation preserves active comic");

        vm.Dispose();
        True(CanOpenExclusive(activePath), "T08 cancelled transition still releases active file on dispose");
        True(CanOpenExclusive(cancelledPath), "T08 cancelled preparation leaves no file lock");
    }

    private static void CreateImageArchive(string path, int count)
    {
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        for (int i = 0; i < count; i++)
        {
            using var bitmap = new SKBitmap(64 + i, 80 + i, SKColorType.Bgra8888, SKAlphaType.Premul);
            bitmap.Erase(new SKColor((byte)(20 + i), (byte)(40 + i), (byte)(60 + i)));
            using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 90);
            ZipArchiveEntry entry = archive.CreateEntry($"{i + 1}.png", CompressionLevel.Fastest);
            using Stream stream = entry.Open();
            data.SaveTo(stream);
        }
    }

    private static bool CanOpenExclusive(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static void Equal<T>(T expected, T actual, string name) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}");
        Console.WriteLine($"PASS {name}");
    }

    private static void True(bool value, string name)
    {
        if (!value)
            throw new InvalidOperationException(name);
        Console.WriteLine($"PASS {name}");
    }
}
