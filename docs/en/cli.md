# aegnc command-line tool

[English](cli.md) · [简体中文](../zh-cn/cli.md) · [All guides](README.md)

`aegnc` ships with the desktop application and converts subtitles or burns saved projects into video. On Windows it sits beside the desktop executable; on macOS it is at `AegiNext.app/Contents/MacOS/aegnc`. Installation does not add it to PATH. Replace `aegnc` in the examples with its actual path when necessary.

```sh
./AegiNext.app/Contents/MacOS/aegnc --help
```

After extracting the Windows distribution, run `& .\AegiNext\aegnc.exe --help` in PowerShell. From source, use `dotnet run --project src/AegiNext.Cli -c Release -- --help`. Video export also needs matching native modules and an adjacent `aegn-exporter`; see [Building](building.md).

## Convert subtitles

```sh
aegnc convert -i input.ass --target srt -o subtitles/ --accept-loss
aegnc convert -i project.aeginext --target ass -o subtitles.ass --accept-loss
```

Input supports ASS v4+, SRT, and `.aeginext`; choose `--target ass|srt`. TXT and traditional SSA are outside the first CLI version. Input uses `--input/-i`, output uses `--output/-o`, and explicit relative paths resolve against the current working directory.

Omitting output writes beside the input using its stem and the target extension. Same-format conversion adds `.converted`. Directory output adds numeric suffixes to avoid existing files. An explicit output file must have the matching extension and cannot overwrite an existing file or name the input.

Existing directories, paths ending in a separator, and paths without an extension are directories. Other paths are files: `-o result` creates a directory, while `-o result.srt` names a file.

Conversion displays all import and export diagnostics. Interactive terminals require one confirmation; scripts must supply `--accept-loss`, otherwise the command exits without writing. This switch accepts reported conversion losses without bypassing parse errors or project limits. ASS-to-ASS rebuilds the format content and does not guarantee a byte-identical round trip.

Text input is strict UTF-8 with an optional UTF-8 BOM; output is UTF-8 without a BOM. ASS retains declared PlayRes axes, using 1920/1080 for missing axes with a diagnostic. SRT uses a temporary 1920×1080 canvas. External-subtitle projects use 30 fps, affecting sampled animations; project input retains its own canvas, frame rate, and reference scaling. An unconfirmed media playback origin produces a diagnostic and uses zero offset after acceptance.

## Export video

```sh
aegnc export -i project.aeginext --encoding h264 --use-gpu --vbr --video-bitrate 8 -o output/
aegnc export -i project.aeginext --encoding vp9 --crf --quality 32 --audio opus --audio-bitrate 192 --container webm
```

`export` accepts a saved `.aeginext` and burns in the project appearance. It requires a valid media binding and supported even canvas dimensions. It shares encoding, HDR, muxing, and timestamp validation with desktop export; see [Video export](export.md). The first CLI version has no stream-copy subtitle command.

| Option | Values and defaults |
|---|---|
| `--encoding` | auto/h264/hevc/vp8/vp9/av1; default auto chooses H264 for SDR and HEVC for HDR |
| `--use-gpu` | Software by default; explicitly requested hardware fails when unavailable |
| `--crf`, `--cq`, `--vbr`, `--cbr` | Mutually exclusive; software defaults to CRF, VP8 to CQ, GPU to VBR |
| `--quality` | H264/HEVC 0–51, default 20; VP8/VP9 0–63 and AV1 1–63, default 32 |
| `--video-bitrate` | 0.1–200 Mbps, default 8; decimal point, at most three decimal places |
| `--speed` | ultrafast/superfast/veryfast/faster/fast/medium/slow/slower/veryslow; default medium |
| `--audio` | copy/aac/opus/none; default copy |
| `--audio-bitrate` | Integer 32–512 kbps, default 192; only used for AAC/Opus |
| `--decode` | auto/software/hardware; default auto, independent of GPU encoding |
| `--container` | mp4/mkv/webm; must match encoding, audio, and the explicit output extension |

CRF rejects a video bitrate, VBR/CBR reject quality, and CQ accepts both. GPU rejects CRF/CQ. WebM rejects AAC; the worker checks actual copied-audio compatibility instead of silently re-encoding it.

Omitted output uses `project directory/output/project filename-yyyyMMdd-HHmmss.extension`. Auto/H264/HEVC default to MP4; VP8/VP9/AV1 default to MKV. An explicit file extension selects a compatible container, and an accompanying `--container` must agree. Explicit files cannot be overwritten; automatic directory names add numeric suffixes on collisions.

## Wizards and scripts

Run `aegnc` without arguments to choose conversion or video export and follow the applicable steps. Bare `convert` or `export` starts that command's complete wizard. Partial arguments only prompt for missing required fields: conversion needs input and target, while export only needs input. Other options use defaults. For example, `export -i project.aeginext --vbr` uses 8 Mbps without asking for a bitrate.

Invalid or conflicting explicit arguments fail directly. `--non-interactive`, redirected input, or redirected output prohibit prompts. Scripts must provide required fields and accept any conversion diagnostics. Use `--help/-h` for help and `--version` for the product version. Current prompts and descriptions use Chinese.

Commands show resolved settings before execution. Export reports frame count, time, and actual encoder. Ctrl+C cancels and cleans temporary files; output is committed atomically only after success. Exit codes: success `0`, runtime failure `1`, argument/configuration error `2`, cancellation `130`.
