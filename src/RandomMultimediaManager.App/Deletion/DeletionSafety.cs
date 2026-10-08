using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.Deletion;

public enum RecycleSupport { Supported, Unsupported, Unknown }
public sealed record RecycleCapability(RecycleSupport Support, bool RecycleOnlyGuaranteed, string? Reason = null)
{
    // No NAS/RaiDrive backend has been verified. Shell/server recycle bins are not equivalent.
    public static RecycleCapability For(StorageBinding binding) => binding.Kind == StorageKind.Local
        ? new(RecycleSupport.Supported, true)
        : new(RecycleSupport.Unknown, false, "이 원격/provider의 휴지통 지원과 recycle-only 동작을 검증하지 못했습니다.");
    public bool CanRecycle => Enum.IsDefined(Support) && Support != RecycleSupport.Unsupported && RecycleOnlyGuaranteed;
}

public static class DeletionSafety
{
    public static string? AliasKey(string path, StorageObservation evidence)
    {
        var normalized = WindowsPath.Normalize(path);
        if (evidence.Kind == StorageKind.RemoteUNC) return normalized.PathKey;
        if (evidence.Kind != StorageKind.RemoteMapped || evidence.EvidenceKind != BindingEvidenceKind.WindowsMapping)
            return null;
        return WindowsPath.Normalize(evidence.Target!.TrimEnd('\\') + "\\" + normalized.Path[normalized.Root.Length..]).PathKey;
    }
    public static bool IsRemote(DeletionRecord record) => record.Version == 1
        || record.Bindings!.Any(b => b.Binding.Kind != StorageKind.Local || b.Binding.RequiresConfirmation);
}
