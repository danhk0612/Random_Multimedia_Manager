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
