# Publish packages

[English](publishing.md) · [简体中文](../zh-cn/publishing.md) · [All guides](README.md)

## Create a release

Start from a full Git clone, complete the release changes, and create a commit before tagging it. Run on the target operating system:

```powershell
git tag rel/beta/1.2.3.1
pwsh -NoProfile -File ./release.ps1 -Tag rel/beta/1.2.3.1
```

This builds Release and creates a macOS DMG for the host architecture or a Windows x64 NSIS installer. Output goes to a fresh `artifacts/releases/<RID>/<timestamp>-<ID>/` directory and is printed on success. Windows ARM64 hosts target x64.

```powershell
pwsh -NoProfile -File ./release.ps1 -Tag rel/beta/1.2.3.1 -Version 1.2.3.1 -SkipBuild
```

For ordinary publishing, `-Tag` must point to HEAD. When omitted, a valid release tag is selected from HEAD; distinct release identities require explicit selection. Ordinary publishing fails without a valid tag, usable Git, or complete history. Optional `-Version` must have four numeric components and match the selected tag; it validates the version instead of overriding the tag.

`-SkipBuild` reuses matching native output and still publishes the desktop app, export worker, and CLI. Use explicit `-FfmpegRoot`, `-SdlRoot`, or `-NsisPath` when needed; output must be a new directory. Publishing does not install dependencies. Scripts build and package locally; the publisher creates commits, creates/pushes tags, and uploads GitHub Releases.

## Test packages without a tag

To test packaging before creating a release tag, provide the intended valid tag name explicitly and use `--force`. The PowerShell spelling `-Force` is also supported:

```powershell
pwsh -NoProfile -File ./release.ps1 -Tag rel/beta/1.2.3.99 --force
```

This mode requires an explicit `-Tag` and packages the current HEAD without creating or moving Git tags. It still rejects an existing tag that points to another commit. Tag format, the `0–65534` range of each numeric component, optional four-component `-Version` matching, and complete Git history remain required.

Test packages retain manifest schema 2 and record `IsTestPackage: true`; ordinary publishing records `false`. Manifest `GitSha` and the system build number still come from the actual HEAD. Output still uses a fresh directory under `artifacts/releases/<RID>/<timestamp>-<ID>/`.

## Versions and increments

Git tags record release identity. The app and CLI display the product version; platform metadata uses a separate numeric build number:

| Git tag | App / CLI display | Channel release sequence |
|---|---|---|
| `rel/beta/1.2.3.1` | `1.2.3-beta.1` | beta sequence 1 |
| `rel/stable/1.2.3.1` | `1.2.3` | stable sequence 1 |

The fourth component does not use a `1000` threshold. The first three components and release sequence range from `0` to `65534`; historical sequence `0` remains valid. Increment the fourth component for the next release of the same base version and channel. Inspect existing sequences with:

```powershell
git tag --list 'rel/beta/1.2.3.*' --sort=-v:refname
```

For example, create a new commit on the same mainline before tagging the next beta as `rel/beta/1.2.3.2`. Use `rel/stable/1.2.3.1` for the stable release and `.2` for its next release. Every new release uses a new commit. Cross-platform builds and failed-build retries of the same commit keep the original tag.

The system build number is `git rev-list --count HEAD`, including HEAD and all its ancestors, with a maximum of `65534`. Complete commit history records this number; no counter file is needed. Builds do not create commits or write numbers back. New descendant commits on the same mainline increase the number; the same commit keeps the same number on every platform.

| Metadata | Value |
|---|---|
| `InformationalVersion`, product display version | `1.2.3-beta.1` or `1.2.3` |
| `AssemblyVersion` | `1.2.3.0` |
| Windows `FileVersion`, numeric installer version | `1.2.3.<system build number>` |
| macOS `CFBundleShortVersionString` | `1.2.3` |
| macOS `CFBundleVersion` | System build number |

Stable sequences remain in assembly metadata and the package manifest even though they are not displayed. The desktop app, worker, and CLI share one version snapshot across restore, build, and publish.

Update comparison orders the first three components, then the channel (stable is newer than every beta of the same base version), then the sequence within that channel. The GitHub Release `prerelease` flag still controls channel candidates. Set it for beta uploads and clear it for stable uploads.

Existing beta tags use sequence `0`. Use `.1` for the first beta of the same base version after migration so the old numeric updater can discover it. Old clients cannot recognize the `beta.1 → stable.1` channel promotion; after installing a release with the new updater, subsequent comparisons follow the channel rules above.

## Create a portable package

```powershell
# macOS:
pwsh -NoProfile -File ./publish.ps1 -Tag rel/beta/1.2.3.1 -RuntimeIdentifier osx-arm64 -Configuration Release -OutputDirectory ./artifacts/releases/mac-local
# Windows:
pwsh -NoProfile -File ./publish.ps1 -Tag rel/beta/1.2.3.1 -RuntimeIdentifier win-x64 -Configuration Release -FfmpegRoot C:/SDK/ffmpeg-shared -SdlRoot C:/SDK/SDL3 -OutputDirectory ./artifacts/releases/windows-local
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

Manually place the complete Koumori runtime package in the repository's `models/koumori/` directory. Normal `dotnet build`, `dotnet publish`, `publish.ps1`, and `release.ps1` validate the manifest and every SHA-256 digest, then copy only the declared runtime files to `models/koumori/` beside the executable. The build synchronizes that owned output directory so files from an older package do not remain after replacing or removing the source model. An empty source directory or its README alone is allowed for checkouts without the model; a partial or invalid package fails the build. No training, model download, or neighboring training-repository discovery occurs. `-KoumoriModelDirectory <runtime-package-directory>` remains available on the publishing scripts to override the source for that run; direct MSBuild calls can use `-p:AegiNextKoumoriModelDirectory=<runtime-package-directory>`. The published package manifest records the deployed model ID and manifest digest, and offline verification checks this metadata against the actual payload.

Koumori supports `win-x64` and `osx-arm64`. Schemas 1/2/3 contain 4/7/8 runtime files respectively. Schema 3 adds `snap-model.onnx`: generation and snapping share one encoder while using separate output graphs. Keep `manifest.json`, the referenced ONNX files, frontend/decoder configuration, and the encoder license together; checkpoint and training files are not deployed. Training status and quality-acceptance metadata do not control packaging and are preserved as supplied. The updated runtime accepts all three schemas; older runtimes cannot load schema 3, so rolling back the application also requires a compatible model package. Automatic timing settings offer **Built-in** and **Specify manually**, with the bundled model selected by default. Without a bundled model, supply it before analysis or select a custom directory. ONNX Runtime CPU is pinned to 1.31.0; its Apple Silicon native library requires macOS 14.0 or later, which also raises the application bundle minimum when this library is shipped. Native Windows and macOS acceptance still requires testing on each target OS.

- `media-runtime.json` selects package-local tools.
- `package-manifest.json` records product version, original tag, channel, release sequence, system build number, the `IsTestPackage` flag, RID, Git identity/dirty state, runtime/tool versions, dependency/license origins, actual OS requirements, and file hashes.
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

Verification checks complete file hashes and language-pack structure/encoding. New packages continue to use manifest schema 2, requiring desktop, worker, and CLI entry points with a self-contained .NET 10 runtime, and check that release identity matches the product display version. Historical schema 1/2 packages with three- or four-component numeric versions retain offline verification support. Publishing directly runs the packaged CLI help, version, and an SRT-to-ASS conversion on the host. Move the entire package; macOS copies/archives must retain extended attributes for signing. Use Finder or `ditto`, then verify the moved bundle's signature.

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
