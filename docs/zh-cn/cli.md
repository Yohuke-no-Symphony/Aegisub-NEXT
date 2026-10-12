# 命令行工具 aegnc

[English](../en/cli.md) · [简体中文](cli.md) · [全部指南](README.md)

`aegnc` 随桌面应用分发，支持独立转换字幕和从已保存工程烧录视频。Windows 可执行文件与桌面程序相邻；macOS 位于 `AegiNext.app/Contents/MacOS/aegnc`。安装不会自动修改 PATH，以下示例中的 `aegnc` 可以替换成实际路径。

```sh
./AegiNext.app/Contents/MacOS/aegnc --help
```

Windows 解压发行目录后可在 PowerShell 执行 `& .\AegiNext\aegnc.exe --help`。源码构建后可使用 `dotnet run --project src/AegiNext.Cli -c Release -- --help`；视频导出还需要匹配的原生模块和相邻的 `aegn-exporter`，参见[构建](building.md)。

## 转换字幕

```sh
aegnc convert -i input.ass --target srt -o subtitles/ --accept-loss
aegnc convert -i project.aeginext --target ass -o subtitles.ass --accept-loss
```

输入支持 ASS v4+、SRT 和 `.aeginext`，目标为 `--target ass|srt`；首版不接受 TXT 或传统 SSA。输入参数为 `--input/-i`，输出参数为 `--output/-o`。相对路径按当前工作目录解释。

省略输出时写入输入目录，使用源文件名与目标扩展名；同格式转换使用 `.converted` 后缀。目录输出自动添加数字后缀避让已有文件。显式输出文件必须使用对应扩展名，不能覆盖已有文件或指向输入文件。

已有目录、末尾带路径分隔符以及没有扩展名的路径按目录处理；其他路径按文件处理。例如 `-o result` 创建目录，`-o result.srt` 指定文件。

转换会显示导入与导出的全部格式诊断。交互终端需一次确认；脚本必须使用 `--accept-loss`，否则退出且不写文件。该开关接受可继续转换的损失，不会忽略解析错误或工程预算限制。ASS 转 ASS 会重新生成格式内容，不保证原文逐字一致。

文本输入严格按 UTF-8 读取，允许 UTF-8 BOM；输出为 UTF-8 无 BOM。ASS 保留已声明的 PlayRes，缺失轴使用 1920/1080 并报告诊断；SRT 使用 1920×1080 的临时画布。外部字幕临时工程使用 30 fps，影响需要采样的动画；工程输入使用自身画布、帧率与参考缩放。未确认媒体播放原点的工程会产生诊断，接受后沿用零偏移。

## 导出视频

```sh
aegnc export -i project.aeginext --encoding h264 --use-gpu --vbr --video-bitrate 8 -o output/
aegnc export -i project.aeginext --encoding vp9 --crf --quality 32 --audio opus --audio-bitrate 192 --container webm
```

`export` 接受 `.aeginext` 工程并烧录工程外观；要求有效媒体绑定及受支持的偶数画布。它与桌面压制共用编码、HDR、封装和时间戳验证，参见[视频导出](export.md)。首版 CLI 不提供内挂字幕方式。

| 参数 | 可选值与默认值 |
|---|---|
| `--encoding` | auto/h264/hevc/vp8/vp9/av1；默认 auto，SDR 选择 H264，HDR 选择 HEVC |
| `--use-gpu` | 默认软件编码；显式请求硬件，不可用时失败 |
| `--crf`、`--cq`、`--vbr`、`--cbr` | 互斥；软件默认 CRF，VP8 默认 CQ，GPU 默认 VBR |
| `--quality` | H264/HEVC 0–51，默认 20；VP8/VP9 0–63，AV1 1–63，默认 32 |
| `--video-bitrate` | 0.1–200 Mbps，默认 8；小数点、最多三位小数 |
| `--speed` | ultrafast/superfast/veryfast/faster/fast/medium/slow/slower/veryslow；默认 medium |
| `--audio` | copy/aac/opus/none；默认 copy |
| `--audio-bitrate` | 32–512 kbps 的整数，默认 192；仅 AAC/Opus 使用 |
| `--decode` | auto/software/hardware；默认 auto，与编码 GPU 开关独立 |
| `--container` | mp4/mkv/webm；须与编码、音频及显式输出扩展名兼容 |

CRF 不接受视频码率，VBR/CBR 不接受质量值，CQ 同时接受质量和视频码率。GPU 不接受 CRF/CQ。WebM 不接受 AAC；复制音频的实际容器兼容性由导出 worker 检查，不自动重编码。

省略输出时使用 `工程目录/output/工程文件名-yyyyMMdd-HHmmss.扩展名`。Auto/H264/HEVC 默认 MP4，VP8/VP9/AV1 默认 MKV；显式文件扩展名可以选择兼容容器，若同时提供 `--container` 则必须一致。文件输出不覆盖，目录的自动名称添加数字后缀避让冲突。

## 向导与脚本

无参数执行 `aegnc`，先选择转换或视频导出，再逐步选择有效参数。无参数执行 `convert` 或 `export` 会进入对应完整向导。提供部分参数时，只补问缺失的必需项：转换需要输入和目标，导出只需要输入；其余使用默认值。例如 `export -i project.aeginext --vbr` 使用 8 Mbps，不再询问码率。

显式参数无效或冲突时直接报错。`--non-interactive`、输入重定向或输出重定向均禁止询问；脚本应完整提供必需项，转换还需接受产生的诊断。使用 `--help/-h` 查看帮助，`--version` 查看产品版本。

执行前显示最终参数；导出报告帧数、时间和实际编码器。Ctrl+C 取消并清理临时文件，成功后才原子提交输出。退出码：成功 `0`，运行失败 `1`，参数或配置错误 `2`，取消 `130`。
