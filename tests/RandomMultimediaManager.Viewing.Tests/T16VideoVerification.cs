using System.IO;
using System.Reflection;
using System.Windows.Controls;
using LibVLCSharp.Shared;
using RandomMultimediaManager.App.Video;

internal static class T16VideoVerification
{
    private static void Check(bool value, string name)
    { if (!value) throw new Exception(name); Console.WriteLine("PASS T16 " + name); }
    public static async Task Run(Grid surface)
    {
        PreparedVideo? first = null, second = null;
        MediaPlayer Player(PreparedVideo video) => (MediaPlayer)typeof(PreparedVideo)
            .GetField("player", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(video)!;
        try
        {
            VideoOperation Operation() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var one = Operation();
            var prepared = await PreparedVideo.PrepareAsync(surface, one,
                Path.Combine(AppContext.BaseDirectory, "silent.mp4"), null, false, CancellationToken.None);
            first = prepared.Video ?? throw new Exception(prepared.Error);
            var visit = new VideoVisit(one, Guid.NewGuid());
            first.Activate(visit, 60, false);
            await Task.Delay(100);
            var before = first.Snapshot(visit);
            PreparedVideo.SetPrivacyMuted(true);
            Check(Player(first).Mute && !first.AppMuted, "privacy mutes native output without overwriting app mute");
            Check(first.Snapshot(visit).State == before.State && first.Snapshot(visit).Visit == before.Visit,
                "hide does not pause or replace visit");
            // A second decoder becomes Ready/active while Hidden, including the same path reopen case.
            var two = Operation();
            prepared = await PreparedVideo.PrepareAsync(surface, two,
                Path.Combine(AppContext.BaseDirectory, "silent.mp4"), null, false, CancellationToken.None);
            second = prepared.Video ?? throw new Exception(prepared.Error);
            var nextVisit = new VideoVisit(two, Guid.NewGuid());
            second.Activate(nextVisit, 70, false);
            Check(Player(second).Mute && !second.AppMuted && Player(first).Mute, "late Ready and multiple players remain muted");
            second.SetMuted(nextVisit, false); second.SetVolume(nextVisit, 80);
            Check(Player(second).Mute, "reopen state restoration cannot bypass privacy mute");
            first.SetMuted(visit, true); first.SetPaused(visit, true);
            await Task.Delay(100);
            var paused = first.Snapshot(visit).State;
            PreparedVideo.SetPrivacyMuted(false);
            Check(first.AppMuted && Player(first).Mute && first.Snapshot(visit).State == paused,
                "original app mute and pause survive restoration");
            Check(!second.AppMuted && (second.AudioUnavailable || !Player(second).Mute), "unmuted app intent restored");
            PreparedVideo.SetPrivacyMuted(true);
            await second.DisposeAsync(); second = null;
            PreparedVideo.SetPrivacyMuted(false);
            Check(first.AppMuted, "disposing hidden player does not change remaining player mute");
        }
        finally
        {
            if (second is not null) await second.DisposeAsync();
            if (first is not null) await first.DisposeAsync();
            PreparedVideo.SetPrivacyMuted(false);
        }
    }
}
