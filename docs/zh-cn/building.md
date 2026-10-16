# 构建与运行

[English](../en/building.md) · [简体中文](building.md) · [全部指南](README.md)

## 配置环境

使用 .NET SDK **10.0.401**（允许同 feature band 的稳定补丁）和 **PowerShell 7.2+**。原生工作台支持 macOS `osx-arm64`/`osx-x64`、Windows `win-x64`；Windows ARM64 宿主仍以 x64 为目标。Linux 原生媒体与运行验收暂未支持。

原生构建需要 CMake、Ninja、编译器、共享 FFmpeg SDK 和 SDL3。macOS 使用 Homebrew 与 Apple SDK，Windows 使用 Scoop 和 x64 MinGW。版本由 `ffmpeg-toolchain.json` 及原生依赖清单定义，只有命令行工具的 FFmpeg 包不够；当前媒体锁定 FFmpeg 9.0.2、SDL3 3.4.16。

使用 `-InstallDependencies` 自动在项目的 `.dependencies/<RID>/<库名>/<版本>/` 准备锁定 SDK；RID 为 `osx-arm64`、`osx-x64` 或 `win-x64`。macOS 按 `native/sdk-dependencies.json` 中锁定的源码构建 FFmpeg、SDL3、libplacebo、Vulkan-Headers、Vulkan-Loader、shaderc、LittleCMS、x264、x265、dav1d、libvpx、SVT-AV1 和 Opus，并下载官方 MoltenVK SDK。新增软件编码依赖锁定为 **libvpx 1.17.0、SVT-AV1 4.2.0、Opus 1.6.1**。Windows 使用锁定的 **Gyan FFmpeg 9.0.2 共享开发包**和 SDL3 MinGW 开发包。CMake、Ninja、Meson、pkgconf、编译器等构建工具继续使用系统安装，缺失且可自动安装的工具由 Homebrew/Scoop 补齐。

```powershell
# 两个平台：安装媒体 SDK，并构建 Debug 工作台
pwsh ./build-debug-native.ps1 -InstallDependencies
# macOS：安装渲染 SDK，并构建可选 HDR 原生模块
pwsh ./build.ps1 -Target Native -InstallDependencies
```

首次源码构建需要下载和编译，`-Jobs` 控制并行数。下载包按 SHA256 校验后保存在 `.dependencies/downloads/`，安装记录保存配方指纹和文件校验值；重复执行复用有效 SDK，受管理的损坏或过期 SDK 会重新准备。安装采用临时目录、替换前备份和验证失败回滚，中断后重试可恢复已有备份；失败日志和源码保留在 `.dependencies/.work/`。不覆盖未带项目安装记录的手动 SDK，也不卸载或切换系统库版本。整个 `.dependencies/` 不提交 Git。

macOS 的 FFmpeg 显式启用 x264、x265（8/10 bit）、libvpx 的 VP8/VP9、SVT-AV1、dav1d 软件 AV1 解码、AAC/Opus、MP4/Matroska/WebM 及系统音视频、TLS、压缩和字符转换支持，关闭系统第三方库自动探测。内置解码器还覆盖 ProRes、VP8/VP9、MPEG-2/4、MJPEG、FFV1 等格式。两个平台的环境检查均要求所选 SDK 提供 `libdav1d` 解码器。项目动态库只链接项目依赖和系统框架，发布收集包括 dav1d、libvpx、SVT-AV1、Opus 在内的运行时依赖闭包及原始许可证、专利声明。

`Export`/`Workbench` 的环境门禁检查 libx264 8-bit、libx265 10-bit、VP8 8-bit、VP9/AV1 8/10-bit、AAC/Opus 编码器及三个封装器是否已编入所选 SDK，并检查声明的像素/采样格式。版本相同但缺少编码功能的旧 SDK 仍会失败；受项目管理的 SDK 通过 `-InstallDependencies` 更新，显式指定的 SDK 需要自行提供完整能力。编码器注册、SDK 短片检查与目标设备实际导出属于不同验证层，硬件候选已注册不代表实体显卡可用。

在仓库根目录执行：

```powershell
pwsh -NoProfile -File ./build.ps1 -Target Workbench -CheckEnvironment
pwsh -NoProfile -File ./build.ps1 -Target Workbench -Configuration Release
```

使用 `-CheckEnvironment` 只检查，不安装、不还原、不构建。`-InstallDependencies` 对齐项目 SDK；显式参数指定的 SDK 始终保留，版本不匹配会报错。项目 SDK 优先于包管理器设置的环境变量；自定义 SDK 请使用 `-FfmpegRoot` 或 `-SdlRoot`。

## 启动

```powershell
dotnet run --project src/AegiNext.Desktop/AegiNext.Desktop.csproj -c Release --runtime osx-arm64 -p:AegiNextRuntimeIdentifier=osx-arm64 --no-build
```

Windows 将两个 RID 换成 `win-x64`，Intel Mac 换成 `osx-x64`。Windows 显式构建：

```powershell
pwsh -NoProfile -File ./build.ps1 -Target Workbench -RuntimeIdentifier win-x64 -Configuration Release
```

