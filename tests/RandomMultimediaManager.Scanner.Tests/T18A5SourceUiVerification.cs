using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Scanning;
using RandomMultimediaManager.App.ViewModels;
using RandomMultimediaManager.Core;

internal static class T18A5SourceUiVerification
{
    public static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "rmm-t18a5-ui-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            using var database = LibraryDatabase.Open(Path.Combine(root, "library.db"));
            var category = new Category(Guid.NewGuid(), "UNC sources", MediaType.Video);
            database.SaveCategory(category);
            var scans = new ScanCoordinator(database, observe: ObserveAsync);
            var editor = new CategoryEditorViewModel(database, scans) { SelectedCategory = category };

            editor.SourcePath = @"\\MediaServer\Share\한글 폴더";
            var add = editor.SaveSource();
            Check(add.Success, "T18A-5 editor accepts and stores a UNC source path");
            var remote = editor.SelectedSource ?? throw new InvalidOperationException("UNC source was not selected after save.");
            Check(remote.RootPath == @"\\MediaServer\Share\한글 폴더"
                && remote.RootPathKey == remote.RootPath.ToUpperInvariant(),
                "T18A-5 UNC source preserves display casing and canonical PathKey");

            editor.SourceScanOnStartup = true;
            editor.SelectedSourceRefreshMode = SourceRefreshMode.Scheduled;
            editor.SourceIntervalHours = 168;
            Check((await editor.SaveSelectedSourcePolicyAsync()).Success,
                "T18A-5 saves explicitly selected remote startup and scheduled policy");
            var savedPolicy = database.GetEffectiveSourceRefreshPolicy(remote.Id);
            Check(savedPolicy is { ScanOnStartup: true, RefreshMode: SourceRefreshMode.Scheduled, IntervalHours: 168 },
                "T18A-5 persists the configured remote interval");

            long latestCompleted = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            database.SaveSourceRefreshPolicy(remote.Id, savedPolicy! with { LastCompletedAtUtc = latestCompleted });
            await scans.SavePolicyAsync(remote.Id, savedPolicy!);
            Check(database.GetEffectiveSourceRefreshPolicy(remote.Id).LastCompletedAtUtc == latestCompleted,
                "T18A-5 policy save preserves a newer scan completion committed after the UI read");
            savedPolicy = database.GetEffectiveSourceRefreshPolicy(remote.Id);

            var detectionBeforeFailure = scans.GetAccess(remote.Id);
            editor.SelectedSourceRefreshMode = SourceRefreshMode.Events;
            var rejected = await editor.SaveSelectedSourcePolicyAsync();
            Check(!rejected.Success && database.GetEffectiveSourceRefreshPolicy(remote.Id) == savedPolicy
                && scans.GetAccess(remote.Id) == detectionBeforeFailure
                && editor.SelectedSourceRefreshMode == SourceRefreshMode.Scheduled,
                "T18A-5 rejects remote Events and restores the previous policy/detection state");

            Check((await editor.ConfirmSelectedBindingAsync()).Success,
                "T18A-5 explicit confirmation observes and accepts the current UNC binding");
            var binding = database.GetStorageBinding(remote.RootPath)!;
            Check(binding.Kind == StorageKind.RemoteUNC && !binding.RequiresConfirmation
                && scans.GetAccess(remote.Id) == SourceAccessState.Available,
                "T18A-5 confirmation status is process-observed and does not replace source identity");

            string localPath = Path.Combine(root, "local media");
            Directory.CreateDirectory(localPath);
            editor.BeginNewSource();
            editor.SourcePath = localPath;
            Check(editor.SaveSource().Success, "T18A-5 editor accepts a local source");
            var local = editor.SelectedSource!;
            Check((await editor.ConfirmSelectedBindingAsync()).Success,
                "T18A-5 local source confirmation uses observed device evidence");
            Check(database.GetEffectiveSourceRefreshPolicy(local.Id) is
                { ScanOnStartup: true, RefreshMode: SourceRefreshMode.Events, IntervalHours: 24 },
                "T18A-5 applies the confirmed Local defaults without changing the existing UNC policy");

            await scans.CloseAsync();
            Console.WriteLine("PASS: T18A-5 source UI contract");
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    private static Task<StorageObservation> ObserveAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var root = WindowsPath.Normalize(path);
        return Task.FromResult(root.Root.StartsWith(@"\\", StringComparison.Ordinal)
            ? StorageObservation.Classify(path, MappingLookup.Failed)
            : StorageObservation.Classify(path, MappingLookup.NotMapped, localDriveType: true,
                device: "test-device", volume: "00000001"));
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}
