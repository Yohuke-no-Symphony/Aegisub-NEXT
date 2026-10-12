<p align="center">
  <img src="src/AegiNext.Desktop/Assets/AppIcon.png" width="112" alt="AegiNext icon" />
</p>

<h1 align="center">AegiNext</h1>

<p align="center">现代化、原生跨平台的字幕制作软件</p>

<p align="center">
  <a href="README.md">English</a> · <a href="README.zh-CN.md">简体中文</a>
</p>

<p align="center">
  <a href="docs/zh-cn/quick-start.md">快速开始</a> ·
  <a href="docs/zh-cn/README.md">中文文档</a> ·
  <a href="docs/en/README.md">English Docs</a>
</p>

AegiNext 致力于提供更易上手的字幕制作体验，兼容 Aegisub 文件标准，原生支持 macOS 与 Windows。开发目标是在 Aegisub 原作者停止维护后，接替旧版 Aegisub，延续字幕制作工作流。

![AegiNext 字幕工作台](docs/assets/screenshot.jpg)

## 功能

- **字幕制作**：多轨编辑、波形与语谱图打轴、ASS/SRT 导入导出。
- **样式与特效**：富文本、卡拉 OK、关键帧、运动路径、蒙版和可复用脚本。
- **现代工作台**：可停靠面板、明暗主题、英文／简体中文／日文界面与自定义快捷键。
- **保存与导出**：自动保存、备份恢复、独立进程视频压制与 HDR 导出。

## 快速开始

1. 打开应用并创建工程，通过 **文件 → 打开视频**载入媒体。
2. 选择轨道，按 **F8 / F9** 标记字幕起止时间，再输入文字。
3. 调整样式，按 **Cmd/Ctrl + S** 保存，通过**格式**导出字幕或在**压制**面板导出视频。

需要广泛兼容视频时，在**设置 → 媒体**中选择**自动**解码。macOS 按实际编码和 profile 协商 VideoToolbox，支持设备可加速 HEVC 4:2:2/4:4:4、AV1、VP9、ProRes 422/4444，并保留源精度、色度与 Alpha；硬件不支持的输入回退 CPU。ProRes Alpha 合成到黑底；**GPU（严格）**要求确认真实硬件加速，不自动回退。详见[媒体兼容性](docs/zh-cn/media.md)。

## Aegisub 兼容操作

支持旧版 Aegisub 的以下试听快捷键和鼠标打轴操作；实际按键以**设置 → 快捷键**为准。

| 默认按键 / 鼠标 | 操作 |
|---|---|
| Q | 试听字幕开始前的短片段 |
| W | 试听字幕结束后的短片段 |
| E | 试听字幕开头的短片段 |
| R | 试听整句字幕 |
| 时间线主体左键 | 在点击位置设置主选字幕的开始时间 |
| 时间线主体右键 | 在点击位置设置主选字幕的结束时间 |

Q/W/E/R 在时间线或字幕行获得焦点、且未编辑文本时生效。短片段默认 **500 ms**，可在**设置 → 预览**调整。

鼠标打轴需先选择字幕，并开启时间线左下角鼠标图标对应的**经典 Aegisub 打轴**（默认关闭）。开启后，主体左、右键修改当前主选字幕，不切换选择；关闭后恢复普通选择、拖动和右键菜单。详见[快捷键说明](docs/zh-cn/quick-start.md#旧版-aegisub-兼容操作)。

## 字幕与工程格式

| 格式 | 保留内容 | 操作入口 |
|---|---|---|
| ASS（`.ass`） | 基础及局部样式、左／右／垂直边距、卡拉 OK、静态位置和受支持的蒙版 | 格式 → Aegisub → 导入 / 导出 |
| SRT（`.srt`） | 字幕文字与起止时间 | 格式 → SRT → 导入 / 导出 |
| AegiNext 工程（`.aeginext`） | 工程数据、轨道、样式、特效和媒体引用 | 文件 → 打开工程 / 保存 / 另存为 |

字幕导入创建独立轨道，重叠字幕保留原时间并分配到额外轨道；导出包含所有字幕轨道。继续编辑请保存 `.aeginext` 工程。

受支持的 ASS `\move`、`\fad` 和 `\fade` 动画会转换为可编辑的原生轨道；不支持的标签或动画会在导入时提示转换确认。SRT 导出会提示富样式、卡拉 OK、蒙版和动画的损失。完整支持范围见[字幕导入与导出](docs/zh-cn/subtitle-editing.md#导入与导出)。

## 从源码运行

先安装仓库要求的 .NET SDK 和 PowerShell，以下命令均在仓库根目录执行。

### 安装依赖

原生 SDK 按锁定版本与 SHA256 校验安装到项目 `.dependencies/`，不提交 Git。构建工具在 macOS 使用 Homebrew、Windows 使用 Scoop。Windows 生成发布安装包还需要安装 NSIS：

```powershell
scoop bucket add extras
scoop install nsis
```

准备锁定的项目 SDK、安装缺失的构建工具并构建工作台：

```powershell
pwsh -NoProfile -File ./build.ps1 -Target Workbench -InstallDependencies
```

### 构建

后续构建执行：

```powershell
pwsh -NoProfile -File ./build.ps1 -Target Workbench -Configuration Release
```

### Release 打包

```powershell
pwsh -NoProfile -File ./release.ps1
```

在目标系统运行，macOS 生成 DMG，Windows 生成 NSIS 安装包；产物位于 `artifacts/releases/` 下的新目录。

环境配置、启动与调试见[构建指南](docs/zh-cn/building.md)，打包见[发布指南](docs/zh-cn/publishing.md)。

## 许可证

GPLv3。
