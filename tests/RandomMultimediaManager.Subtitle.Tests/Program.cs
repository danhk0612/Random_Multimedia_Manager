using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using LibVLCSharp.Shared;
using RandomMultimediaManager.App.Video;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var surface = new Grid();
        var window = new Window { Title = "T10 subtitle probe", Content = surface, Width = 640, Height = 400 };
        window.Loaded += async (_, _) =>
        {
            int code = 0;
            try { await RunAsync(surface); }
            catch (Exception ex) { Console.Error.WriteLine(ex); code = 1; }
            finally { window.Close(); app.Shutdown(code); }
        };
        return app.Run(window);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"PASS {message}");
    }

    private static async Task RunAsync(Grid surface)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string temporary = Path.Combine(Path.GetTempPath(), "rmm-t10-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "silent.mp4");
        string videoPath = Path.Combine(temporary, "movie.mp4");
        string plainVideoPath = Path.Combine(temporary, "plain.mp4");
        File.Copy(fixture, videoPath);
        File.Copy(fixture, plainVideoPath);

        string exactSrt = Path.Combine(temporary, "movie.srt");
        string exactSmi = Path.Combine(temporary, "movie.smi");
        string suffixedSrt = Path.Combine(temporary, "movie.ko.srt");
        string suffixedSmi = Path.Combine(temporary, "movie.en.smi");
        string unrelated = Path.Combine(temporary, "movie2.srt");
        string corruptSrt = Path.Combine(temporary, "broken.srt");

        File.WriteAllText(exactSrt,
            "1\r\n00:00:00,200 --> 00:00:02,000\r\n한글 UTF-8 자막\r\n", new UTF8Encoding(false));
        string smiText = "<SAMI><BODY><SYNC Start=1000><P Class=KRCC>한글 CP949 자막<SYNC Start=3000><P Class=KRCC>두 번째</BODY></SAMI>";
        File.WriteAllBytes(exactSmi, Encoding.GetEncoding(949).GetBytes(smiText));
        File.WriteAllText(suffixedSrt,
            "1\r\n00:00:00,500 --> 00:00:02,500\r\n언어 접미사 SRT\r\n", new UTF8Encoding(false));
        File.WriteAllBytes(suffixedSmi, Encoding.GetEncoding(949).GetBytes("<SAMI><BODY><SYNC Start=500>suffix</BODY></SAMI>"));
        File.WriteAllText(unrelated, "1\r\n00:00:00,000 --> 00:00:01,000\r\nnot a candidate\r\n", new UTF8Encoding(false));
        File.WriteAllBytes(corruptSrt, [0x81, 0x81, 0xFF]);

        PreparedVideo? video = null;
        PreparedVideo? plainVideo = null;
        string? ownedTemporarySubtitle = null;
        try
        {
            IReadOnlyList<ExternalSubtitleCandidate> candidates = ExternalSubtitleService.Discover(videoPath);
            Check(candidates.Count == 4, "same-folder base-name discovery excludes unrelated movie2 subtitle");
            Check(candidates[0].Path == exactSrt && candidates[1].Path == exactSmi &&
                candidates[2].Path == suffixedSrt && candidates[3].Path == suffixedSmi,
                "candidate priority is exact SRT, exact SMI, suffixed SRT, suffixed SMI");
            Check(ExternalSubtitleService.Discover(plainVideoPath).Count == 0, "video without matching subtitle has no automatic candidate");

            PreparedExternalSubtitle preparedSrt = ExternalSubtitleService.PrepareForLoad(exactSrt);
            Check(!preparedSrt.IsTemporary && preparedSrt.EncodingName == "UTF-8", "UTF-8 SRT uses original file without conversion");

            PreparedExternalSubtitle preparedSmi = ExternalSubtitleService.PrepareForLoad(exactSmi);
            Check(preparedSmi.IsTemporary && File.Exists(preparedSmi.LoadPath), "CP949/EUC-KR SMI is converted to temporary UTF-8");
            string converted = File.ReadAllText(preparedSmi.LoadPath, Encoding.UTF8);
            Check(converted.Contains("한글 CP949 자막") && converted.Contains("<SYNC Start=1000>"),
                "SMI Korean text and sync markup survive encoding conversion");
            ExternalSubtitleService.DeleteTemporary(preparedSmi);
            Check(!File.Exists(preparedSmi.LoadPath), "standalone converted SMI temporary file can be released");

            bool invalidRejected = false;
            try { _ = ExternalSubtitleService.PrepareForLoad(corruptSrt); }
            catch (DecoderFallbackException) { invalidRejected = true; }
            Check(invalidRejected, "invalid non-UTF-8 SRT is rejected instead of guessed as Korean encoding");

            var operation = new VideoOperation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var preparedVideo = await PreparedVideo.PrepareAsync(surface, operation, videoPath, null, false, CancellationToken.None);
            Check(preparedVideo.Status == VideoPreparationStatus.Ready && preparedVideo.Video is not null,
                $"video remains independently Ready before subtitle load: {preparedVideo.Error}");
            video = preparedVideo.Video!;
            var visit = new VideoVisit(operation, Guid.NewGuid());
            video.Activate(visit, 50, true);
            video.SetPaused(visit, true);
            await WaitForStateAsync(video, visit, VLCState.Paused);
            var beforeSubtitle = video.Snapshot(visit);

            ExternalSubtitleApplyResult srtResult = await video.LoadExternalSubtitleAsync(visit, exactSrt);
            Check(srtResult.Success && video.SelectedSubtitleTrack(visit) >= 0, $"manual/automatic SRT load selects a track: {srtResult.Error}");
            Check(video.Snapshot(visit).State == VLCState.Paused && video.Snapshot(visit).Visit == beforeSubtitle.Visit,
                "SRT load preserves paused playback state and visit token");
            Check(video.DisableSubtitles(visit) && video.SelectedSubtitleTrack(visit) < 0, "subtitle off disables selected track without changing visit");

            ExternalSubtitleApplyResult smiResult = await video.LoadExternalSubtitleAsync(visit, exactSmi);
            ownedTemporarySubtitle = smiResult.TemporaryPath;
            Check(smiResult.Success && smiResult.EncodingName == "CP949/EUC-KR → UTF-8" &&
                ownedTemporarySubtitle is not null && File.Exists(ownedTemporarySubtitle),
                $"CP949/EUC-KR SMI loads through owned UTF-8 temporary file: {smiResult.Error}");
            Check(video.Snapshot(visit).State == VLCState.Paused, "SMI load does not resume or restart paused video");

            ExternalSubtitleApplyResult badResult = await video.LoadExternalSubtitleAsync(visit, corruptSrt);
            Check(!badResult.Success && video.Snapshot(visit).Visit == visit && video.Snapshot(visit).State == VLCState.Paused,
                "subtitle encoding failure is separate from video readiness and playback state");

            await video.DisposeAsync();
            video = null;
            Check(ownedTemporarySubtitle is not null && !File.Exists(ownedTemporarySubtitle),
                "PreparedVideo disposal removes converted subtitle temporary file");
            using (new FileStream(exactSrt, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            using (new FileStream(exactSmi, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            Check(surface.Children.Count == 0, "video/subtitle disposal releases WPF view and original subtitle file handles");

            var plainOperation = new VideoOperation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var plainPrepared = await PreparedVideo.PrepareAsync(surface, plainOperation, plainVideoPath, null, false, CancellationToken.None);
            Check(plainPrepared.Status == VideoPreparationStatus.Ready && plainPrepared.Video is not null,
                $"next video without subtitle is Ready: {plainPrepared.Error}");
            plainVideo = plainPrepared.Video!;
            var plainVisit = new VideoVisit(plainOperation, Guid.NewGuid());
            plainVideo.Activate(plainVisit, 50, true);
            Check(plainVideo.SelectedSubtitleTrack(plainVisit) < 0, "new video does not inherit previous external subtitle selection");
            await plainVideo.DisposeAsync();
            plainVideo = null;
        }
        finally
        {
            if (video is not null) await video.DisposeAsync();
            if (plainVideo is not null) await plainVideo.DisposeAsync();
            await Task.Run(() => Directory.Delete(temporary, true));
        }

        Console.WriteLine("T10 subtitle probe complete. Visual subtitle timing/sync remains a manual observation item.");
    }

    private static async Task WaitForStateAsync(PreparedVideo video, VideoVisit visit, VLCState state)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        while (video.Snapshot(visit).State != state && DateTime.UtcNow < deadline)
            await Task.Delay(25);
        Check(video.Snapshot(visit).State == state, $"video reaches {state} before subtitle mutation");
    }
}
