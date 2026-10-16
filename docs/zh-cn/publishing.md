# 发布打包

[English](../en/publishing.md) · [简体中文](publishing.md) · [全部指南](README.md)

## 生成发布包

从完整 Git 克隆开始，先完成发布改动并创建提交，再为该提交创建标签。在目标操作系统运行：

```powershell
git tag rel/beta/1.2.3.1
pwsh -NoProfile -File ./release.ps1 -Tag rel/beta/1.2.3.1
```

脚本构建 Release，macOS 生成宿主架构的 DMG，Windows 生成 x64 NSIS 安装包。输出使用全新的 `artifacts/releases/<RID>/<timestamp>-<ID>/` 目录，成功后打印完整路径。Windows ARM64 宿主仍以 x64 为目标。

```powershell
pwsh -NoProfile -File ./release.ps1 -Tag rel/beta/1.2.3.1 -Version 1.2.3.1 -SkipBuild
```

普通发布的 `-Tag` 必须指向 HEAD；省略时自动读取 HEAD 上的合法发布标签。存在不同发布身份时必须显式选择，缺少合法标签、Git 不可用或浅克隆均不能普通发布。可选 `-Version` 必须为四段数字，并与所选标签的版本一致；它只校验版本，不覆盖标签。

`-SkipBuild` 复用匹配原生产物，仍发布桌面应用、导出 worker 和 CLI。必要时指定 `-FfmpegRoot`、`-SdlRoot` 或 `-NsisPath`；输出目录必须尚不存在，发布不会安装依赖。脚本只在本地构建和打包，提交、创建/推送标签及上传 GitHub Release 由发布者完成。

## 未创建标签的测试包

验证打包流程时，可以显式提供尚未创建的合法标签名称并使用 `--force`，也支持 PowerShell 写法 `-Force`：

```powershell
pwsh -NoProfile -File ./release.ps1 -Tag rel/beta/1.2.3.99 --force
```

此模式要求明确指定 `-Tag`，按当前 HEAD 打包，不创建或移动 Git 标签。如果该标签已经存在且指向其他提交，仍会拒绝。标签格式、各数字段 `0–65534` 的范围、可选 `-Version` 的四段匹配校验，以及完整 Git 历史要求均继续生效。

测试包继续使用清单 schema 2，并写入 `IsTestPackage: true`；普通发布写入 `false`。清单中的 `GitSha` 和系统构建号仍来自真实 HEAD，输出仍使用 `artifacts/releases/<RID>/<timestamp>-<ID>/` 下的全新目录。

## 版本与递增

Git 标签保存发布身份；应用和 CLI 显示产品版本，平台元数据使用独立的数字构建号：

| Git 标签 | 应用／CLI 显示 | 渠道内发布序号 |
|---|---|---|
| `rel/beta/1.2.3.1` | `1.2.3-beta.1` | beta 的第 1 号发布 |
| `rel/stable/1.2.3.1` | `1.2.3` | stable 的第 1 号发布 |

第四段不使用 `1000` 分界。前三段和发布序号均为 `0–65534`，历史序号 `0` 继续有效。同一基础版本、同一渠道的下一次发布递增第四段；可以用以下命令查看已有序号：

```powershell
git tag --list 'rel/beta/1.2.3.*' --sort=-v:refname
```

例如发布下一个 beta 时，在同一主线上创建新提交，再创建 `rel/beta/1.2.3.2`。正式版使用 `rel/stable/1.2.3.1`，后续重新发布递增为 `.2`。每次新发布都使用新提交；同一提交的跨平台构建或失败重试继续使用原标签。

系统构建号由 `git rev-list --count HEAD` 计算，包含 HEAD 及其全部祖先，最大为 `65534`。完整提交历史就是构建号记录，不需要维护计数文件；构建不会创建提交或回写编号。沿同一主线新增提交后构建号递增，同一提交在所有平台保持一致。

| 元数据 | 值 |
|---|---|
| `InformationalVersion`、产品显示版本 | `1.2.3-beta.1` 或 `1.2.3` |
| `AssemblyVersion` | `1.2.3.0` |
| Windows `FileVersion`、安装器数字版本 | `1.2.3.<系统构建号>` |
| macOS `CFBundleShortVersionString` | `1.2.3` |
| macOS `CFBundleVersion` | 系统构建号 |

正式版序号虽不显示，仍写入程序集元数据和包清单。桌面、worker 和 CLI 的还原、构建与发布共用同一份版本快照。

更新比较先比较前三段，再比较渠道（stable 高于同基础版本的全部 beta），最后比较同渠道序号。GitHub Release 的 `prerelease` 标记继续决定候选渠道，上传 beta 时设置预发布标记，正式版取消该标记。

现有 beta 标签的序号为 `0`，迁移后的首次同版本 beta 使用 `.1`，旧数字更新器才能发现该更新。旧客户端仍无法识别 `beta.1 → stable.1` 的渠道晋升；安装带有新更新器的版本后，后续比较按上述渠道规则执行。

## 生成便携包

```powershell
# macOS:
pwsh -NoProfile -File ./publish.ps1 -Tag rel/beta/1.2.3.1 -RuntimeIdentifier osx-arm64 -Configuration Release -OutputDirectory ./artifacts/releases/mac-local
# Windows:
pwsh -NoProfile -File ./publish.ps1 -Tag rel/beta/1.2.3.1 -RuntimeIdentifier win-x64 -Configuration Release -FfmpegRoot C:/SDK/ffmpeg-shared -SdlRoot C:/SDK/SDL3 -OutputDirectory ./artifacts/releases/windows-local
```

