# Media integration

[English](media.md) · [简体中文](../zh-cn/media.md) · [All guides](README.md)

## Set up and locate the adapter

Build `Workbench` for matching RID/configuration native libraries; see [Building](building.md). Development runs use absolute `AEGINEXT_FFPROBE_PATH`/`AEGINEXT_FFMPEG_PATH` or PATH; invalid explicit paths fail. Packages use their own tools.

| Location | Responsibility |
|---|---|
| `src/AegiNext.Media/Probing` | FFprobe tool identity, bounded processes, and immutable media facts |
| `src/AegiNext.Media/Decoding` | Decoder/frame contracts, native handles, and exact frame navigation |
| `src/AegiNext.Media/Playback` | Raw-frame scheduling and presentation ownership |
| `src/AegiNext.Media/Preview` | Independent SDR conversion |
| `src/AegiNext.Media/Audio`, `Analysis` | Output clocks and viewport audio analysis |
| `src/AegiNext.Desktop/Controllers`, `Workspace` | UI delivery, transport, calibration, and analysis coordination |
| `native/shared` | Shared preview/export demux, decode, seek, and color interpretation |

## Probe and decode

Use `FfprobeMediaProbe` for existing local files. It validates tool identity, invokes without a shell, drains both pipes, and bounds timeout/output. Preserve all stream indices, raw timestamps/time bases, color/HDR facts, and unknown values; reported frame rates do not prove CFR.

Open the selected absolute stream index with `FfmpegVideoDecoder`. Receive buffered frames and drain EOF. A returned frame owns an independent reference; later reads or decoder disposal cannot reclaim it. Dispose every frame explicitly. Plane copies contain valid pixels in logical row order, without cropping or color conversion.

Seek to a keyframe, then select the display interval `Time <= target < NextFrameTime`. Display timing uses original PTS, then FFmpeg's best-effort timestamp. When both are absent, a previous frame's valid duration can supply the next time; matching declared average and nominal frame rates can supply duration when necessary. An initial stream start can anchor the first sequential frame. These derived times carry provenance and never overwrite raw PTS or best-effort facts. Missing evidence or regressing display times rejects; duplicate times select the last decoded frame. VFR remains rational. Cache decoded intervals with bounded frame/byte budgets; cache hits must reconnect sequential playback to the logical next frame.

Auto decoding prefers VideoToolbox on macOS or D3D11VA on Windows and falls back to CPU before first delivery for unsupported hardware formats. GPU (strict) requires confirmed acceleration without CPU fallback; choose Auto or CPU for broad compatibility. Cancellation, corrupt input, and later errors do not trigger silent fallback. macOS selects a decoder with the actual VideoToolbox configuration and negotiates source-preserving output, including HEVC 4:2:2/4:4:4, AV1, VP9, and ProRes 422/4444 on capable devices. Readback preserves chroma, component precision, Alpha, geometry, and frame metadata; 12-bit ProRes may use a 16-bit output container. Windows D3D11VA readback supports opaque 4:2:0 NV12/P010. Windows selects Vulkan for ProRes 4444/4444 XQ (with or without Alpha) and extended HEVC sampling/precision that D3D11VA readback cannot preserve. Vulkan device selection matches a Windows hardware adapter and excludes software adapters; actual Vulkan frames and faithful CPU readback are required before reporting acceleration. ProRes uses GPU compute shaders, while HEVC uses supported Vulkan video profiles. The pinned SDK's Vulkan ProRes 422 sample differences exceed the compatibility bounds, so those profiles remain on CPU in Auto mode and reject strict GPU mode. Acceleration depends on the device, driver, OS, codec profile, and dimensions, rather than the MKV/MOV container; a VideoToolbox software session is not reported as GPU decoding.

