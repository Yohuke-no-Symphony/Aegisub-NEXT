# 媒体集成

[English](../en/media.md) · [简体中文](media.md) · [全部指南](README.md)

## 配置与入口

构建匹配 RID/配置的 `Workbench` 原生库，详见[构建](building.md)。开发运行使用绝对 `AEGINEXT_FFPROBE_PATH`/`AEGINEXT_FFMPEG_PATH` 或 PATH，显式路径无效即失败；应用包使用包内工具。

| 位置 | 职责 |
|---|---|
| `src/AegiNext.Media/Probing` | FFprobe 身份、有界进程与不可变媒体事实 |
| `src/AegiNext.Media/Decoding` | 解码/帧契约、原生句柄和精确帧导航 |
| `src/AegiNext.Media/Playback` | 原始帧调度与呈现所有权 |
| `src/AegiNext.Media/Preview` | 独立 SDR 转换 |
| `src/AegiNext.Media/Audio`、`Analysis` | 输出时钟与视口音频分析 |
| `src/AegiNext.Desktop/Controllers`、`Workspace` | UI 交付、播放、校准和分析协调 |
| `native/shared` | 预览/导出共享解封装、解码、定位和颜色解释 |

## 探测与解码

使用 `FfprobeMediaProbe` 读取已有本地文件：验证工具身份、不经 shell 调用、并发读取双管道，并限制超时/输出。保留全部流索引、原始时间戳/时间基、颜色/HDR 事实及未知值；报告帧率不能证明 CFR。

通过 `FfmpegVideoDecoder` 打开绝对流索引，优先接收缓存帧并排空 EOF。返回帧拥有独立引用，后续读取或解码器释放不回收它；每帧显式释放。平面复制按逻辑行顺序保留有效像素，不裁剪或转换颜色。

先定位关键帧，再选择显示区间 `Time <= target < NextFrameTime`。显示时间优先原始 PTS，再用 FFmpeg best-effort 时间戳；两者都缺失时，可以用上一帧有效时长推导下一帧时间，必要时仅在声明的平均与名义帧率一致时推导时长。首次顺序读取可用流起点锚定首帧。推导时间显式记录依据，不覆盖原始 PTS 或 best-effort 事实。缺少依据或显示时间倒退时拒绝，重复时间选择最后解码帧。VFR 保持有理时间；缓存限制帧数/字节预算，命中后仍须将顺序播放连接到逻辑下一帧。

自动解码优先 macOS VideoToolbox 或 Windows D3D11VA，硬件不支持的格式在首次交付前回退 CPU。“GPU（严格）”要求确认真实加速，不回退 CPU；需要广泛兼容时选择“自动”或“CPU”。取消、损坏输入和后续错误不静默回退。macOS 选择具备实际 VideoToolbox 配置的解码器，并协商保留源格式的输出；支持设备可加速 HEVC 4:2:2/4:4:4、AV1、VP9、ProRes 422/4444。硬件读回保留色度、分量精度、Alpha、尺寸与帧元数据，12-bit ProRes 可使用 16-bit 输出容器。Windows D3D11VA 读回支持不透明 4:2:0 NV12／P010；ProRes 4444／4444 XQ（有／无 Alpha）及 D3D11VA 读回无法保真的扩展 HEVC 采样／精度使用 Vulkan。Vulkan 设备匹配 Windows 硬件适配器，排除软件适配器；取得真实 Vulkan 帧并完成保真读回后才报告加速。ProRes 使用 GPU 计算着色器，HEVC 使用设备支持的 Vulkan Video profile。锁定 SDK 的 Vulkan ProRes 422 样本差异超出兼容性验收范围，因此这些 profile 在自动模式使用 CPU，严格 GPU 模式拒绝。加速取决于设备、驱动、系统、编码 profile 与尺寸，而非 MKV／MOV 容器；VideoToolbox 软件会话不会标为 GPU 解码。

