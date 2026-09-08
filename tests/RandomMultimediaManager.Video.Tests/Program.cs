using System.IO;
using System.Windows;
using System.Windows.Controls;
using LibVLCSharp.Shared;
using RandomMultimediaManager.App.Video;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var surface = new Grid();
        var window = new Window { Title = "T09 native lifecycle probe", Content = surface, Width = 640, Height = 400 };
        window.Loaded += async (_, _) =>
        {
            int code = 0;
            try { await RunAsync(surface, args); }
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

    private static async Task RunAsync(Grid surface, string[] args)
    {
        string[] sources = args.Length == 0
            ? [Path.Combine(AppContext.BaseDirectory, "Fixtures", "silent.mp4"), Path.Combine(AppContext.BaseDirectory, "Fixtures", "silent.mkv")]
            : args;
        string temporary = Path.Combine(Path.GetTempPath(), "rmm-t09-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var session = Guid.NewGuid();
        VideoOperation Operation() => new(session, Guid.NewGuid(), Guid.NewGuid());
        PreparedVideo? current = null;
        PreparedVideo? candidate = null;
        try
        {
            foreach (string source in sources)
            {
                string copy = Path.Combine(temporary, Guid.NewGuid() + Path.GetExtension(source));
                File.Copy(source, copy);
                for (int iteration = 0; iteration < 3; iteration++)
                {
                    var token = Operation();
                    var result = await PreparedVideo.PrepareAsync(surface, token, copy, null, false, CancellationToken.None);
                    Check(result.Status == VideoPreparationStatus.Ready && result.Video is not null, $"Ready {Path.GetExtension(copy)}: {result.Error}");
                    current = result.Video!;
                    Console.WriteLine($"Native {current.NativeVersion}; decoded={current.PreparedVideoBlocks}/{current.PreparedAudioBlocks}");
                    Check(!current.CanActivate(Operation()), "wrong operation cannot activate");
                    var visit = new VideoVisit(token, Guid.NewGuid());
                    current.Activate(visit, 50, true);
                    await Task.Delay(250);
                    Check(current.Snapshot(visit).Visit == visit, "active visit identity");
                    Check(current.Seek(visit, 1000), "local seek accepted");
                    current.SetPaused(visit, true);
                    await Task.Delay(100);
                    var before = current.Snapshot(visit);
                    bool staleRejected = false;
                    try { current.SetMuted(visit with { VisitId = Guid.NewGuid() }, false); }
                    catch (InvalidOperationException) { staleRejected = true; }
                    Check(staleRejected && current.Snapshot(visit).Muted == before.Muted, "stale visit cannot unmute current");

                    var failure = await PreparedVideo.PrepareAsync(surface, Operation(), Path.Combine(temporary, "absent.mp4"), null, false, CancellationToken.None);
                    Check(failure.Status == VideoPreparationStatus.Failed, "missing file fails preparation");
                    string broken = Path.Combine(temporary, "broken.mp4");
                    File.WriteAllText(broken, "not a video");
                    failure = await PreparedVideo.PrepareAsync(surface, Operation(), broken, null, false, CancellationToken.None);
                    Check(failure.Status == VideoPreparationStatus.Failed, "corrupt file fails preparation");
                    File.Delete(broken);
                    Check(current.Snapshot(visit).Visit == before.Visit && current.Snapshot(visit).Muted == before.Muted,
                        "failed candidate preserves current identity and mute");

                    using var cancelled = new CancellationTokenSource();
                    var preparing = PreparedVideo.PrepareAsync(surface, Operation(), copy, null, false, cancelled.Token);
                    cancelled.Cancel();
                    var cancelledResult = await preparing;
                    Check(cancelledResult.Status == VideoPreparationStatus.Cancelled && cancelledResult.Video is null, "cancel owns and releases late result");
                    Check(current.Snapshot(visit).Visit == visit, "late completion preserves current visit");

                    var secondToken = Operation();
                    var second = await PreparedVideo.PrepareAsync(surface, secondToken, copy, null, false, CancellationToken.None);
                    Check(second.Status == VideoPreparationStatus.Ready, $"two decoders coexist: {second.Error}");
                    candidate = second.Video!;
                    Check(current.Snapshot(visit).Muted == before.Muted && current.Snapshot(visit).State == VLCState.Paused,
                        "candidate does not change current playback/mute");
                    await candidate.DisposeAsync();
                    candidate = null;
                    Check(current.SetRate(visit, 1.5f), "rate accepted (actual speed needs observation)");
                    if (current.AudioTracks(visit).Any(t => t.Id >= 0))
                    {
                        current.SetVolume(visit, 35);
                        Check(current.Snapshot(visit).Volume == 35, "volume roundtrip");
                    }
                    Console.WriteLine($"Tracks audio={current.AudioTracks(visit).Length}, subtitle={current.SubtitleTracks(visit).Length}");
                    await current.StopAsync(visit);
                    await current.DisposeAsync();
                    await current.DisposeAsync();
                    current = null;
                    using (new FileStream(copy, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                    Check(surface.Children.Count == 0, "all WPF views removed");
                    string moved = copy + ".moved";
                    File.Move(copy, moved);
                    File.Move(moved, copy);
                    Console.WriteLine("PASS released test copy can be exclusively opened and moved");
                }
                File.Delete(copy);
                Check(!File.Exists(copy), "released test copy deleted");
            }
        }
        finally
        {
            if (candidate is not null) await candidate.DisposeAsync();
            if (current is not null) await current.DisposeAsync();
            // This uniquely created directory contains only copies and generated corrupt fixtures.
            Directory.Delete(temporary, true);
        }
        Console.WriteLine("Native probe complete. No audible-output, overlay, fullscreen or GPU success claim.");
    }
}
