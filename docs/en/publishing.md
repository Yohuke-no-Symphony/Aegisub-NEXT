# Publish packages

[English](publishing.md) · [简体中文](../zh-cn/publishing.md) · [All guides](README.md)

## Create a release

Run on the target operating system:

```powershell
pwsh -NoProfile -File ./release.ps1
```

This builds Release and creates a macOS DMG for the host architecture or a Windows x64 NSIS installer. Output goes to a fresh `artifacts/releases/<RID>/<Configuration>/<timestamp>-<ID>/` directory and is printed on success. Windows ARM64 hosts target x64.

```powershell
pwsh -NoProfile -File ./release.ps1 -Version 1.2.3 -SkipBuild
```

Version defaults to `Directory.Build.props`. `-SkipBuild` reuses matching native output and still publishes the desktop app, export worker, and CLI. Use explicit `-FfmpegRoot`, `-SdlRoot`, or `-NsisPath` when needed; output must be a new directory. Publishing does not install dependencies.

## Create a portable package

```powershell
# macOS:
pwsh -NoProfile -File ./publish.ps1 -RuntimeIdentifier osx-arm64 -Configuration Release -OutputDirectory ./artifacts/releases/mac-local
# Windows:
pwsh -NoProfile -File ./publish.ps1 -RuntimeIdentifier win-x64 -Configuration Release -FfmpegRoot C:/SDK/ffmpeg-shared -SdlRoot C:/SDK/SDL3 -OutputDirectory ./artifacts/releases/windows-local
```

Add `-CreateDmg` on macOS or `-CreateInstaller` on Windows. NSIS requires 3.11+; `-NsisPath` requires installer creation. Packaging runs on the corresponding OS.

Install NSIS on Windows with Scoop before creating an installer:

```powershell
scoop bucket add extras
scoop install nsis
```

## Package contents

The complete payload includes self-contained .NET, the desktop app, `AegiNext.Export`, `aegn-exporter`, `aegnc` and its complete Spectre dependencies, FFmpeg/FFprobe, native modules, recursive runtime dependencies, original license/patent notices, and the entire `i18n/` language-resource directory copied from the publish output. The export assembly, worker, and native export library must share a configuration and matching ABI contract fingerprint. Replacing only one can fail contract validation.

Spectre's pinned upstream license and package notice are stored in `scripts/publish/licenses/`; update them together when upgrading the packages in `Directory.Packages.props`.

- `media-runtime.json` selects package-local tools.
- `package-manifest.json` records version, RID, Git identity/dirty state, runtime/tool versions, dependency/license origins, actual OS requirements, and file hashes.
- macOS rewrites Mach-O dependency paths and signs the bundle; the highest native-library requirement becomes the minimum OS version. Default signing is ad-hoc; notarization is separate.
- Windows collects x64 PE dependencies from selected SDK/compiler locations. Use `-RuntimeDependencyDirectory` and `-LicenseDirectory` for explicit additional sources.

The selected FFmpeg SDK must provide H.264, HEVC, VP8, VP9, SVT-AV1, AAC/Opus, and MP4/Matroska/WebM. VP9/AV1 must accept both 8-bit and 10-bit output. macOS uses libvpx 1.17.0, SVT-AV1 4.2.0, and Opus 1.6.1 from the project source SDK, collecting these libraries and their runtime dependencies recursively. Windows uses the pinned Gyan 9.0.2 shared distribution. The dependency manifest locks sources, hashes, recipes, and license origins; see [Building](building.md) for preparation. System build tools may come from Homebrew/Scoop, while shipped encoding capabilities must come from the selected SDK.

## Installers and icons

The Windows wizard supports current-user and all-users scopes; current-user installation defaults to `%LOCALAPPDATA%\Programs\AegiNext`. Desktop shortcut creation is optional. Close the app, worker, and CLI before reinstalling/uninstalling. Reinstallation removes obsolete owned files; uninstall preserves extra files and personal preferences.

Silent install accepts `/S /CurrentUser` or `/S /AllUsers`; add `/DesktopShortcut` if desired and place an unquoted `/D=...` last. Synchronous automated uninstall uses the matching scope and `_?=...` last.

Regenerate platform icons on macOS:

```powershell
pwsh -NoProfile -File ./scripts/assets/export-app-icons.ps1 -SourcePng ./src/AegiNext.Desktop/Assets/AppIcon.Source.png
```

## Verify and relocate

```powershell
pwsh -NoProfile -File ./scripts/publish/verify-package.ps1 -PackageDirectory ./artifacts/releases/mac-local
```

Verification checks complete file hashes and language-pack structure/encoding. New packages use manifest schema 2, additionally requiring desktop, worker, and CLI entry points with a self-contained .NET 10 runtime; schema 1 retains its existing verification contract. Publishing directly runs the packaged CLI help, version, and an SRT-to-ASS conversion on the host. Move the entire package; macOS copies/archives must retain extended attributes for signing. Use Finder or `ditto`, then verify the moved bundle's signature.

On each target OS, check startup, media opening, audible playback, subtitle editing, actual export/cancel, and relocated operation without developer tools on PATH. Use the packaged worker and tools for export checks, covering at least these distinct capabilities:

| Check | Required result |
|---|---|
| VP8, VP9, AV1 software export | Actual encoder, complete frame count, decodable output; check VP9/AV1 at both 8-bit and 10-bit |
| CQ/CRF, VBR, CBR | Requested mode and parameters match the receipt; CQ also confirms target bitrate. Short-clip average bitrate is not rate-control evidence |
| MKV, WebM, MP4 and audio | Supported combinations, source-audio copy, AAC/Opus, no audio; incompatible WebM audio is rejected before video encoding |
| PQ, HLG, static HDR10 | Final-container 10-bit output, color labels, mastering/content-light, and subtitle brightness matching reference conditions |
| Relocated independent execution | Tools still start and encode/decode without developer tools on PATH; original license/patent notices remain complete |
| Windows hardware export | VP9 QSV and AV1 NVENC/QSV/AMF on compatible physical GPUs, distinguishing initialization confirmation from runtime-query evidence |

Passing SDK software clips and dependency-closure relocation still leaves complete-application export, cancellation, and error feedback to check. Software output in a Windows VM does not establish physical GPU support. macOS has no added VP8/VP9/AV1 hardware encoding backend. Installer scopes, updates, native menus, DPI, HDR display, and export-panel appearance require their corresponding runtime/visual checks. QSV currently reports `ConfiguredAndInitialized`, not `RuntimeQueried`.