兼容性测试覆盖 H.264 10-bit/RGB、HEVC 10-bit 4:2:2/4:4:4、VP8、VP9/AV1 10-bit、MPEG-2/4、MJPEG、ProRes 422 Proxy/LT/422/HQ 与 4444/4444 XQ（有／无 Alpha）、FFV1 16-bit，以及 MKV、MP4、MOV、WebM、AVI、MPEG-TS 容器。AV1 在硬件支持时使用原生解码器，软件模式使用 dav1d，film grain 由解码器合成。具体素材仍需具备有效流信息和受支持的色彩解释。

缺失 SDR 标签使用共享的显式解析规则，原始事实不变，解析结果标明推断字段。存在 HDR 证据时要求完整受支持标签。预览解码偏好与导出解码/编码选项独立。

## 统一时间源

默认音频使用 macOS CoreAudio、Windows WASAPI，输出 48 kHz 双声道 float PCM，预填约 200 ms、队列最多 250 ms。设备呈现位置映射已消费样本，SYSTEM 时间不减去猜测的队列/设备缓冲延迟。

额外延迟校准按设备、后端、采样率和声道匹配。设备时钟丢失时冻结打轴并更换输出，F8/F9 不能使用不可用时钟；音频空隙/短轨道补静音。无音频视频使用单调时钟，显式 SDL 输出标为 ESTIMATED。

播放结果包含 generation，新命令使旧交付失效，消费方在呈现前检查身份。暂停/替换取消过期工作而不终止共享解码器；关闭请求原生取消、等待工作结束并释放未交付资源，已交付帧仍由消费方负责。

## 预览合成与音频分析

SDR 转换借用原始帧，输出独立、不透明、从上到下的 sRGB BGRA8。ProRes 4444 Alpha 保留在原始解码平面中，预览与导出先在线性光下将透明区域合成到黑底，再各自处理颜色；未标注 Alpha 语义时按 straight 处理，明确的预乘标记按预乘解释。导出使用高精度源像素，不使用 SDR 预览。合成保留对应的未叠字背景，避免重复字幕或错帧背景。

控制器结果检查请求、媒体时间、工程修订和画质。工作台选择预览画质，交互时临时限制。呈现帧与派发延迟应和解码耗时分开测量，尤其是长 GOP、高分辨率和反复定位。

绑定媒体后，`AudioAnalysisSession` 在工程 `caches/audio/<identity>/` 构建完整波形与频谱金字塔。`data.bin` 保存无压缩二进制块，`index.bin` 保存版本、精确有理时间、各层索引与 SHA-256 校验。指纹包含算法版本、流索引、文件大小和修改时间及首／中／尾各最多 64 KiB 的内容，媒体移动不改变身份。完整缓存跨启动复用；取消、失败或崩溃留下的临时构建不作为完整缓存使用。

默认分析使用独立固定网格：48 kHz 单声道 min/max 波形，最细持久层为 512 样本；63-tap FIR 低通至 16 kHz，1024 点 Hann FFT、256 样本 hop、128 对数频率行、−80 至 0 dB 强度范围。普通波形和全部频谱缩放通过二分索引读取既有层。粗谱保留绝对媒体网格上的中心列子集。仅细于所选持久波形层的请求使用独立、按需开启的局部解码器及有界 PCM LRU，不生成视口 FFT，不占用播放音频。

`AudioAnalysisCacheBuilder` 单路顺序解码，使用有界分段流水线重叠解码、并行 FIR／FFT 与有序写入；默认每段 196608 个样本（4.096 秒）。所有工程的解码、DSP 和局部细波形共享应用级线程预算，自动模式最多 4 个工作位，手动范围为 1 至 CPU 数量减 1（单核保留 1）。细波形读取优先，但连续优先次数受限，后台工程仍能推进。每段只滤波一次，复用 PCM、FFT 和频谱输出缓冲；FFT 窗函数和旋转因子预先计算，每个频率行只做一次对数转换。默认 64 MiB 分配为 48 MiB 工作预算与 16 MiB 读缓存，内存还会限制并发数；不持久保存 PCM。完成的批次按顺序发布，未完成区间保持待分析。隐藏图层仅取消视口读取；更换媒体或关闭则取消并排空构建。批次排空全部工作位后才调用检查点，可协作让出任务执行位，使单执行位下保存、切换媒体等工作仍能运行。

