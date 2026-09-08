using System.IO.Compression;
using System.Text;
using RandomMultimediaManager.App.Media.Comic;

namespace RandomMultimediaManager.Comic.Tests;

internal static class Program
{
    private static int _passed;

    public static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "RandomMultimediaManager-T07-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            NaturalSortAndPageRead(root);
            EmptyArchive(root);
            NoImageEntries(root);
            CorruptArchive(root);
            EncryptedArchive(root);
            InvalidPage(root);
            CancellationAndOwnership(root);

            Console.WriteLine($"T07 comic archive checks passed: {_passed}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void NaturalSortAndPageRead(string root)
    {
        string path = Path.Combine(root, "sort.cbz");
        CreateZip(path,
        [
            ("10.jpg", "10"),
            ("2.jpg", "2"),
            ("1.jpg", "1"),
            ("Folder10/1.PNG", "f10"),
            ("folder2/10.jpg", "f2-10"),
            ("folder2/2.jpg", "f2-2"),
            ("folder2/001.jpg", "f2-001"),
            ("folder2/01.jpg", "f2-01"),
            ("folder2/1.jpg", "f2-1"),
            ("folder2/A2.webp", "A2"),
            ("folder2/a10.WEBP", "a10"),
            ("notes.txt", "ignore")
        ]);

        ComicArchiveOpenResult open = ComicArchive.Open(path);
        Equal(ComicArchiveOpenStatus.Opened, open.Status, "sort archive opens");
        using ComicArchive archive = open.Archive!;

        string[] actual = archive.Pages.Select(page => page.EntryPath).ToArray();
        string[] expected =
        [
            "1.jpg",
            "2.jpg",
            "10.jpg",
            "folder2/1.jpg",
            "folder2/01.jpg",
            "folder2/001.jpg",
            "folder2/2.jpg",
            "folder2/10.jpg",
            "folder2/A2.webp",
            "folder2/a10.WEBP",
            "Folder10/1.PNG"
        ];
        SequenceEqual(expected, actual, "natural path sort covers 1/2/10, folders, digits, case");

        ComicPageOpenResult page = archive.OpenPage(7);
        Equal(ComicPageOpenStatus.Opened, page.Status, "requested page stream opens");
        using Stream stream = page.Stream!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        Equal("f2-10", reader.ReadToEnd(), "requested page reads only its entry stream");
    }

    private static void EmptyArchive(string root)
    {
        string path = Path.Combine(root, "empty.zip");
        using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create)) { }