## Rider 调试

打开 `AegiNext.sln`。首次 Debug 调试前或修改 native/ABI 后，构建匹配的原生库：

```powershell
pwsh -NoProfile -File ./build-debug-native.ps1
# Windows:
pwsh -NoProfile -File ./build-debug-native.ps1 -RuntimeIdentifier win-x64
```

随后以 Debug 运行 `AegiNext.Desktop`。普通 dotnet/Rider 构建只复制已有产物，不编译缺失的原生库；Debug/Release 相互隔离，重建后停止旧进程。压制错误 ANX1001/ANX1002 表示原生契约元数据缺失/过期，重建对应 Workbench。

## 构建选项

| 目标 / 参数 | 用途 |
|---|---|
| `Managed`，默认 | 构建托管项目并复制已有原生产物 |
| `Workbench` | Decoder → Audio → Export → Managed |
| `Decoder`、`Audio`、`Export` | 单独构建对应原生模块 |
| `Native`、`All` | 可选 macOS HDR 诊断；All 加 Managed，不含 Workbench |
| `-FfmpegRoot`、`-SdlRoot` | 显式共享 SDK 路径 |
| `-WithMediaTools` | 托管构建时检查 PATH FFmpeg/FFprobe |
| `-ReportPath` | 保存环境报告 |

FFmpeg SDK 按显式路径 → 项目 SDK → `FFMPEG_DIR` → 包管理器前缀解析；SDL 按显式路径 → 项目 SDK → `SDL3_DIR` → 包管理器前缀解析。显式路径无效即失败。Windows 原生构建将所选 SDK 的 ffmpeg、ffprobe 和配套 DLL 暂存到 `tools/`，托管构建自动复制到应用输出目录，开发运行无需额外配置 PATH。开发工具可通过绝对 `AEGINEXT_FFMPEG_PATH`/`AEGINEXT_FFPROBE_PATH` 覆盖，也可从 PATH 查找；完整应用包使用包内工具。

原生产物位于 `artifacts/native/<RID>/<Configuration>/`，托管中间文件使用 `obj/<RID>`。Workbench 默认按本机目标 RID 还原、构建和测试，确保媒体 TestHost 与原生产物一致。NuGet 版本集中在 `Directory.Packages.props`，普通还原且不维护 lock 文件。Release 构建使用 `AegiNext.Product.slnf`，Debug 使用 `AegiNext.sln`。

## 构建版本

托管构建统一从当前 Git 提交的发布标签解析版本，dotnet、Rider 与 PowerShell 构建使用同一规则。`rel/beta/1.2.3.1` 的产品版本为 `1.2.3-beta.1`，`rel/stable/1.2.3.1` 为 `1.2.3`；第四段是各渠道独立的发布序号，记录在 Git 标签中，正式版的序号也保留在程序集元数据中。

系统构建号为 `git rev-list --count HEAD`，即当前提交及其完整祖先的数量。同一提交在不同平台构建或重试时编号一致，构建不会写回版本文件或递增计数器。前三段与发布序号范围为 `0–65534`，系统构建号不得超过 `65534`。程序集版本固定为 `x.y.z.0`，文件版本使用 `x.y.z.<系统构建号>`。

`build.ps1` 可用 `-Tag` 选择指向 HEAD 的发布标签；可选 `-Version` 必须为四段数字并与标签一致。直接使用 dotnet 时，对应参数为 `-p:AegiNextRequestedTag=rel/beta/1.2.3.1` 和 `-p:AegiNextRequestedVersion=1.2.3.1`。