The compatibility suite covers H.264 10-bit/RGB, HEVC 10-bit 4:2:2/4:4:4, VP8, VP9/AV1 10-bit, MPEG-2/4, MJPEG, ProRes 422 Proxy/LT/422/HQ and 4444/4444 XQ (with or without Alpha), and FFV1 16-bit in MKV, MP4, MOV, WebM, AVI, and MPEG-TS containers. AV1 uses the native decoder for supported hardware sessions and dav1d for software, including decoder-applied film grain. Codec support still depends on valid stream metadata and supported color interpretation.

Missing SDR color tags use explicit shared resolution rules. Raw facts remain unchanged, while resolved color records inferred fields. HDR evidence requires complete supported tags. Preview decoder preference and export decode/encode choices are independent.

## Keep one time source

Default audio output uses CoreAudio on macOS and WASAPI on Windows: 48 kHz stereo float PCM, about 200 ms prefill, at most 250 ms queued. Device presentation position maps to consumed media samples; SYSTEM time does not subtract a guessed queue/buffer delay.

Additional delay calibration is keyed by device, backend, sample rate, and channels. Device-clock loss freezes timing until replacement; F8/F9 cannot use an unavailable clock. Audio gaps/short tracks use silence. Video without audio uses a monotonic clock; explicit SDL output is ESTIMATED.

Playback results carry generations. New commands supersede stale deliveries; consumers check identity immediately before presentation. Pause/replacement cancels obsolete work without terminally canceling the shared decoder. Close requests native cancellation, drains work, and releases undelivered resources; handed-out frames remain consumer-owned.

## Compose preview and analyze audio

SDR conversion borrows the raw frame and produces independent, opaque, top-down sRGB BGRA8. ProRes 4444 Alpha remains present in the original decoded planes. Preview and export composite transparency onto black in linear light before their separate color pipelines; unspecified alpha is treated as straight, while explicit premultiplication is respected. Export uses high-precision source pixels and never consumes the SDR preview. Composition keeps the corresponding uncomposed background to avoid double subtitles or wrong-frame reuse.

Controller results check request, media time, project revision, and quality. Select preview quality in the workbench; interaction uses a temporary cap. Measure presented frames and dispatch latency separately from decode duration, especially for long GOP, high resolution, and repeated seeks.

Binding media starts an `AudioAnalysisSession` that builds complete waveform and spectrum pyramids in the project's `caches/audio/<identity>/`. `data.bin` stores uncompressed binary blocks; `index.bin` stores the version, exact rational timeline, level indexes, and SHA-256 checksums. Identity includes the algorithm version, stream index, file size and modification time, and up to 64 KiB each from the beginning, middle, and end. Moving media preserves identity. Complete caches survive reopening; cancelled, failed, or abandoned temporary builds cannot become cache hits.

Default analysis uses a fixed grid: 48 kHz mono waveform min/max with a finest persistent level of 512 samples; a 63-tap FIR low-pass to 16 kHz; and 1024-point Hann FFT, 256-sample hop, 128 logarithmic frequency rows, and a −80 to 0 dB intensity range. Normal waveform zoom and every spectrum zoom read existing levels through binary indexes. Coarse spectra select centers on the absolute media grid. Only waveform requests finer than the selected persistent level open a separate local decoder with a bounded PCM LRU. Viewports never compute FFTs or consume playback audio.

`AudioAnalysisCacheBuilder` decodes sequentially through one source and overlaps decoding, parallel FIR/FFT, and ordered writes through a bounded segment pipeline. The default segment contains 196608 samples (4.096 seconds). All projects share an application CPU budget for decoding, DSP, and local waveform detail. Automatic mode uses at most four permits; manual settings allow one through CPU count minus one, with one retained on a single-core machine. Foreground detail has bounded priority so background projects still advance. Each segment filters once; PCM, FFT, and spectrum buffers are reused. Window coefficients and FFT twiddles are precomputed, and logarithms are calculated once per frequency row. The default 64 MiB budget allocates 48 MiB to working storage and 16 MiB to read caches; memory also limits concurrency. PCM is not persisted. Finished batches publish in order; unfinished intervals remain pending. Hiding layers cancels viewport reads; replacing media or closing cancels and drains the build. Checkpoints run only after every batch worker has drained and can cooperatively release the task slot so saving and media replacement can proceed even with a one-slot limit.

