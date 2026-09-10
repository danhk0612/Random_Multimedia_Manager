using RandomMultimediaManager.Core;

int count = 0;
void Reject(Action action)
{
    try { action(); } catch (ArgumentException) { count++; return; }
    throw new Exception("Expected invalid value to be rejected.");
}
foreach (var progress in new[] { PlaybackProgress.Comic(0), PlaybackProgress.Comic(4, 1), PlaybackProgress.Video(0), PlaybackProgress.Video(long.MaxValue) })
{ progress.Validate(); count++; }
foreach (var progress in new[] {
    PlaybackProgress.Comic(-1), PlaybackProgress.Comic(1, double.NaN), PlaybackProgress.Comic(0, double.PositiveInfinity),
    PlaybackProgress.Comic(0, -0.1), PlaybackProgress.Comic(0, 1.1), PlaybackProgress.Video(-1),
    new PlaybackProgress(MediaType.Video, 0, null, 1), new PlaybackProgress(MediaType.Comic, 0, null, null),
    new PlaybackProgress((MediaType)100, null, null, 0) }) Reject(progress.Validate);
foreach (int days in new[] { 0, 7, 36500 }) { new AppSettings(days).Validate(); count++; }
Reject(new AppSettings(-1).Validate);
Reject(new AppSettings(36501).Validate);
Reject(new AppSettings(7, (ResumeMode)100).Validate);
if (new AppSettings() != new AppSettings(7, ResumeMode.Resume)) throw new Exception("Wrong defaults.");
Console.WriteLine($"PASS: {count + 1} Core checks");
T06Checks.Run();
