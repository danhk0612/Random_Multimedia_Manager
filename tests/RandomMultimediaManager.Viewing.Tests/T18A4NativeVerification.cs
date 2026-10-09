using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RandomMultimediaManager.App.Video;

internal static class T18A4NativeVerification
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS T18A-4 WPF " + message); }
    public static async Task Run()
    {
        var surface = new Grid();
        var window = new Window { Content = surface, Width = 400, Height = 300 };
        window.Show(); await Dispatcher.Yield(DispatcherPriority.Loaded);
        string fixture = Path.Combine(AppContext.BaseDirectory, "silent.mp4");
        string root = Path.Combine(Path.GetTempPath(), "rmm-t18a4-native-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        string subtitle = Path.Combine(root, "test.srt");
        await File.WriteAllTextAsync(subtitle, "1\n00:00:00,000 --> 00:00:05,000\ntest\n");
        try
        {
            foreach (string stage in new[] { "seek", "subtitle", "stop", "play", "closing-subtitle" })
            {
                var operation = new VideoOperation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
                var prepared = await PreparedVideo.PrepareAsync(surface, operation, fixture, null, false, CancellationToken.None);
                Check(prepared.Status == VideoPreparationStatus.Ready, stage + " real libVLC Ready");
                var video = prepared.Video!;
                var visit = new VideoVisit(operation, Guid.NewGuid()); video.Activate(visit, 0, true);
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                typeof(PreparedVideo).GetProperty("BeforeIo", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(video, (Func<string, Task>)(async name => { if (name == (stage == "closing-subtitle" ? "subtitle" : stage)) { entered.TrySetResult(); await release.Task; } }));
                Task io = stage == "seek" ? video.SeekAsync(visit, 500)
                    : stage is "subtitle" or "closing-subtitle" ? video.LoadExternalSubtitleAsync(visit, subtitle)
                    : stage == "play" ? video.PlayAsync(visit) : video.StopAsync(visit);
                await entered.Task;
                if (stage == "closing-subtitle")
                {
                    video.SetExitRequested(true);
                    release.SetResult();
                    bool blocked = false;
                    try { await io; } catch (InvalidOperationException) { blocked = true; }
                    Check(blocked && video.Snapshot(visit).Visit == visit, "Closing skips late subtitle apply while preserving active visit");
                    await video.DisposeAsync();
                    continue;
                }
                var disposal = video.DisposeAsync().AsTask();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check(!disposal.IsCompleted && !video.WhenIdleAsync().IsCompleted && surface.Children.Count == 1,
                    stage + " UI responsive, actual IO drain before HWND removal");
                bool rejected = false;
                try { await video.SeekAsync(visit, 0); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, stage + " no new IO during release");
                release.SetResult();
                try { await io; } catch (InvalidOperationException) { }
                await disposal;
                Check(surface.Children.Count == 0, stage + " late operation released before HWND cleanup");
            }
            bool discoverFailed = false;
            try { await Task.Run(() => ExternalSubtitleService.Discover(Path.Combine(root, "absent", "movie.mp4"))); }
            catch (DirectoryNotFoundException) { discoverFailed = true; }
            Check(discoverFailed, "unavailable subtitle directory is a failure, not no subtitles");
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }
}
