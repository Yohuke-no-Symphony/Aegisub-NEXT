# Build and run

[English](building.md) · [简体中文](../zh-cn/building.md) · [All guides](README.md)

## Set up

Use .NET SDK **10.0.401** (stable patches in the same feature band) and **PowerShell 7.2+**. Native workbench builds support macOS `osx-arm64`/`osx-x64` and Windows `win-x64`; Windows ARM64 hosts target x64. Linux native media/runtime support is deferred.

Native builds need CMake, Ninja, a compiler, the shared FFmpeg SDK, and SDL3. macOS uses Homebrew and the Apple SDK; Windows uses Scoop and x64 MinGW. Versions are defined in `ffmpeg-toolchain.json` and native dependency manifests; a CLI-only FFmpeg distribution is insufficient. Current media locks are FFmpeg 9.0.2 and SDL3 3.4.16.

Use `-InstallDependencies` to prepare locked SDKs in `.dependencies/<RID>/<name>/<version>/`, where RID is `osx-arm64`, `osx-x64`, or `win-x64`. macOS builds FFmpeg, SDL3, libplacebo, Vulkan-Headers, Vulkan-Loader, shaderc, LittleCMS, x264, x265, dav1d, libvpx, SVT-AV1, and Opus from sources locked in `native/sdk-dependencies.json`, and downloads the official MoltenVK SDK. Added software encoding dependencies are **libvpx 1.17.0, SVT-AV1 4.2.0, and Opus 1.6.1**. Windows uses the pinned **Gyan FFmpeg 9.0.2 shared development SDK** and SDL3 MinGW SDK. Build tools remain system installations; missing supported tools are installed through Homebrew/Scoop.

On Windows, `Managed`/`Workbench` also check the MSVC x64 CRT required by native libraries such as ONNX Runtime. `-InstallDependencies` prepares pinned version **14.51.36247** from an official Microsoft package, storing DLLs and original notices in `.dependencies/win-x64/msvc-runtime/14.51.36247/`. A full Visual Studio installation is unnecessary; publishing discovers its `bin/` directory automatically. macOS ARM64/x64 use their own project SDKs and do not install the Windows CRT.

```powershell
# Both platforms: prepare media SDKs and build the Debug workbench
pwsh ./build-debug-native.ps1 -InstallDependencies
# macOS: prepare rendering SDKs and build the optional HDR module
pwsh ./build.ps1 -Target Native -InstallDependencies
```

The first source build downloads and compiles dependencies; `-Jobs` controls parallelism. Archives are verified against SHA256 and cached in `.dependencies/downloads/`. Installation receipts record recipe fingerprints and file checksums. Repeated installs reuse valid SDKs and repair managed SDKs when damaged or outdated. Staging, backups, validation, and rollback protect existing SDKs; retry recovers interrupted replacements. Failed build logs and sources remain under `.dependencies/.work/`. Unmanaged SDK directories and system library installations are preserved. The entire `.dependencies/` tree is ignored by Git.

The macOS FFmpeg recipe enables x264, x265 (8/10 bit), libvpx VP8/VP9, SVT-AV1, dav1d software AV1 decoding, AAC/Opus, MP4/Matroska/WebM, and system media, TLS, compression, and character conversion support, with third-party autodetection disabled. Built-in decoders also cover formats such as ProRes, VP8/VP9, MPEG-2/4, MJPEG, and FFV1. Environment checks require a `libdav1d` decoder in the selected SDK on both platforms. Project dynamic libraries link only project dependencies and system frameworks. Publishing collects the runtime dependency closure and original license/patent notices, including dav1d, libvpx, SVT-AV1, and Opus.

The `Export`/`Workbench` capability gate checks that the selected SDK includes libx264 8-bit, libx265 10-bit, VP8 8-bit, VP9/AV1 8/10-bit, AAC/Opus encoders, and all three muxers, with their declared pixel/sample formats. An older SDK with the same version can still fail if encoding features are absent. Update project-managed SDKs through `-InstallDependencies`; explicit SDKs must supply complete capabilities. Encoder registration, SDK short-clip checks, and actual export on the target device are separate validation layers. A registered hardware candidate does not establish physical GPU support.

Run from the repository root:

```powershell
pwsh -NoProfile -File ./build.ps1 -Target Workbench -CheckEnvironment
pwsh -NoProfile -File ./build.ps1 -Target Workbench -Configuration Release
```

`-CheckEnvironment` only checks: it does not install, restore, or build. `-InstallDependencies` aligns project SDKs. Explicit SDK parameters are preserved; incompatible selections fail validation. Project SDKs take precedence over package-manager environment variables; use `-FfmpegRoot` or `-SdlRoot` to select a custom SDK.

## Launch

```powershell
dotnet run --project src/AegiNext.Desktop/AegiNext.Desktop.csproj -c Release --runtime osx-arm64 -p:AegiNextRuntimeIdentifier=osx-arm64 --no-build
```

Replace both RIDs with `win-x64` on Windows or `osx-x64` on Intel Macs. Explicit Windows build:

```powershell
pwsh -NoProfile -File ./build.ps1 -Target Workbench -RuntimeIdentifier win-x64 -Configuration Release
```

Copy Koumori models manually to `models/koumori/` beside the executable, or select **Specify manually** in automatic timing settings and choose an existing model directory. Build, publish, and clean leave models alone; models stored at the repository root are not copied into the runtime directory. See [Publishing](publishing.md) for package paths.

## Debug with Rider

Open `AegiNext.sln`. Before the first Debug run or after native/ABI changes, build the matching libraries:

```powershell
pwsh -NoProfile -File ./build-debug-native.ps1
# Windows:
pwsh -NoProfile -File ./build-debug-native.ps1 -RuntimeIdentifier win-x64
```

Then run `AegiNext.Desktop` in Debug. Ordinary dotnet/Rider builds copy existing native output and do not compile missing libraries. Debug and Release are isolated; stop the old process after rebuilding. Export errors ANX1001/ANX1002 indicate absent/stale native contract metadata: rebuild the matching Workbench.

## Build options

| Target / option | Purpose |
|---|---|
| `Managed`, default | Build managed projects and copy existing native output |
| `Workbench` | Decoder → Audio → Export → Managed |
| `Decoder`, `Audio`, `Export` | Build one independent native component |
| `Native`, `All` | Optional macOS HDR diagnostic; All adds Managed, not Workbench |
| `-FfmpegRoot`, `-SdlRoot` | Explicit shared SDK locations |
| `-WithMediaTools` | Check PATH FFmpeg/FFprobe for a managed build |
| `-ReportPath` | Save an environment report |

FFmpeg SDK resolution: explicit root → project SDK → `FFMPEG_DIR` → package-manager prefix. SDL uses explicit root → project SDK → `SDL3_DIR` → package-manager prefix. Invalid explicit paths fail. Windows native builds stage the selected SDK's ffmpeg, ffprobe, and companion DLLs into `tools/`; managed builds copy them into the application output, so development runs need no additional PATH configuration. Development tools can be overridden with absolute `AEGINEXT_FFMPEG_PATH`/`AEGINEXT_FFPROBE_PATH` or found on PATH; complete packages use their own tools.

Native output is `artifacts/native/<RID>/<Configuration>/`; intermediate managed files use `obj/<RID>`. Workbench restores, builds, and tests for the host target RID by default, keeping the media TestHost consistent with native output. NuGet versions live in `Directory.Packages.props`, with ordinary restore and no package lock files. Release builds use `AegiNext.Product.slnf`; Debug uses `AegiNext.sln`.

## Build versions

Managed builds derive their version from release tags on the current Git commit. dotnet, Rider, and PowerShell builds share the same rules. `rel/beta/1.2.3.1` produces product version `1.2.3-beta.1`; `rel/stable/1.2.3.1` produces `1.2.3`. The fourth component is an independent release sequence within each channel, recorded in the Git tag. Stable sequences are also retained in assembly metadata.

The system build number is `git rev-list --count HEAD`: the current commit and all its ancestors. Building the same commit on another platform or retrying it keeps the same number; builds do not write version files or increment a counter. The first three components and release sequence range from `0` to `65534`; the system build number must not exceed `65534`. Assembly version is fixed at `x.y.z.0`, while file version uses `x.y.z.<system build number>`.

Use `build.ps1 -Tag` to select a release tag pointing to HEAD. Optional `-Version` must have four numeric components and match the tag. With dotnet directly, use `-p:AegiNextRequestedTag=rel/beta/1.2.3.1` and `-p:AegiNextRequestedVersion=1.2.3.1` respectively.