设置中的“音频分析”页提供自动／手动线程数、内存预算、波形增益和语谱亮度／对比度。显示参数立即重绘，线程与内存参数在安全批次边界调整，无需重新生成缓存。勾选高级模式后可修改分段长度、频谱采样率、FFT 大小、hop 比例、频率行数／范围、窗函数、dB 范围和最细持久波形层；页面显示窗口／步长时长与缓存体积估算。影响缓存内容的参数只在点击“应用并重新生成”后生效，并为全部打开的工程排队重建。重建前取消并排空旧构建，保留已显示图层直到新缓存可用。重置立即恢复普通参数，高级参数恢复为默认草稿，再点击应用生效。

缓存格式 v2 的身份与索引包含完整分析配方摘要；执行和显示参数不参与身份，不同配方的缓存可以并存。旧版本缓存会重新生成一次；调整 dB 分析范围会从音频重新计算，可恢复旧范围中已经裁剪的弱能量。

另存为成功后由 `AudioCacheMigrationTask` 后台复制完整缓存；构建中只记录最新目标，完成后迁移。保存不等待生成或复制。迁移持有数据句柄与对应索引的快照及目标写入租约，源缓存同时重建也不会混用两个版本；打开缓存时通过同一租约保证读取完整的数据／索引组合。取消或复制失败保留原读取路径，下次保存可重试。

## 验证

```powershell
dotnet test Tests/AegiNext.Media.Tests/AegiNext.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~Probing'
dotnet test Tests/AegiNext.Desktop.Tests/AegiNext.Desktop.Tests.csproj -c Release --filter 'FullyQualifiedName~VideoPreview'
dotnet test Tests/AegiNext.Media.Tests/AegiNext.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~Analysis'
```

原生用例所需开关和工具路径见[构建](building.md)。验证实际 PTS/像素、定位、替代、取消、帧生命周期和故障。静音设备测试不证明扬声器延迟，Headless 呈现不证明原生 HDR 或 GPU 验收。

验证原始 HEVC 4:4:4 10-bit 报错时，将 `AEGINEXT_COMPATIBILITY_MEDIA_PATH` 设为源文件绝对路径，启用原生测试并运行 `OriginalVideoCompatibilityTests`，测试会走实际桌面预览控制器。可选 `AEGINEXT_COMPATIBILITY_REPORT_PATH` 保存帧哈希、显示区间、回退诊断与资源计数。

## 纯音频工程

**文件 → 打开音频**（默认无快捷键，可在 **设置 → 快捷键** 中指定）可打开只有音频流、没有视频流的文件，例如 WAV、FLAC、MP3、M4A/AAC、OGG/Opus 或 MKA。把音频文件拖到预览面板也可以打开。

- **替换语义：** 每个工程只有一个媒体绑定。打开音频会替换整个绑定（当前视频及其音频都会被解除关联），而不是只替换现有视频下的音轨。如果所选文件包含真正的视频流，会按普通视频媒体打开；内嵌封面（`attached_pic`）会被忽略。
- **预览：** 预览显示工程分辨率的黑色画布，并在其上渲染字幕。画布以固定 25 fps 网格随音频时钟推进，因此播放、跳转、F8/F9、QWER/鼠标打轴、波形、频谱和 Koumori 分析都与视频媒体一样使用音频流。由于没有视频关键帧，打轴后处理中的关键帧吸附会被跳过。
- **工程文件：** `.aeginext` 以 `videoStreamIndex: null` 保存音频引用（工程格式版本 20）。保存、重新打开以及重新链接移动过的文件与视频相同。旧版本会拒绝版本 20 的工程，而不会误读。
- **导出：** 视频导出与字幕封装需要视频流，此时会被禁用并显示说明；命令行也会拒绝此类工程。字幕格式（ASS、SRT 等）仍可正常导出。