HEAD 没有合法发布标签，或没有可用的完整 Git 历史时，普通开发构建使用 `0.1.0-dev`。正式打包要求完整克隆和指向 HEAD 的合法发布标签；多个不同发布身份同时指向 HEAD 时，需要显式选择标签。纯环境检查与纯原生构建不依赖发布身份。创建标签、选择标签与发布序号的递增方式见[发布](publishing.md#版本与递增)。

## 定向测试

```powershell
pwsh -NoProfile -File ./build.ps1 -RunTests -TestProjects SubtitleFormats
pwsh -NoProfile -File ./build.ps1 -RunTests -TestProjects Cli
pwsh -NoProfile -File ./build.ps1 -RunTests -TestProjects Application
pwsh -NoProfile -File ./build.ps1 -Target Workbench -RunTests -TestProjects Export
pwsh -NoProfile -File ./build.ps1 -RunTests -TestProjects 'Desktop.Ui'
dotnet test Tests/AegiNext.Core.Tests/AegiNext.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~EffectScript'
```

项目筛选支持 Core、Application、SubtitleFormats、Cli、Rendering、Media、Export、Desktop、Desktop.Ui。未指定 `-RunTests` 不运行测试；Workbench 测试也执行对应的原生 CTest 套件。Release 仅还原选中的测试项目。独立字幕转换测试位于 `Tests/AegiNext.SubtitleFormats.Tests`，编辑器集成测试留在 Application。导出参数、预设、ABI 与 worker 合同测试位于 `Tests/AegiNext.Export.Tests`，桌面入口与控件行为留在 Desktop/Desktop.Ui 测试。

真实媒体/设备测试需要匹配原生产物、实际工具路径和显式开关：`AEGINEXT_RUN_DECODER_TESTS=1`、`AEGINEXT_RUN_AUDIO_TESTS=1`；系统设备额外需要 `AEGINEXT_RUN_SYSTEM_AUDIO_TESTS=1`。否则原生用例跳过，详见[媒体](media.md)。

真实导出测试另需同时提供 `AEGINEXT_EXPORT_WORKER_PATH`、`AEGINEXT_FFMPEG_PATH`、`AEGINEXT_FFPROBE_PATH` 三个绝对文件路径，否则真实导出用例跳过。先构建同配置 Workbench；例如在 PowerShell 中运行 macOS arm64 软件导出检查：

```powershell
$codecTestRid = 'osx-arm64'
$codecTestSdk = (Resolve-Path ".dependencies/$codecTestRid/ffmpeg/9.0.2").Path
$toolSuffix = if ($IsWindows) { '.exe' } else { '' }
$env:AEGINEXT_EXPORT_WORKER_PATH = (Resolve-Path "src/AegiNext.ExportWorker/bin/Release/net10.0/$codecTestRid/aegn-exporter.dll").Path
$env:AEGINEXT_FFMPEG_PATH = "$codecTestSdk/bin/ffmpeg$toolSuffix"
$env:AEGINEXT_FFPROBE_PATH = "$codecTestSdk/bin/ffprobe$toolSuffix"
dotnet test Tests/AegiNext.Export.Tests/AegiNext.Export.Tests.csproj -c Release --runtime $codecTestRid -p:AegiNextRuntimeIdentifier=$codecTestRid --filter 'FullyQualifiedName~VideoRateControlExportTests'
```

Windows 将 RID 改为 `win-x64`，Intel Mac 改为 `osx-x64`。这些路径必须对应已构建的 worker 与所选 SDK。软件编码/解码及包搬移检查不能替代 NVENC/QSV/AMF 实体硬件、可听播放或桌面视觉验收。

构建脚本 QA：

```powershell
pwsh -NoProfile -File ./scripts/test-build.ps1 -RestoreTools
pwsh -NoProfile -File ./scripts/test-build.ps1
```

首条命令将固定版本 QA 工具还原到工程产物目录。Headless UI 和静音音频测试不能证明原生外观或可听延迟；应用打包见[发布](publishing.md)。

## 压制性能计时

启动 `aegn-exporter` 前设置 `AEGINEXT_EXPORT_PROFILE=1`，原生导出成功后会向 stderr 写入以 `AEGINEXT_EXPORT_PROFILE` 开头的 JSON，记录帧数、原生总耗时、解码/下载、渲染、前景范围构建、YUV 重采样、合成、编码和封装耗时。前景计数区分更新、版本复用、有效像素和有界颜色预计算。配套的 `AEGINEXT_RENDER_PROFILE` 分别记录求值、绘制、F32 复制耗时，以及复制字节数和临时表面的分配／复用次数。正常 worker 协议仍使用 stdout；未设置开关时不读取阶段时钟。

诊断时直接捕获 worker stderr，桌面导出器会消费该输出。另行测量包含 worker 启动和最终封装的完整导出时间，比较 FPS 时使用相同素材、工程快照、配置和构建模式。内部重采样与合成每阶段最多使用四个线程；GPU 编码仍可能受 CPU 合成速度限制。

导出 ABI 6 继续显式报告字幕前景已更新、未变化或为空。原生导出保留 F32 缓冲，并为稳定版本复用预计算的有效范围。连续更新时直接使用原有合成内核，省去有效范围构建；首次恢复为未变化状态时再构建一次。视频背景仍执行相同的全帧 YUV 重采样。ABI 变化后必须一起重新构建应用和 worker，合同尺寸和版本见[架构](architecture.md#视频导出边界)。

首帧确认 VideoToolbox 硬件后端后，自动启用一帧解码预取；软件及其他后端默认串行。设置 `AEGINEXT_EXPORT_PREFETCH=0` 可关闭预取，`1` 可在任何后端强制启用；其他显式值也使用串行解码。生产线程独占解码，并在发布前固定帧的颜色信息；渲染与编码仍在导出线程执行。`read_ms` 表示解码服务耗时，可能与后续阶段重叠；`prefetch_wait_ms` 表示导出线程的实际等待耗时。比较完整导出时间，不要累加重叠阶段的时长。诊断计数包含队列容量和排队／读取中的峰值帧数。

macOS 原生构建启用 `BUILD_TESTING=ON` 后还会生成 `aeginext_export_metal_tests`。使用 `--benchmark 8` 测量实验性 SDR Metal 合成，计入上传、同步、回读及相同的 CPU 重采样。这个独立研究目标不会启用常规导出的 GPU 合成；它报告与 CPU 基线的数值差异，任何差异都不满足默认逐像素一致的启用要求。HDR 及解码／编码 GPU 表面的互操作尚不在原型范围内。
