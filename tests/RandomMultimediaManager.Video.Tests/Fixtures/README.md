# T09 synthetic fixtures

These original test patterns contain no audio or user media. Both containers contain the same
6-second 320×180 / 24 fps H.264 test pattern, generated with FFmpeg:

```sh
ffmpeg -f lavfi -i color=c=blue:size=320x180:rate=24 -t 6 -c:v libx264 -preset fast -crf 30 -pix_fmt yuv420p -an silent.mp4
ffmpeg -i silent.mp4 -c copy silent.mkv
```

These fixtures exercise decoding/lifetime on Windows CI without requiring an audio device.
They cannot verify audible playback, audio tracks, mute leakage, or hardware decoding.
Pass paths to MP4/MKV files containing audio to the test executable for a desktop probe;
all modifications/deletions are limited to copies in a newly created temporary directory.

## T11 AVI/AC3 regression

`ac3.avi` is an original 8-second navy frame/440 Hz tone, H.264 1280×720 at
30000/1001 fps with AC3 stereo, 48 kHz, 448 kbps:

```sh
ffmpeg -f lavfi -i color=c=navy:s=1280x720:r=30000/1001 -f lavfi -i sine=frequency=440:sample_rate=48000 -t 8 -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a ac3 -ac 2 -b:a 448k ac3.avi
```

The lifecycle probe includes this file and prints active audio diagnostics.
On a runner without an audio endpoint it checks video-only fallback/lifetime;
that does not establish AC3 decoding or audible output. A desktop check remains required.
