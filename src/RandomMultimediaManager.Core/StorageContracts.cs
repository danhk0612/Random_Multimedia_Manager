namespace RandomMultimediaManager.Core;

public enum StorageKind { Local, RemoteMapped, RemoteUNC, VirtualOrUnknown }
public enum BindingEvidenceKind { None, LocalDeviceAndVolume, WindowsMapping, UncName, UserConfirmation }
public enum MappingLookup { Failed, NotMapped, Mapped }
public enum SourceRefreshMode { Manual, Events, Scheduled }
public enum BindingAccess { Unconfirmed, Available, BindingChanged }

// Observations are supplied by a worker in the later IO coordinator. Fixed drive type or a
// failed WNet lookup alone is deliberately insufficient evidence for Local.
public sealed record StorageObservation(StorageKind Kind, string? Target, BindingEvidenceKind EvidenceKind,
    string? Provider = null, string? Device = null, string? Volume = null)
{
    public static StorageObservation Classify(string path, MappingLookup mapping,
        string? mappedTarget = null, bool localDriveType = false, string? device = null,
        string? volume = null, string? provider = null)
    {
        if (!Enum.IsDefined(mapping)) throw new ArgumentException("매핑 조회 결과 오류");
        var root = WindowsPath.Normalize(path);
        if (root.Root.StartsWith(@"\\", StringComparison.Ordinal))
            return new(StorageKind.RemoteUNC, root.RootKey, BindingEvidenceKind.UncName, provider);
        if (mapping == MappingLookup.Mapped && !string.IsNullOrWhiteSpace(mappedTarget))
        {
            var target = WindowsPath.Normalize(mappedTarget);
            if (!target.Root.StartsWith(@"\\", StringComparison.Ordinal))
                throw new ArgumentException("매핑 대상은 UNC 경로여야 합니다.");
            return new(StorageKind.RemoteMapped, target.PathKey, BindingEvidenceKind.WindowsMapping, provider);
        }
        if (mapping == MappingLookup.NotMapped && localDriveType && !string.IsNullOrWhiteSpace(device)
            && !string.IsNullOrWhiteSpace(volume) && provider is null)
            return new(StorageKind.Local, device, BindingEvidenceKind.LocalDeviceAndVolume, null, device, volume);
        return new(StorageKind.VirtualOrUnknown, null, BindingEvidenceKind.None, provider);
    }
    public void Validate()
    {
        if (!Enum.IsDefined(Kind) || !Enum.IsDefined(EvidenceKind)) throw new ArgumentException("바인딩 종류 오류");
        bool valid = Kind switch
        {
            StorageKind.Local => EvidenceKind == BindingEvidenceKind.LocalDeviceAndVolume
                && !string.IsNullOrWhiteSpace(Device) && !string.IsNullOrWhiteSpace(Volume) && Target == Device && Provider is null,
            StorageKind.RemoteMapped => EvidenceKind == BindingEvidenceKind.WindowsMapping && IsUnc(Target),
            StorageKind.RemoteUNC => EvidenceKind == BindingEvidenceKind.UncName && IsUnc(Target),
            _ => EvidenceKind is BindingEvidenceKind.None or BindingEvidenceKind.UserConfirmation && Target is null
        };
        if (!valid) throw new ArgumentException("바인딩 근거가 부족합니다.");
    }
    private static bool IsUnc(string? value) => value is not null
        && WindowsPath.Normalize(value) is var path && path.PathKey == value && path.Root.StartsWith(@"\\");
}

public sealed record StorageBinding(string RootKey, StorageKind Kind = StorageKind.VirtualOrUnknown,
    string? ExpectedTarget = null, BindingEvidenceKind EvidenceKind = BindingEvidenceKind.None,
    long Revision = 0, bool RequiresConfirmation = true, string? Provider = null,
    string? Device = null, string? Volume = null)
{
    public StorageObservation Observation => new(Kind, ExpectedTarget, EvidenceKind, Provider, Device, Volume);
    public void Validate()
    {
        if (WindowsPath.Normalize(RootKey).RootKey != RootKey || Revision < 0)
            throw new ArgumentException("바인딩 루트 또는 revision 오류");
        Observation.Validate();
        bool unc = RootKey.StartsWith(@"\\", StringComparison.Ordinal);
        if ((unc && Kind is StorageKind.Local or StorageKind.RemoteMapped)
            || (!unc && Kind == StorageKind.RemoteUNC))
            throw new ArgumentException("루트 형식과 바인딩 종류 불일치");
        if (Kind == StorageKind.RemoteUNC && ExpectedTarget != RootKey)
            throw new ArgumentException("UNC 루트 근거 불일치");
        if (Kind == StorageKind.VirtualOrUnknown && !RequiresConfirmation)
            throw new ArgumentException("불명 provider는 매 실행 확인이 필요합니다.");
    }
}

public sealed record SourceRefreshPolicy(bool ScanOnStartup = false,
    SourceRefreshMode RefreshMode = SourceRefreshMode.Manual, int? IntervalHours = null,
    long? LastCompletedAtUtc = null)
{
    public static SourceRefreshPolicy DefaultFor(StorageBinding binding) =>
        binding.Kind == StorageKind.Local && !binding.RequiresConfirmation
            ? new(true, SourceRefreshMode.Events, 24) : new();
    public void Validate(StorageBinding binding)
    {
        if (!Enum.IsDefined(RefreshMode) || (RefreshMode == SourceRefreshMode.Manual
            ? IntervalHours is not null : IntervalHours is not (>= 1 and <= 168))
            || (RefreshMode == SourceRefreshMode.Events
                && (binding.Kind != StorageKind.Local || binding.RequiresConfirmation)))
            throw new ArgumentException("소스 갱신 정책이 허용 범위를 벗어났습니다.");
    }
}

// Per-process evidence, not persisted availability. Call Invalidate on disconnect/configuration
// changes before accepting another result. No IO or automatic work is started here.
public sealed class BindingVerification(StorageBinding binding)
{
    public StorageBinding Binding { get; } = binding;
    public long Generation { get; private set; }
    public BindingAccess Access { get; private set; } = BindingAccess.Unconfirmed;
    public void Invalidate() { Generation = checked(Generation + 1); Access = BindingAccess.Unconfirmed; }
    public bool Observe(long generation, StorageObservation observation, bool userConfirmed = false)
    {
        observation.Validate();
        if (generation != Generation) return false;
        if (observation.Kind == StorageKind.VirtualOrUnknown && Binding.ExpectedTarget is not null)
        { Invalidate(); return false; }
        bool mismatch = (Binding.ExpectedTarget is not null && (Binding.Kind != observation.Kind
            || Binding.ExpectedTarget != observation.Target || Binding.Provider != observation.Provider
            || Binding.Device != observation.Device || Binding.Volume != observation.Volume))
            || (Binding.Provider is not null && Binding.Provider != observation.Provider);
        if (mismatch) { Invalidate(); Access = BindingAccess.BindingChanged; return false; }
        if (Binding.RequiresConfirmation && !userConfirmed) { Access = BindingAccess.Unconfirmed; return false; }
        Access = BindingAccess.Available; return true;
    }
    public bool CanAccess(string path, long generation) => generation == Generation
        && Access == BindingAccess.Available && WindowsPath.Normalize(path).RootKey == Binding.RootKey;
}