Ordinary development builds use `0.1.0-dev` when HEAD has no valid release tag or complete Git history is unavailable. Ordinary release packaging requires a full clone and a valid release tag pointing to HEAD; select a tag explicitly when HEAD has multiple distinct release identities. Environment-only checks and native-only builds do not require a release identity. See [Publishing](publishing.md#versions-and-increments) for tag creation, selection, and release sequence increments.

## Run focused tests

```powershell
pwsh -NoProfile -File ./build.ps1 -RunTests -TestProjects SubtitleFormats
pwsh -NoProfile -File ./build.ps1 -RunTests -TestProjects Cli
pwsh -NoProfile -File ./build.ps1 -RunTests -TestProjects Application
pwsh -NoProfile -File ./build.ps1 -Target Workbench -RunTests -TestProjects Export
pwsh -NoProfile -File ./build.ps1 -RunTests -TestProjects 'Desktop.Ui'
dotnet test Tests/AegiNext.Core.Tests/AegiNext.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~EffectScript'
```

Valid project filters: Core, Application, SubtitleFormats, Cli, Rendering, Media, Export, Desktop, Desktop.Ui. No tests run without `-RunTests`; Workbench testing also runs the corresponding native CTest suites. Release restores only selected test projects. Independent subtitle conversion tests belong in `Tests/AegiNext.SubtitleFormats.Tests`; editor integration tests remain in Application. Export settings, presets, ABI, and worker contracts belong in `Tests/AegiNext.Export.Tests`; desktop entry points and controls remain in Desktop/Desktop.Ui tests.

Real media/device tests require matching native output and explicit switches: `AEGINEXT_RUN_DECODER_TESTS=1`, `AEGINEXT_RUN_AUDIO_TESTS=1`, and for the actual system device `AEGINEXT_RUN_SYSTEM_AUDIO_TESTS=1`, plus real tool paths. Otherwise native cases skip. See [Media](media.md).

Real export tests additionally require all three absolute file paths: `AEGINEXT_EXPORT_WORKER_PATH`, `AEGINEXT_FFMPEG_PATH`, and `AEGINEXT_FFPROBE_PATH`. They skip if any is absent. First build Workbench with the matching configuration; for example, run software export checks for macOS arm64 in PowerShell:

```powershell
$codecTestRid = 'osx-arm64'
$codecTestSdk = (Resolve-Path ".dependencies/$codecTestRid/ffmpeg/9.0.2").Path
$toolSuffix = if ($IsWindows) { '.exe' } else { '' }
$env:AEGINEXT_EXPORT_WORKER_PATH = (Resolve-Path "src/AegiNext.ExportWorker/bin/Release/net10.0/$codecTestRid/aegn-exporter.dll").Path
$env:AEGINEXT_FFMPEG_PATH = "$codecTestSdk/bin/ffmpeg$toolSuffix"
$env:AEGINEXT_FFPROBE_PATH = "$codecTestSdk/bin/ffprobe$toolSuffix"
dotnet test Tests/AegiNext.Export.Tests/AegiNext.Export.Tests.csproj -c Release --runtime $codecTestRid -p:AegiNextRuntimeIdentifier=$codecTestRid --filter 'FullyQualifiedName~VideoRateControlExportTests'
```

Use `win-x64` on Windows or `osx-x64` on Intel Macs. Paths must identify the built worker and selected SDK. Software encoding/decoding and package relocation checks do not replace physical NVENC/QSV/AMF, audible playback, or desktop visual acceptance.

Build-script QA:

```powershell
pwsh -NoProfile -File ./scripts/test-build.ps1 -RestoreTools
pwsh -NoProfile -File ./scripts/test-build.ps1
```

The first command restores pinned QA tools to project artifacts. Headless UI and silent audio tests do not establish native appearance or audible latency. Package the result with [Publishing](publishing.md).

## Profile video export

Set `AEGINEXT_EXPORT_PROFILE=1` before starting `aegn-exporter` to write an `AEGINEXT_EXPORT_PROFILE` JSON line to stderr after a successful native export. It reports frame count, native elapsed time, decoder/download time, rendering, overlay coverage, YUV resampling, composition, encoding, and muxing. Overlay counters distinguish updates, retained revisions, active pixels, and bounded foreground color preparation. The accompanying `AEGINEXT_RENDER_PROFILE` line separates evaluation, drawing, and F32 copying, and reports copied bytes and scratch-surface allocations/reuse. The normal worker protocol remains on stdout. Without the switch, stage clocks are disabled.

Capture worker stderr directly when diagnosing performance; the desktop exporter consumes it. Measure complete export wall time separately, including worker startup and final remux. Use the same media, project snapshot, configuration, and build mode when comparing FPS. Internal resampling and composition use at most four threads per stage; GPU encoding can still be limited by CPU composition.

Export ABI 6 continues to report updated, unchanged, or empty subtitle foreground. Native export retains its F32 buffer and reuses prepared coverage for stable revisions. Consecutive updates use the scalar composition path without rebuilding coverage; the first subsequent unchanged frame prepares coverage once. Video backgrounds still pass through the same full-frame YUV resampling. Rebuild the application and worker together after an ABI change; see [Architecture](architecture.md#video-export-boundaries) for contract sizes and versions.

One-frame decode prefetch is automatic after the first frame confirms the VideoToolbox hardware backend. Software and other backends remain serial by default. Set `AEGINEXT_EXPORT_PREFETCH=0` to disable prefetch or `1` to force it for any backend; other explicit values also select serial decoding. The producer owns decoding and snapshots frame color metadata before publication. Rendering and encoding remain on the export thread. `read_ms` measures decoder service time, which may overlap later stages; `prefetch_wait_ms` measures the export thread's actual wait. Compare complete wall time rather than adding overlapping stage durations. The profiler reports the queue capacity and peak queued/in-flight frames.

On macOS, a native build with `BUILD_TESTING=ON` also creates `aeginext_export_metal_tests`. Run it with `--benchmark 8` to measure the experimental SDR Metal compositor, including uploads, synchronization, readback, and the same CPU resampling. This standalone research target does not enable GPU composition in normal exports. It reports numerical differences against the CPU oracle; any difference fails the default bit-exact activation requirement. HDR and decoder/encoder GPU-surface interoperability are outside this prototype.