        ComicArchiveOpenResult open = ComicArchive.Open(path);
        Equal(ComicArchiveOpenStatus.EmptyArchive, open.Status, "empty archive is distinct");
        True(open.Archive is null, "empty archive returns no owner");
    }

    private static void NoImageEntries(string root)
    {
        string path = Path.Combine(root, "no-images.zip");
        CreateZip(path, [("docs/readme.txt", "text"), ("folder/", "")]);

        ComicArchiveOpenResult open = ComicArchive.Open(path);
        Equal(ComicArchiveOpenStatus.NoImageEntries, open.Status, "archive without image entries is distinct");
    }

    private static void CorruptArchive(string root)
    {
        string path = Path.Combine(root, "corrupt.cbz");
        File.WriteAllBytes(path, [0x50, 0x4b, 0x03, 0x04, 0x01, 0x02, 0x03]);

        ComicArchiveOpenResult open = ComicArchive.Open(path);
        Equal(ComicArchiveOpenStatus.CorruptArchive, open.Status, "corrupt archive is distinct");
    }

    private static void EncryptedArchive(string root)
    {
        string path = Path.Combine(root, "encrypted.zip");
        CreateZip(path, [("1.jpg", "payload")]);
        MarkFirstEntryEncrypted(path);

        ComicArchiveOpenResult open = ComicArchive.Open(path);
        Equal(ComicArchiveOpenStatus.UnsupportedEncryption, open.Status, "encrypted archive is distinct");
        True(open.Archive is null, "encrypted archive returns no owner");
    }

    private static void InvalidPage(string root)
    {
        string path = Path.Combine(root, "invalid-page.zip");
        CreateZip(path, [("1.jpg", "one")]);

        ComicArchiveOpenResult open = ComicArchive.Open(path);
        Equal(ComicArchiveOpenStatus.Opened, open.Status, "invalid-page fixture opens");
        using ComicArchive archive = open.Archive!;

        Equal(ComicPageOpenStatus.InvalidPage, archive.OpenPage(-1).Status, "negative page rejected");
        Equal(ComicPageOpenStatus.InvalidPage, archive.OpenPage(1).Status, "past-end page rejected");
    }

    private static void CancellationAndOwnership(string root)
    {
        string path = Path.Combine(root, "ownership.cbz");
        CreateZip(path, [("1.jpg", new string('x', 4096)), ("2.png", "two")]);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Equal(ComicArchiveOpenStatus.Cancelled, ComicArchive.Open(path, cancelled.Token).Status,
            "pre-cancelled archive open is cancelled");
        AssertExclusiveOpen(path, "cancelled archive open releases file");

        ComicArchiveOpenResult open = ComicArchive.Open(path);
        Equal(ComicArchiveOpenStatus.Opened, open.Status, "ownership fixture opens");
        ComicArchive archive = open.Archive!;

        True(!CanOpenExclusive(path), "archive owner holds source file while active");

        ComicPageOpenResult cancelledPage = archive.OpenPage(0, cancelled.Token);
        Equal(ComicPageOpenStatus.Cancelled, cancelledPage.Status, "pre-cancelled page request is cancelled");

        ComicPageOpenResult page = archive.OpenPage(0);
        Equal(ComicPageOpenStatus.Opened, page.Status, "page stream opens for ownership check");
        Stream stream = page.Stream!;
        stream.Dispose();
        True(!CanOpenExclusive(path), "disposing page stream keeps archive ownership only");

        ComicPageOpenResult stillOpenPage = archive.OpenPage(1);
        Equal(ComicPageOpenStatus.Opened, stillOpenPage.Status, "second page stream opens");
        Stream ownedStream = stillOpenPage.Stream!;

        archive.Dispose();
        True(!ownedStream.CanRead, "disposing archive disposes outstanding page streams");
        AssertExclusiveOpen(path, "disposing archive releases source file lock");

        Equal(ComicPageOpenStatus.Failed, archive.OpenPage(0).Status, "disposed archive rejects page request");
    }

    private static void CreateZip(string path, IEnumerable<(string Name, string Content)> entries)
    {
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, string content) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Fastest);
            if (name.EndsWith('/'))
                continue;

            using Stream stream = entry.Open();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: false);
            writer.Write(content);
        }
    }

    private static void MarkFirstEntryEncrypted(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int local = FindSignature(bytes, 0x04034b50);
        int central = FindSignature(bytes, 0x02014b50);
        if (local < 0 || central < 0)
            throw new InvalidOperationException("ZIP fixture signatures missing.");

        ushort localFlags = BitConverter.ToUInt16(bytes, local + 6);
        ushort centralFlags = BitConverter.ToUInt16(bytes, central + 8);
        BitConverter.GetBytes((ushort)(localFlags | 0x0001)).CopyTo(bytes, local + 6);
        BitConverter.GetBytes((ushort)(centralFlags | 0x0001)).CopyTo(bytes, central + 8);
        File.WriteAllBytes(path, bytes);
    }

    private static int FindSignature(byte[] bytes, uint signature)
    {
        byte[] target = BitConverter.GetBytes(signature);
        for (int i = 0; i <= bytes.Length - target.Length; i++)
        {
            if (bytes.AsSpan(i, target.Length).SequenceEqual(target))
                return i;
        }
        return -1;
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

    private static void AssertExclusiveOpen(string path, string name)
    {
        True(CanOpenExclusive(path), name);
    }

    private static void Equal<T>(T expected, T actual, string name) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}");
        Pass(name);
    }

    private static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string name)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException(
                $"{name}: expected [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}]");
        Pass(name);
    }

    private static void True(bool value, string name)
    {
        if (!value)
            throw new InvalidOperationException(name);
        Pass(name);
    }

    private static void Pass(string name)
    {
        _passed++;
        Console.WriteLine($"PASS {name}");
    }
}