macOS 添加 `-CreateDmg`，Windows 添加 `-CreateInstaller`。NSIS 要求 3.11+，`-NsisPath` 必须与安装包生成配合；打包须在对应系统执行。

Windows 生成安装包前，通过 Scoop 安装 NSIS：

```powershell
scoop bucket add extras
scoop install nsis
```

## 应用包内容

完整内容包括自包含 .NET、桌面应用、`AegiNext.Export`、`aegn-exporter`、`aegnc` 及完整 Spectre 依赖、FFmpeg/FFprobe、原生模块、递归运行时依赖、原始许可证与专利声明，以及从发布输出整体复制的 `i18n/` 语言资源目录。导出库、worker 和 native export 必须来自同一配置，包含匹配的 ABI 契约指纹；只替换其中一个文件可能导致合同校验失败。

Spectre 的固定版本上游许可证和包声明保存在 `scripts/publish/licenses/`；升级 `Directory.Packages.props` 中的包版本时一同更新。

- `media-runtime.json` 指定包内工具。
- `package-manifest.json` 记录产品版本、原始标签、渠道、发布序号、系统构建号、`IsTestPackage` 标记、RID、Git 身份/脏状态、运行时/工具版本、依赖/许可来源、实际系统要求和文件哈希。
- macOS 重写 Mach-O 依赖路径并签名，最高原生库要求决定最低系统版本；默认临时签名，公证另行处理。
- Windows 从选定 SDK/编译器位置收集 x64 PE 依赖；额外来源使用 `-RuntimeDependencyDirectory` 和 `-LicenseDirectory`。

发布所选 FFmpeg SDK 必须提供 H.264、HEVC、VP8、VP9、SVT-AV1、AAC/Opus 及 MP4/Matroska/WebM；VP9/AV1 还需同时接受 8/10-bit。macOS 使用项目源码 SDK 的 libvpx 1.17.0、SVT-AV1 4.2.0、Opus 1.6.1，递归收集这些库与其运行时依赖；Windows 使用锁定的 Gyan 9.0.2 共享包。源码、哈希、配方与许可来源由依赖清单锁定，详细准备方式见[构建](building.md)。系统编译工具可来自 Homebrew/Scoop，发布的编码能力仍须来自选定 SDK。

## 安装器与图标

Windows 向导支持当前用户和所有用户，当前用户默认安装到 `%LOCALAPPDATA%\Programs\AegiNext`；桌面快捷方式可选。更新/卸载前关闭应用、worker 和 CLI。重装清理过期的自有文件，卸载保留额外文件和个人设置。

静默安装接受 `/S /CurrentUser` 或 `/S /AllUsers`，需要快捷方式时加 `/DesktopShortcut`，不带引号的 `/D=...` 放最后。自动同步卸载使用对应范围，且 `_?=...` 放最后。

macOS 重新生成平台图标：

```powershell
pwsh -NoProfile -File ./scripts/assets/export-app-icons.ps1 -SourcePng ./src/AegiNext.Desktop/Assets/AppIcon.Source.png
```

## 校验与搬移

```powershell
pwsh -NoProfile -File ./scripts/publish/verify-package.ps1 -PackageDirectory ./artifacts/releases/mac-local
```

校验覆盖完整文件哈希及语言包结构/编码。新包继续使用清单 schema 2，要求桌面、worker 与 CLI 三个入口及自包含 .NET 10 运行时，并校验发布身份与产品显示版本的一致性；历史 schema 1/2 的三段、四段数字版本包保留离线验包兼容。发布在本机直接运行包内 CLI 的帮助、版本和一次 SRT→ASS 转换。搬移整个应用包；macOS 拷贝/归档须保留签名所需的扩展属性，使用 Finder 或 `ditto`，再校验搬移后的签名。

分别在目标系统检查启动、媒体打开、可听播放、字幕编辑、实际导出/取消，以及无开发工具 PATH 的搬移运行。导出检查应使用包内 worker 与工具，至少覆盖以下不同能力：

| 检查 | 应确认的结果 |
|---|---|
| VP8、VP9、AV1 软件导出 | 实际编码器、完整帧数、可解码成片；VP9/AV1 分别检查 8/10-bit |
| CQ/CRF、VBR、CBR | 所选模式与参数回执一致；CQ 同时确认目标码率，不能以短片平均码率代替码控证据 |
| MKV、WebM、MP4 与音频 | 支持的格式组合、原音轨复制、AAC/Opus、无音频；WebM 不兼容音轨在视频编码前被拒绝 |
| PQ、HLG 与静态 HDR10 | 10-bit、色彩标签与 mastering/content-light 在最终容器中保留，字幕亮度符合参考条件 |
| 搬移后的独立运行 | 清除开发工具 PATH 后仍能启动工具并完成编码/解码，原始许可证与专利声明齐全 |
| Windows 硬件导出 | 在支持的实体显卡上检查 VP9 QSV 与 AV1 NVENC/QSV/AMF，区分初始化确认和运行时查询证据 |

SDK 软件短片与运行时闭包搬移通过后，仍需检查完整应用的导出、取消与错误反馈。Windows 虚拟机的软件输出不能证明实体 GPU 可用；macOS 没有新增 VP8/VP9/AV1 硬件编码后端。安装范围、更新、原生菜单、DPI、HDR 显示与导出面板外观需要对应实机/视觉检查，QSV 回执当前属于 `ConfiguredAndInitialized`，不标记为 `RuntimeQueried`。