The Audio Analysis settings page exposes automatic/manual workers, memory budget, waveform gain, and spectrum brightness/contrast. Display changes repaint immediately; worker and memory changes take effect at safe batch boundaries without rebuilding. Advanced mode exposes segment length, spectrum sample rate, FFT size, hop divisor, frequency rows/range, window function, dB range, and the finest persistent waveform level, with window/hop duration and cache-size estimates. Changes that affect cache content require Apply and Rebuild, which queues one rebuild per open project. Rebuilding cancels and drains the old build while retaining displayed layers until new data is available. Reset restores ordinary settings immediately and prepares default advanced settings as a draft requiring Apply.

Cache format v2 includes the complete analysis recipe digest in its identity and index. Execution and display settings do not affect identity, and caches for different recipes can coexist. Older cache versions regenerate once. Changing the analysis dB range recalculates from audio and can recover weak energy clipped by the previous range.

After Save As succeeds, `AudioCacheMigrationTask` copies the complete cache in the background. A running build records the latest destination and migrates after completion. Saving waits for neither generation nor copying. Migration retains a snapshot of the data handle and matching index, plus a destination writer lease, so rebuilding the source cannot mix two generations. Cache opening uses the same lease to read a consistent data/index pair. Cancellation or copy failure retains the original read path; another save can retry.

## Verify

```powershell
dotnet test Tests/AegiNext.Media.Tests/AegiNext.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~Probing'
dotnet test Tests/AegiNext.Desktop.Tests/AegiNext.Desktop.Tests.csproj -c Release --filter 'FullyQualifiedName~VideoPreview'
dotnet test Tests/AegiNext.Media.Tests/AegiNext.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~Analysis'
```

Native cases need the switches and tool paths in [Building](building.md). Test actual PTS/pixels, seeking, supersession, cancellation, frame lifetime, and faults. Silent system-device tests do not establish speaker latency; Headless presentation does not establish native HDR or GPU acceptance.

To verify the original HEVC 4:4:4 10-bit failure through the real desktop preview controller, set `AEGINEXT_COMPATIBILITY_MEDIA_PATH` to the absolute source path and run `OriginalVideoCompatibilityTests` with native tests enabled. Optional `AEGINEXT_COMPATIBILITY_REPORT_PATH` records frame hashes, intervals, fallback diagnostics, and resource counts.

## Audio-only projects

**File → Open Audio** (no default shortcut; assign one in **Settings → Shortcuts**) opens a file that has an audio stream but no video stream, such as WAV, FLAC, MP3, M4A/AAC, OGG/Opus, or MKA. Dropping an audio file on Preview does the same.

- **Replace semantics:** a project has exactly one media binding. Opening audio replaces the whole binding (the current video and its audio are unlinked); it does not swap only the soundtrack under the existing video. If the chosen file also contains a real video stream, it is opened as ordinary video media. Embedded cover art (`attached_pic`) is ignored.
- **Preview:** Preview shows a black canvas at the project resolution with subtitles rendered on top. The canvas advances on a fixed 25 fps grid driven by the audio clock, so playback, seeking, F8/F9, QWER/mouse timing, waveform, spectrogram, and Koumori analysis use the audio stream as with video media. Video keyframes are unavailable, so keyframe snapping in the timing post-processor is skipped.
- **Project file:** `.aeginext` stores the audio reference with `videoStreamIndex: null` (project format version 20). Save, reopen, and relinking a moved file work as with video. Older builds reject version 20 projects instead of misreading them.
- **Export:** video export and subtitle muxing need a video stream and are disabled with an explanatory message; the CLI rejects such projects too. Subtitle formats (ASS, SRT, …) still export normally.
