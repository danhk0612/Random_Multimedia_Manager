using System.Buffers.Binary;
using System.IO.Compression;

namespace RandomMultimediaManager.App.Media.Comic;

public enum ComicArchiveOpenStatus
{
    Opened,
    EmptyArchive,
    NoImageEntries,
    UnsupportedEncryption,
    CorruptArchive,
    Cancelled,
    Failed
}

public enum ComicPageOpenStatus
{
    Opened,
    InvalidPage,
    Cancelled,
    Failed
}

public sealed record ComicArchivePage(int Index, string EntryPath, long Length);

public sealed record ComicArchiveOpenResult(
    ComicArchiveOpenStatus Status,
    ComicArchive? Archive = null,
    string? Error = null);

public sealed record ComicPageOpenResult(
    ComicPageOpenStatus Status,
    Stream? Stream = null,
    string? Error = null);

public sealed class ComicArchive : IDisposable
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif"
    };

    private readonly object _sync = new();
    private readonly FileStream _file;
    private readonly ZipArchive _archive;
    private readonly ZipArchiveEntry[] _entries;
    private readonly HashSet<TrackedPageStream> _openPageStreams = [];
    private bool _disposed;

    private ComicArchive(FileStream file, ZipArchive archive, ZipArchiveEntry[] entries)
    {
        _file = file;
        _archive = archive;
        _entries = entries;
        Pages = entries
            .Select((entry, index) => new ComicArchivePage(index, entry.FullName, entry.Length))
            .ToArray();
    }

    public IReadOnlyList<ComicArchivePage> Pages { get; }

    public static ComicArchiveOpenResult Open(string path, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return new(ComicArchiveOpenStatus.Cancelled);

        FileStream? file = null;
        ZipArchive? archive = null;

        try
        {
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);

            if (cancellationToken.IsCancellationRequested)
                return DisposeAndReturn(file, archive, ComicArchiveOpenStatus.Cancelled);

            if (archive.Entries.Count == 0)
                return DisposeAndReturn(file, archive, ComicArchiveOpenStatus.EmptyArchive);

            if (ZipEncryptionInspector.HasEncryptedEntries(file))
                return DisposeAndReturn(file, archive, ComicArchiveOpenStatus.UnsupportedEncryption);

            var imageEntries = new List<ZipArchiveEntry>();
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (cancellationToken.IsCancellationRequested)
                    return DisposeAndReturn(file, archive, ComicArchiveOpenStatus.Cancelled);

                if (entry.Name.Length == 0)
                    continue;

                if (ImageExtensions.Contains(Path.GetExtension(entry.Name)))
                    imageEntries.Add(entry);
            }

            if (imageEntries.Count == 0)
                return DisposeAndReturn(file, archive, ComicArchiveOpenStatus.NoImageEntries);

            imageEntries.Sort((left, right) => NaturalPathComparer.Instance.Compare(left.FullName, right.FullName));
            ComicArchive result = new(file, archive, imageEntries.ToArray());
            file = null;
            archive = null;
            return new(ComicArchiveOpenStatus.Opened, result);
        }
        catch (InvalidDataException ex)
        {
            archive?.Dispose();
            file?.Dispose();
            return new(ComicArchiveOpenStatus.CorruptArchive, Error: ex.Message);
        }
        catch (OperationCanceledException)
        {
            archive?.Dispose();
            file?.Dispose();
            return new(ComicArchiveOpenStatus.Cancelled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            archive?.Dispose();
            file?.Dispose();
            return new(ComicArchiveOpenStatus.Failed, Error: ex.Message);
        }
    }

    public ComicPageOpenResult OpenPage(int pageIndex, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return new(ComicPageOpenStatus.Cancelled);

        lock (_sync)
        {
            if (_disposed)
                return new(ComicPageOpenStatus.Failed, Error: "The comic archive is disposed.");

            if ((uint)pageIndex >= (uint)_entries.Length)
                return new(ComicPageOpenStatus.InvalidPage);

            Stream? raw = null;
            try
            {
                raw = _entries[pageIndex].Open();
                if (cancellationToken.IsCancellationRequested)
                {
                    raw.Dispose();
                    return new(ComicPageOpenStatus.Cancelled);
                }

                var tracked = new TrackedPageStream(raw, this);
                _openPageStreams.Add(tracked);
                return new(ComicPageOpenStatus.Opened, tracked);
            }
            catch (InvalidDataException ex)
            {
                raw?.Dispose();
                return new(ComicPageOpenStatus.Failed, Error: ex.Message);
            }
            catch (IOException ex)
            {
                raw?.Dispose();
                return new(ComicPageOpenStatus.Failed, Error: ex.Message);
            }
        }
    }

    public void Dispose()
    {
        TrackedPageStream[] streams;
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            streams = _openPageStreams.ToArray();
            _openPageStreams.Clear();
        }

        foreach (TrackedPageStream stream in streams)
            stream.DisposeFromOwner();

        _archive.Dispose();
        _file.Dispose();
    }

    private void Release(TrackedPageStream stream)
    {
        lock (_sync)
            _openPageStreams.Remove(stream);
    }

    private static ComicArchiveOpenResult DisposeAndReturn(
        FileStream file,
        ZipArchive archive,
        ComicArchiveOpenStatus status)
    {
        archive.Dispose();
        file.Dispose();
        return new(status);
    }

    private sealed class TrackedPageStream(Stream inner, ComicArchive owner) : Stream
    {
        private Stream? _inner = inner;
        private ComicArchive? _owner = owner;

        private Stream Inner => _inner ?? throw new ObjectDisposedException(nameof(TrackedPageStream));

        public override bool CanRead => _inner?.CanRead ?? false;
        public override bool CanSeek => _inner?.CanSeek ?? false;
        public override bool CanWrite => false;
        public override long Length => Inner.Length;
        public override long Position { get => Inner.Position; set => Inner.Position = value; }
        public override void Flush() => Inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => Inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => Inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => Inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                DisposeCore();
            base.Dispose(disposing);
        }

        internal void DisposeFromOwner() => DisposeCore();

        private void DisposeCore()
        {
            Stream? stream = Interlocked.Exchange(ref _inner, null);
            ComicArchive? currentOwner = Interlocked.Exchange(ref _owner, null);
            if (stream is null)
                return;

            try
            {
                stream.Dispose();
            }
            finally
            {
                currentOwner?.Release(this);
            }
        }
    }

    private sealed class NaturalPathComparer : IComparer<string>
    {
        public static readonly NaturalPathComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            string[] left = x.Replace('\\', '/').Split('/');
            string[] right = y.Replace('\\', '/').Split('/');
            int componentCount = Math.Min(left.Length, right.Length);

            for (int i = 0; i < componentCount; i++)
            {
                int component = CompareComponent(left[i], right[i]);
                if (component != 0)
                    return component;
            }

            int count = left.Length.CompareTo(right.Length);
            return count != 0 ? count : StringComparer.Ordinal.Compare(x, y);
        }

        private static int CompareComponent(string left, string right)
        {
            int li = 0;
            int ri = 0;

            while (li < left.Length && ri < right.Length)
            {
                if (char.IsDigit(left[li]) && char.IsDigit(right[ri]))
                {
                    int number = CompareNumber(left, ref li, right, ref ri);
                    if (number != 0)
                        return number;
                    continue;
                }

                int insensitive = char.ToUpperInvariant(left[li]).CompareTo(char.ToUpperInvariant(right[ri]));
                if (insensitive != 0)
                    return insensitive;

                li++;
                ri++;
            }

            int remaining = (left.Length - li).CompareTo(right.Length - ri);
            return remaining != 0 ? remaining : StringComparer.Ordinal.Compare(left, right);
        }

        private static int CompareNumber(string left, ref int li, string right, ref int ri)
        {
            int leftStart = li;
            int rightStart = ri;

            while (li < left.Length && char.IsDigit(left[li])) li++;
            while (ri < right.Length && char.IsDigit(right[ri])) ri++;

            int leftSignificant = leftStart;
            int rightSignificant = rightStart;
            while (leftSignificant < li - 1 && left[leftSignificant] == '0') leftSignificant++;
            while (rightSignificant < ri - 1 && right[rightSignificant] == '0') rightSignificant++;

            int leftDigits = li - leftSignificant;
            int rightDigits = ri - rightSignificant;
            int length = leftDigits.CompareTo(rightDigits);
            if (length != 0)
                return length;

            for (int i = 0; i < leftDigits; i++)
            {
                int digit = left[leftSignificant + i].CompareTo(right[rightSignificant + i]);
                if (digit != 0)
                    return digit;
            }

            int leftRunLength = li - leftStart;
            int rightRunLength = ri - rightStart;
            return leftRunLength.CompareTo(rightRunLength);
        }
    }

    private static class ZipEncryptionInspector
    {
        private const uint EndOfCentralDirectorySignature = 0x06054b50;
        private const uint CentralDirectorySignature = 0x02014b50;

        public static bool HasEncryptedEntries(Stream stream)
        {
            if (!stream.CanSeek || stream.Length < 22)
                return false;

            long originalPosition = stream.Position;
            try
            {
                long searchLength = Math.Min(stream.Length, 65_557);
                byte[] tail = new byte[searchLength];
                stream.Position = stream.Length - searchLength;
                ReadExactly(stream, tail);

                int eocd = FindSignatureFromEnd(tail, EndOfCentralDirectorySignature);
                if (eocd < 0 || eocd + 22 > tail.Length)
                    return false;

                ushort totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(eocd + 10, 2));
                uint centralOffset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocd + 16, 4));
                if (totalEntries == ushort.MaxValue || centralOffset == uint.MaxValue)
                    return false;

                stream.Position = centralOffset;
                Span<byte> header = stackalloc byte[46];

                for (int i = 0; i < totalEntries; i++)
                {
                    ReadExactly(stream, header);
                    if (BinaryPrimitives.ReadUInt32LittleEndian(header) != CentralDirectorySignature)
                        return false;

                    ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(header[8..10]);
                    if ((flags & 0x0001) != 0)
                        return true;

                    ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header[28..30]);
                    ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[30..32]);
                    ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(header[32..34]);
                    stream.Seek(nameLength + extraLength + commentLength, SeekOrigin.Current);
                }

                return false;
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }

        private static int FindSignatureFromEnd(ReadOnlySpan<byte> data, uint signature)
        {
            for (int i = data.Length - 4; i >= 0; i--)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i, 4)) == signature)
                    return i;
            }

            return -1;
        }

        private static void ReadExactly(Stream stream, Span<byte> buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = stream.Read(buffer[total..]);
                if (read == 0)
                    throw new EndOfStreamException();
                total += read;
            }
        }

        private static void ReadExactly(Stream stream, byte[] buffer) => ReadExactly(stream, buffer.AsSpan());
    }
}
