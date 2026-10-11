# Video export

[English](export.md) · [简体中文](../zh-cn/export.md) · [All guides](README.md)

## Export a video

1. Open **Export** and choose Automatic, H.264, HEVC, VP8, VP9, or AV1.
2. Select CPU or GPU encoding and speed. CPU quality mode shows CRF or CQ for the selected format, or Mbps with VBR/CBR. GPU uses VBR/CBR.
3. Choose source-audio copy, AAC, Opus, or no audio. AAC/Opus have a separate bitrate control.
4. Click **Export**, select a supported container, and choose a new destination file.
5. Follow progress, cancel if needed, and inspect the finished output.

Automatic chooses H.264 for SDR and HEVC for PQ/HLG. Saved projects default to `project directory/output/project name-yyyyMMdd-HHmmss.extension`, creating `output` when needed. Automatic, H.264, and HEVC default to MP4; VP8, VP9, and AV1 default to MKV. The save dialog lets you change location, filename, and compatible container. Unsaved projects require choosing an output location.

Export captures the project when started and runs in an independent `aegn-exporter` process. Later edits do not affect the job. Success atomically commits the new file; failure/cancellation cleans only its own temporary output. Existing destinations are rejected. Invalid input, output selection failures, task startup failures, and encoding failures show an error dialog and record full details in the log. Correct the settings after acknowledging the error and retry. Cancelling an export does not show an error dialog.

## Choose format and quality

| Format | CPU encoder | Quality mode, range, and default | Output and containers |
|---|---|---|---|
| H.264 | libx264 | CRF 0–51, default 20 | SDR 8-bit; MP4, MKV |
| HEVC / H.265 | libx265 | CRF 0–51, default 20 | 10-bit; MP4, MKV |
| VP8 | libvpx | CQ 0–63, default 32 | SDR 8-bit; MKV, WebM |
| VP9 | libvpx-vp9 | CRF 0–63, default 32 | 8/10-bit; MKV, WebM, MP4 |
| AV1 | libsvtav1 | CRF 1–63, default 32 | 8/10-bit; MKV, WebM, MP4 |

Lower CRF/CQ values usually increase quality and size; numbers cannot be compared directly across formats. VP9 CRF 0 enables lossless encoding. AV1 starts at 1 and cannot use another format's zero setting. VP9/AV1 use 10-bit for sources above 8-bit or HDR, otherwise 8-bit. Export uses 4:2:0 output.

VP8 CQ constrains quality by a target bitrate, so set both quality and Mbps. Other formats' CRF modes do not use a target bitrate. All formats support VBR/CBR with a video target of **0.1–200 Mbps**, default **8 Mbps**, rounded down to 1 kbps precision. VBR varies with content; CBR uses a constant target and buffering constraints. Actual average file bitrate depends on content, duration, and encoder, so a short clip or individual frame need not match the requested value exactly.

The nine speed presets are ultrafast, superfast, veryfast, faster, fast, medium, slow, slower, and veryslow; medium is the default. Slower settings generally spend more time improving compression. Each encoder uses its own speed controls, so the same name does not imply the same speed or quality. GPU presets map to the backend's available speed levels.

## Choose CPU or GPU

CPU encoding supports all listed formats on macOS and Windows. GPU accelerates video encoding; subtitle composition still uses the linear high-precision pipeline. The media settings separately determine video decoding policy.

| Format | macOS GPU | Windows GPU candidates |
|---|---|---|
| H.264, HEVC | VideoToolbox | NVENC, QSV, AMF |
| VP8 | None | None |
| VP9 | None | QSV |
| AV1 | None | NVENC, QSV, AMF |

A candidate also needs a compatible GPU, driver, bit depth, and rate mode. Hardware mode retains VBR/CBR without switching to CPU or another rate mode; if every candidate fails, the error includes backend reasons. VP8 has no GPU option. Use CPU for VP9/AV1 on macOS. RTX 3090 Ti can encode H.264/HEVC but cannot encode VP8, VP9, or AV1 in hardware. AV1 decode support does not imply AV1 encode support. See the [official NVIDIA support matrix](https://developer.nvidia.com/video-encode-decode-support-matrix) for AV1 encoding capability.

QSV completion confirms accepted configuration and encoder initialization. The pinned runtime cannot provide independent evidence from a runtime rate-control query. Registered GPU candidates and passing software tests do not establish acceptance for a physical graphics card; confirm output, drivers, and HDR preservation on the target device.

## Choose container and audio

MKV is the default for VP8/VP9/AV1 and accommodates copied source audio. Choose `.webm` in the save dialog when needed. VP8 does not support MP4, and H.264/HEVC do not support WebM. VP9/AV1 playback in MP4 also depends on the target player's decoder.

Source-audio copy preserves compressed audio packets. AAC/Opus re-encode the selected audio track at **32–512 kbps**, default **192 kbps**. No audio produces a silent output; a source without an audio track does not gain one.

WebM audio supports only Opus or Vorbis. Copying either is allowed; AAC or copying another source format is rejected before video encoding. The app does not silently select Opus. Explicitly choose Opus, no audio, or MKV. Copying audio into other containers also receives an early muxing compatibility check. Timestamp incompatibilities report diagnostics instead of silently shifting the source timeline.

Final muxing also verifies sample trimming at both ends and source chapters. Export fails when copying between containers changes audio samples, or when MP4 cannot preserve chapter timing. Prefer MKV when retaining the original audio and chapters.

## Manage encoding presets

In **Settings → Encoding presets**, add or duplicate a preset, edit its name and encoding parameters, then save. Saving changes to an existing preset updates it. **Capture current settings** copies the active workbench configuration into a new, independent draft. When changing selection, leaving the page, or closing Settings, pending changes can be saved, restored, or kept by cancelling the transition.

The list supports multiple selection. **Export** writes selected saved presets into one `.aegiexports` file. **Import** accepts multiple exchange files, each of which may contain multiple presets. New files use version 2; version 1 presets migrate with their existing settings preserved. Older applications cannot read the new format. Invalid versions, parameters, or conflicting identities/names reject the whole import without partial changes. Exchange files contain no project, media, output, or tool paths.

Select a preset in the **Export** panel to apply it directly. Library edits and imports refresh the list while keeping current parameters and unfinished inputs until you explicitly select a preset. Preset management does not commit project drafts or change Undo.

## HDR and input requirements

Editing previews are SDR. PQ/HLG export reads original high-precision frames and uses 10-bit HEVC, VP9, or AV1 output. H.264/VP8 HDR is rejected. VideoToolbox HDR encoding on macOS is outside the current export contract; use CPU. Windows hardware HDR additionally depends on backend support and output metadata verification.

- PQ uses a 203-nit subtitle reference white by default. HLG uses 1000-nit, zero-black reference conditions.
- Complete static HDR10 mastering and source MaxCLL/MaxFALL are retained. These fields do not represent a new brightness measurement of the composited output.
- Before completion, the encoded video and final container are checked for actual codec, bit depth, color, and static HDR metadata. A mismatch prevents output commit.
- Dynamic HDR such as Dolby Vision/HDR10+, changing/incomplete mastering, and unsupported color semantics are rejected.
- Input must have supported integer planar YUV and square pixels; canvas dimensions must be even and match visible video. VFR retains source presentation timestamps.
- Rotation, stereo, ICC, interlace, corrupt/dynamic formats, and negative timestamps unsupported by the muxing path report errors.

For ASS/SRT files, use [Subtitle editing](subtitle-editing.md). Package requirements and verification are in [Publishing](publishing.md).
