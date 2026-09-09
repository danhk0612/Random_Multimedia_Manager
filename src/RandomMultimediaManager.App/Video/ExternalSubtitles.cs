using System.IO;
using System.Text;

namespace RandomMultimediaManager.App.Video;

public enum ExternalSubtitleFormat
{
    Srt,
    Smi
}

public sealed record ExternalSubtitleCandidate(
    string Path,
    string DisplayName,
    ExternalSubtitleFormat Format,
    bool ExactBaseName,
    int Priority);

public sealed record PreparedExternalSubtitle(
    string OriginalPath,
    string LoadPath,
    ExternalSubtitleFormat Format,
    string EncodingName,
    bool IsTemporary);

public sealed record ExternalSubtitleApplyResult(
    bool Success,
    string? Error,
    string? OriginalPath,
    string? EncodingName,
    int? TrackId,
    string? TemporaryPath);

public static class ExternalSubtitleService
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    static ExternalSubtitleService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static IReadOnlyList<ExternalSubtitleCandidate> Discover(string videoPath)
    {
        if (!System.IO.Path.IsPathFullyQualified(videoPath))
            throw new ArgumentException("영상 경로는 절대 경로여야 합니다.", nameof(videoPath));

        string? directory = System.IO.Path.GetDirectoryName(videoPath);
        string videoName = System.IO.Path.GetFileNameWithoutExtension(videoPath);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(videoName) || !Directory.Exists(directory))
            return Array.Empty<ExternalSubtitleCandidate>();

        var candidates = new List<ExternalSubtitleCandidate>();
        foreach (string path in Directory.EnumerateFiles(directory))
        {
            string extension = System.IO.Path.GetExtension(path);
            ExternalSubtitleFormat? format = extension.Equals(".srt", StringComparison.OrdinalIgnoreCase)
                ? ExternalSubtitleFormat.Srt
                : extension.Equals(".smi", StringComparison.OrdinalIgnoreCase)
                    ? ExternalSubtitleFormat.Smi
                    : null;
            if (format is null) continue;

            string subtitleName = System.IO.Path.GetFileNameWithoutExtension(path);
            bool exact = subtitleName.Equals(videoName, StringComparison.OrdinalIgnoreCase);
            bool suffixed = subtitleName.StartsWith(videoName + ".", StringComparison.OrdinalIgnoreCase);
            if (!exact && !suffixed) continue;

            int priority = (exact ? 0 : 2) + (format == ExternalSubtitleFormat.Smi ? 1 : 0);
            candidates.Add(new ExternalSubtitleCandidate(
                System.IO.Path.GetFullPath(path),
                System.IO.Path.GetFileName(path),
                format.Value,
                exact,
                priority));
        }

        return candidates
            .OrderBy(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Path, StringComparer.Ordinal)
            .ToArray();
    }

    public static ExternalSubtitleCandidate FromManualPath(string path)
    {
        string fullPath = ValidateSubtitlePath(path, out ExternalSubtitleFormat format);
        return new ExternalSubtitleCandidate(
            fullPath,
            System.IO.Path.GetFileName(fullPath),
            format,
            false,
            int.MaxValue);
    }

    public static PreparedExternalSubtitle PrepareForLoad(string path)
    {
        string fullPath = ValidateSubtitlePath(path, out ExternalSubtitleFormat format);
        byte[] bytes = File.ReadAllBytes(fullPath);

        if (format == ExternalSubtitleFormat.Srt)
        {
            _ = DecodeUtf8(bytes);
            return new PreparedExternalSubtitle(fullPath, fullPath, format, "UTF-8", false);
        }

        try
        {
            _ = DecodeUtf8(bytes);
            return new PreparedExternalSubtitle(fullPath, fullPath, format, "UTF-8", false);
        }
        catch (DecoderFallbackException)
        {
            Encoding korean = Encoding.GetEncoding(
                949,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
            string text = korean.GetString(bytes);
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RandomMultimediaManager", "Subtitles");
            Directory.CreateDirectory(directory);
            string temporaryPath = System.IO.Path.Combine(
                directory,
                $"{System.IO.Path.GetFileNameWithoutExtension(fullPath)}-{Guid.NewGuid():N}.smi");
            File.WriteAllText(temporaryPath, text, Utf8WithoutBom);
            return new PreparedExternalSubtitle(fullPath, temporaryPath, format, "CP949/EUC-KR → UTF-8", true);
        }
    }

    public static void DeleteTemporary(PreparedExternalSubtitle subtitle)
    {
        if (subtitle.IsTemporary && File.Exists(subtitle.LoadPath))
            File.Delete(subtitle.LoadPath);
    }

    private static string ValidateSubtitlePath(string path, out ExternalSubtitleFormat format)
    {
        if (!System.IO.Path.IsPathFullyQualified(path) || !File.Exists(path))
            throw new FileNotFoundException("외부 자막 파일을 찾을 수 없습니다.", path);

        string extension = System.IO.Path.GetExtension(path);
        if (extension.Equals(".srt", StringComparison.OrdinalIgnoreCase))
            format = ExternalSubtitleFormat.Srt;
        else if (extension.Equals(".smi", StringComparison.OrdinalIgnoreCase))
            format = ExternalSubtitleFormat.Smi;
        else
            throw new NotSupportedException("T10 외부 자막은 SRT와 SMI만 지원합니다.");

        return System.IO.Path.GetFullPath(path);
    }

    private static string DecodeUtf8(byte[] bytes)
    {
        int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
    }
}
