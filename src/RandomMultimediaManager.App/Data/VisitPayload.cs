using System.Security.Cryptography;
using System.Text;
using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.Data;

internal static class VisitPayload
{
    // Versioned binary encoding avoids culture/JSON formatting differences.
    internal static byte[] Hash(VisitCommitRequest request)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(1);
            writer.Write(request.VisitId.ToString("D"));
            writer.Write(request.ItemId.ToString("D"));
            writer.Write(request.ViewedAtUtc);
            writer.Write((int)request.Origin);
            writer.Write(request.SuppressHistory);
            writer.Write((int)request.Progress.MediaType);
            if (request.Progress.MediaType == MediaType.Comic)
            {
                writer.Write(request.Progress.ComicPageIndex!.Value);
                double offset = request.Progress.ComicPageOffset!.Value;
                writer.Write(offset == 0 ? 0d : offset); // -0 and +0 have the same meaning.
            }
            else writer.Write(request.Progress.VideoPositionMs!.Value);
        }
        return SHA256.HashData(stream.ToArray());
    }
}
