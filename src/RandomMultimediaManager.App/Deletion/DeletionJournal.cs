using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.Deletion;

public enum DeletionMode { Recycle, Permanent }
public enum DeletionPhase { Prepared, Succeeded, Failed, Cancelled, Unknown }
public sealed record DeletionTarget(Guid ItemId, long FileSize, long LastWriteTimeUtc);
public sealed record DeletionRecord(int Version, Guid OperationId, string PathKey, string Path,
    DeletionTarget[] Targets, DeletionMode Mode, long CreatedAtUtc, DeletionPhase Phase);
public sealed record DeletionRecovery(string File, DeletionRecord? Record, string? Error, string? RecoveredPathKey = null)
{
    public string? PathKey => Record?.PathKey ?? RecoveredPathKey;
}

public class DeletionJournal(string directory)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true,
        Converters = { new JsonStringEnumConverter() } };
    public string DirectoryPath => directory;
    public virtual void Write(DeletionRecord record)
    {
        Validate(record);
        Directory.CreateDirectory(directory);
        string destination = System.IO.Path.Combine(directory, record.OperationId.ToString("D") + ".json");
        string temp = destination + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None,
            4096, FileOptions.WriteThrough))
        { JsonSerializer.Serialize(stream, record, Json); stream.Flush(true); }
        // Write-through rename also orders the directory entry on Windows. A crash before the
        // rename leaves only a staging file; OS deletion is never started before Write returns.
        if (OperatingSystem.IsWindows())
        {
            if (!MoveFileEx(temp, destination, 0x1 | 0x8))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        else File.Move(temp, destination, true);
    }
    public virtual IReadOnlyList<DeletionRecovery> ReadAll()
    {
        Directory.CreateDirectory(directory);
        var result = new List<DeletionRecovery>();
        foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var record = JsonSerializer.Deserialize<DeletionRecord>(File.ReadAllText(file), Json)
                    ?? throw new InvalidDataException("빈 삭제 저널입니다.");
                Validate(record);
                if (System.IO.Path.GetFileNameWithoutExtension(file) != record.OperationId.ToString("D"))
                    throw new InvalidDataException("삭제 저널 ID가 일치하지 않습니다.");
                result.Add(new(file, record, null));
            }
            catch (Exception ex)
            {
                string? key = null;
                try
                {
                    using var json = JsonDocument.Parse(File.ReadAllText(file));
                    string? path = json.RootElement.GetProperty("Path").GetString();
                    string? candidate = json.RootElement.GetProperty("PathKey").GetString();
                    if (!string.IsNullOrWhiteSpace(path) && candidate == path.ToUpperInvariant()) key = candidate;
                }
                catch { /* No reliable path: hold the entire library. */ }
                result.Add(new(file, null, ex.Message, key));
            }
        }
        return result;
    }
    public virtual void Remove(string file) => File.Delete(file);
    public string FileFor(DeletionRecord record) => System.IO.Path.Combine(directory, record.OperationId.ToString("D") + ".json");
    private static void Validate(DeletionRecord r)
    {
        if (r.Version != 1 || r.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(r.Path)
            || r.PathKey != r.Path.ToUpperInvariant() || r.Targets is not { Length: > 0 }
            || r.Targets.Any(t => t is null || t.ItemId == Guid.Empty || t.FileSize < 0)
            || r.Targets.Select(t => t.ItemId).Distinct().Count() != r.Targets.Length
            || !Enum.IsDefined(r.Mode) || !Enum.IsDefined(r.Phase))
            throw new InvalidDataException("삭제 저널 계약을 확인할 수 없습니다. 기록을 보존합니다.");
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existing, string replacement, int flags);
}
