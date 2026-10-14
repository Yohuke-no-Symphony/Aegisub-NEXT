# 架构

[English](../en/architecture.md) · [简体中文](architecture.md) · [全部指南](README.md)

## 查找职责归属

| 位置 | 职责 |
|---|---|
| `src/AegiNext.Core` | 有理时间、不可变工程/场景模型、验证、动画、蒙版和特效 DSL |
| `src/AegiNext.Application` | 编辑事务、Undo/Redo、存储/资源，以及提交字幕导入和投影编辑 |
| `src/AegiNext.SubtitleFormats` | ASS/SRT/TXT 转换、格式诊断、ASS 文本投影和字幕时间轴映射 |
| `src/AegiNext.Rendering` | 独立于 UI 的 Skia/HarfBuzz 排版、场景几何和线性 F16 合成 |
| `src/AegiNext.Media` | 探测、帧所有权、原生适配、播放/分析与预览 |
| `src/AegiNext.Export` | 导出能力与参数、预设、worker 协议、原生烧录会话和独立字幕封装 |
| `src/AegiNext.Desktop` | Avalonia 工作区、面板、控件、菜单、偏好和平台宿主 |
| `src/AegiNext.ExportWorker` | 使用 Application 读取快照、调用 Export 的独立 `aegn-exporter` 宿主 |
| `src/AegiNext.Cli` | Spectre.Console 的 aegnc 主机，组合 Application、SubtitleFormats、Rendering 与 Export；不引用 Desktop |
| `native/decoder`、`native/audio`、`native/export` | 独立 FFmpeg/SDL3 C ABI |
| `native/` | 可选 macOS HDR 诊断后端 |
| `Tests/`、`scripts/` | 定向验证、构建和打包工具 |

Core 不依赖 Avalonia、Dock、FFmpeg、Skia 或文件系统。Application 负责业务修改，Desktop 组合服务；Rendering 和 Media 使用显式契约与资源所有权。Export 依赖 Core、Media、SubtitleFormats 与 Rendering，Desktop 和 worker 调用 Export；Media 不反向依赖 Export，也不拥有导出动态库或预设。业务 ViewModel 不持有控件、Dock 对象或 Bitmap。

SubtitleFormats 仅依赖 Core，返回转换数据与诊断。Application 通过编辑事务提交导入结果和高级 ASS 文本编辑。Desktop 负责文件导入导出流程及转换确认，并通过格式库的接口提供字重和排版适配器。

## 视频导出边界

`AegiNext.Export` 是独立托管模块，集中定义格式、品质范围、容器与硬件候选，供设置校验、桌面参数草稿和完成回执共同使用。CPU 使用 libx264、libx265、libvpx、libvpx-vp9 或 libsvtav1；VP8 的 CQ 同时保存质量与码率，其他格式的 CRF 只使用质量，VBR/CBR 使用明确的目标码率。用户范围、默认值及平台矩阵见[视频导出](export.md)。

Desktop 捕获不可变工程和参数，再启动独立 worker；Application 继续拥有任务、编辑事务和持久化。烧录路径在视频编码前检查源音轨与容器兼容性，调用原生导出进行线性合成和编码，并在同一原生会话中封装最终 MP4、MKV 或 WebM。中间 NUT 保留源呈现时间戳；静态 HDR 字段通过显式结构传递到最终封装，不能从临时容器的缺失字段推断它们不存在。AAC/Opus 音轨准备由外部工具完成，复制音轨由原生封装保留音频包。

仅封装由独立的 `SubtitleMuxRequest` 与 `SubtitleMuxExporter` 服务承接，不读取烧录编码参数，也不进入视频解码、渲染或原生编码会话。Desktop 沿用 SRT 损失分析与转换确认；worker 通过 SubtitleFormats 合并工程全部字幕，以媒体绑定的 `MediaOrigin` 映射到原始媒体呈现时间，再调用锁定 FFmpeg 复制源媒体流、保留章节并追加新轨。MKV 保留 SRT，MP4 仅将新轨转换为 `mov_text`；原有轨道保持编码与播放偏好，新轨从不强制显示。MP4 仅在所有映射视频流的色彩范围均已声明为 `tv` 或 `pc` 时启用 `+write_colr`；任何范围未知时均不推断或补齐色彩信息，最终仍严格读回核验完整色彩字段。

新增轨始终请求 `disposition=0`；最终核验仅在输出为 `mov_text` 且源中完全没有字幕轨时，允许唯一新轨读回 `default=1`，`forced` 及其他标志仍必须为零。MKV 与已有原字幕的 MP4 要求新轨全部标志为零，原轨播放偏好始终严格保留。锁定 FFmpeg 会启用 MP4 的唯一字幕轨以保证 QuickTime 字幕菜单可用，启用并不表示默认渲染；实际初始显示由播放器决定，详见 [FFmpeg n9.0.2 `enable_tracks`](https://raw.githubusercontent.com/FFmpeg/FFmpeg/n9.0.2/libavformat/movenc.c)。

MP4/MOV 的章节可能由专用 `bin_data` 轨承载。仅当源中存在章节、容器属于 MOV 系列，且该轨为 `data/bin_data`、handler 为 `SubtitleHandler`、codec tag 为 `text`、帧数等于章节数，并且探测不到数据包时，才将其识别为章节载体。章节通过映射在目标容器重建，源载体不直接复制为普通数据轨；其他源流仍逐一映射。输出章节的起止时间与标签必须匹配，额外输出流也必须通过同样的章节载体识别。

提交前读回全部源流的编码与参数、视频色彩、音轨格式、播放偏好、语言与名称，并核验首包呈现时间和章节。每条音轨还通过有界缓存的流式扫描比较包数量、最大包结束时间、`initial_padding`，以及逐包 `skip_samples` 与 `discard_padding` 的总量；包结束时间允许 1 ms 加源和输出各一个时间基单位的量化容差，其余数值必须一致。新增字幕抽回 SRT，逐条核对文字及起止时间，容差为 1 ms。容器不能保留源流、音频采样裁剪变化或 MP4 截断重叠字幕时拒绝提交；源容器元数据会尝试复制，但不声明任意标签跨容器无损。烧录与仅封装共享 worker 进程管理、取消和原子输出事务，只有通过对应操作的完成回执校验后才提交新文件。

原生结果在编码后和最终封装后读回核验实际编码、位深、色彩及静态 HDR。父进程同时校验请求对应的解码后端、编码器、码控、参数精度与输出证据，确认后才原子提交文件。码控证据区分 `ConfiguredAndInitialized` 与 `RuntimeQueried`；当前 QSV 使用前者。锁定 Windows FFmpeg 的 oneVPL dispatcher 为静态链接，不能把它的 session 交给独立 dispatcher 或依赖私有布局来声称运行时查询成功。

当前 native export **ABI 6** 的请求结构为 **88 字节**、结果结构 **488 字节**、HDR 结构 **96 字节**、封装请求 **32 字节**，字幕前景结构仍为 **24 字节**。worker 协议为 **4**，显式区分烧录与仅封装操作，烧录专属参数不能随仅封装请求传入；应用、worker、原生库及契约指纹必须同配置重建和部署。导出预设版本为 **2**，版本 1 在严格校验后将旧 `crf` 迁移为 `quality`，保留参数与身份；未知、缺失、重复字段及整批冲突仍会拒绝读取或导入。

统一速度名称在不同 CPU 编码器中有明确映射：x264/x265 使用同名预设，libvpx/VP9 使用 `cpu-used`，SVT-AV1 使用数字预设。硬件后端映射到其支持的档位。

| 速度 | libvpx / VP9 `cpu-used` | SVT-AV1 `preset` |
|---|---|---|
| ultrafast | 8 | 9 |
| superfast | 7 | 9 |
| veryfast | 6 | 8 |
| faster | 5 | 8 |
| fast | 4 | 7 |
| medium | 3 | 6 |
| slow | 2 | 4 |
| slower | 1 | 2 |
| veryslow | 0 | 0 |

## 时间与存储

时间采用有理数 `MediaTime`，Clip 可见范围为 `[Start, End)`。解码与 VFR 使用真实呈现时间戳；字幕格式交换只在边界映射已确认的播放原点，保留动画/卡拉 OK 相对时间。

新 `.aeginext` 文件使用 **v18**；`ProjectStore` 同时读取 v3–v17 并在内存中升级。迁移将旧单值边距等值映射为左、右、垂直，保留原有排版位置和字幕／图层时间；未知播放原点保持未知直到确认。兼容缺失/null 的旧蒙版，不支持的非空旧局部蒙版会标明所属对象并拒绝读取；不支持 v1/v2。样式库使用 **v7**，可读取 v1–v6 并采用相同边距迁移。仅支持旧格式的应用无法读取新保存的 v18 工程或 v7 样式库。

版本 14 保存工程素材库、项目内或外部链接的素材存储方式、图片裁剪／播放设置和图形参数。旧图片保留为项目内素材，并成为素材库根文件夹中的条目；迁移补齐全图裁剪和空的显式播放设置。素材库文件夹组织工程元数据，不改变源文件的磁盘目录。

版本 16 为图层和普通文字范围添加原生 X／Y 旋转、透视距离和静态投影补偿。此前版本迁移时，X／Y 旋转和补偿为零、距离为 312.5。版本 17 添加统一 XYZ 的 `ROTATION_3D` 动画目标，以及带分量掩码的有序操作精确曲线；旧标量轴无需重采样即可迁移。静态 X／Y／Z 字段及稳定的旧标识继续兼容读取，旧格式声明拒绝后续版本的新能力。原生 Anchor/Pivot 继续负责位置，ASS 原点标签只在交换边界转换，详见[投影](rendering.md#平面三维投影)。

版本 18 为蒙版添加原生 `Placement` 和固定 `ReferenceFrame`，移除变换中的全局像素 Pivot。旧轴心按参考框归一化，工程画布内的定位用 Anchor 表示，超出工程画布的部分用 Offset 表示；几何、动画值、曲线及稳定身份保留。迁移在旧图层提前反序列化之前完成，新版本严格拒绝旧轴心或混合字段。导出 worker 协议为 v6，应用和 worker 必须匹配；原生导出 ABI 未因这项工程模型变更而改变。

平面 Track → Clip 模型允许同一轨道容纳字幕、图片和形状。同轨片段不能重叠；轨道数组是唯一叠覆顺序，最上方轨道最后绘制。迁移尽量保留原轨道；跨轨片段在旧图层列表中交错不会导致拆轨，只有保持同时活动片段的前后关系或解决同轨重叠确实需要时才派生轨道。旧无效果组仅在时间与合成结果均可保留时展开；含变换、动画、透明度、蒙版、模糊或隔离混合的组会拒绝迁移并标明组名和标识。保存前不修改原文件。

读取校验字段、引用、重叠、几何和预算；保存通过同目录临时文件原子替换。视频只引用、不复制，托管资源使用受限相对路径与可选哈希。另存为重定位资源/引用；跨目录迁移清除可能恢复旧路径的历史。

自动保存通过串行持久化协调器捕获已提交内容和视图状态，不提交草稿、不抢焦点、不改变 Undo。保存旧快照时，较新的修改仍保持未保存。

## 应用级任务服务

`DesktopApplicationContext` 持有唯一 `AegiTaskService`；内核位于 `Application/Tasks`，不引用 Avalonia、Media 或 Rendering。业务模块的具体 `AegiTask` 声明名称、作用域、运行模式、取消、编辑限制和全部资源。运行模式与可取消性、编辑限制互相独立。

统一队列按提交序号启动，队首资源未就绪时后续任务等待。阻断任务等待此前全部任务结束并独占运行，后续任务不能越过屏障。默认并行上限为 4，可在“设置 → 任务”调至 1–32；降低上限不会终止正在运行的任务。工程写入、媒体控制器、个人库和规范化存储路径按资源互斥，全部资源一次取得。内部阶段复用父任务的名额、资源和取消令牌。

提交返回可等待的句柄；取消调用方的等待不取消任务，任务取消必须显式请求。任务进入取消中后，直到子进程退出与清理结束才释放资源及编辑租约。原子提交前检查取消和输入有效性，提交中关闭取消入口。排队不锁住工程，短时编辑租约默认只限制所属工程及其浮动面板。

可取消并行任务可在内部 worker／阶段全部完成且未进入提交或取得编辑租约的检查点调用 `YieldIfWorkIsQueuedAsync`。让出时保留资源、任务身份与开始时间，只释放执行位；仅让队首连续的无冲突并行任务先执行，不跨越阻断任务或资源冲突。取消已让出任务仍须等待原执行栈、取消回调和清理完成，屏障不能越过未完成的执行。已启动的让出任务不能被连续写入合并替换。

工程创建对话框拥有其提交任务的取消权：关闭对话框会显式请求取消，并等待实际任务完成；提交中拒绝取消，已创建的工程继续交给工作台。应用退出在提交和清理完成后阻止新窗口激活。普通调用方的等待取消契约保持独立。

| 业务入口 | 任务策略 |
|---|---|
| 新建、打开、恢复、合并、媒体切换 | 后台准备，替换上下文时限制所属工程，失败回滚 |
| 时间后处理、关键帧索引 | 扫描时允许编辑，提交前校验输入版本及草稿，整批一次 Undo |
| 保存、另存为、资源重定位 | 使用提交时冻结的已提交快照；较新编辑和原始草稿继续保留 |
| 视频／字幕导出、字幕／字体／样式／效果导入准备 | 冻结导出输入；准备结果有效时短时提交 |
| 波形／频谱、系统字体 | 可观察的后台任务；视口替换和分析块留在内部管线 |
| 自动保存、备份、清理、个人库、首选项、最近工程、布局 | 声明存储资源；连续写入合并同目标最新待运行请求 |
| 设置传输、启动恢复与初始化 | 恢复遵守全局屏障，保留恢复日志和事务边界 |

具体入口清单用于检查新增业务是否绕过服务。下表中的任务均由所属协调器提交；资源准备、关键帧探测与原子写入保留底层实现。

| 所属模块／入口 | 任务类或内部阶段 |
|---|---|
| `Workspace` 工程工作流 | `CreateProjectTask`、`OpenProjectTask`、`MergeProjectsTask`、`SaveProjectTask`（含另存为与资源迁移）；失败回滚在父任务内收尾 |
| `Workspace` 媒体上下文 | `SwitchProjectMediaTask`、`SynchronizeProjectMediaTask`、`SwitchPreviewDecodeModeTask` |
| `Workspace` 时间后处理 | `TimingPostProcessingTask`；关键帧索引与缓存是同一任务的内部阶段 |
| `Workspace` 字幕交换／创建 | `ImportSubtitlesTask`、`ExportSubtitlesTask`、`CreateSubtitleClipsTask`、`BeginTimingCueTask` |
| `Workspace` 视频导出 | 烧录使用 `VideoExportTask`，仅封装使用 `SubtitleMuxTask`；导出面板和任务列表取消同一句柄 |
| `Workspace` 字体／样式／效果准备 | `ImportSubtitleFontTask`、`PrepareSubtitleStyleTask`、`ApplySubtitleStyleTask`、`ApplySubtitleTrackStyleTask`、`CaptureSubtitleStyleTask`、`ApplyEffectScriptTask` |
| `Workspace` 图片素材 | `AssetLibraryTask`；协调器先准备导入、放置、刷新、重新定位与收集，再提交工程修改 |
| `Workspace` 个人资源交换 | `ImportStylePresetsTask`、`ExportStylePresetsTask`、`ImportEffectScriptsTask`、`ExportEffectScriptTask` |
| `Workspace` 音频分析／设备 | `AudioAnalysisBatchTask`、`AudioCacheMigrationTask`、`ApplyAudioCalibrationTask`、`RebuildAudioOutputTask` |
| `Workspace` 自动持久化 | `AutomaticProjectPersistenceTask`；备份清理通过 `ProjectPersistenceTask` 或父任务阶段执行 |
| `Startup`／`Editing` 应用服务 | `ApplicationInitializationTask`、`EnumerateSystemFontsTask`、`PreferencesWriteTask`、`RecentProjectsWriteTask`、`PersonalLibraryTask` |
| `Layouts` 布局持久化 | `LayoutWriteTask`；保留协调器对布局交互的编排 |
| `Settings/Presets`／`Settings/Export` | `SettingsStyleImportTask`、`SettingsStyleExportTask`、`SettingsEffectImportTask`、`SettingsEffectExportTask`、`SettingsExportPresetExportTask`；库增删改使用 `PersonalLibraryTask` |
| `Settings/Transfer` | `UserSettingsLayoutCaptureTask`、`UserSettingsExportTask`、`UserSettingsImportTask`、`UserSettingsRestoreStageTask`、`UserSettingsRestoreCancelTask` |

图片可通过“文件 → 导入图片”或项目素材面板导入。工程备份按[工作台](workbench.md#保存与恢复)中的手动文件流程恢复，设置恢复遵守上述恢复任务边界。播放循环、单帧解码、拖动定位、音频时钟与实时预览继续走内部实时管线，其所属耗时业务任务负责取消和完成。分析视口请求及每个分析块不会各自产生任务记录。

连续写入合并保留原任务身份、句柄与排队位置，不替换已运行请求，也不跨越屏障或中间的冲突写入。自动保存仅捕获已提交内容和视图状态，不提交草稿、不改变焦点或 Undo。另存为分别重定位保存快照和最新编辑状态，跨目录迁移清除会恢复旧路径的历史，保留无效输入草稿。

工程关闭先询问用户；取消关闭不影响任务。确认后封闭该作用域、取消可取消任务并等待真实清理及提交，再通过受控通道执行最终保存。失败时先恢复接单再恢复持久化计时器，其他工程继续运行。应用退出取消可取消后台工作并排空必须完成的持久化。

启动先异步完成设置恢复与初始化，再构造欢迎窗口并主动显示，保证异步返回后不会留下仅有 Dock 图标的应用。

任务入口位于工程主窗口标题栏最右侧，在 Windows 上位于按原生测量预留的窗口按钮区域左侧。通用标题栏提供由宿主填充的内容插槽，不读取任务服务。各窗口共享任务数据并独立持有右缘对齐的 Flyout。任务卡片采用两行：首行是名称、阶段／状态和方形取消图标按钮，第二行是进度条；不展示工程名或“应用”等上下文文字。未知总量在运行时显示不定进度，历史进度静止。弹层使用统一字体与卡片样式，宽度随可用空间约束，无横向滚动。

运行／排队项按提交顺序，历史按结束时间倒序，保留本次启动最近 50 条轻量记录，“清空已完成”图标按钮只移除历史。状态终结立即通知，进度合并约每秒 10 次；历史不保存快照、执行闭包或原生资源，也不持久化。

原生 macOS／Windows 的标题栏拖动、窗口缩放及平台按钮命中仍需真实窗口验收。扫描本身的耗时需使用实际媒体另行测量。

## 编辑与呈现

`ProjectEditor` 提交不可变快照，多 Clip 编辑/导入使用原子事务；校验失败不改变工程和历史。布局变化保留同一会话与固定面板实例。个人布局、样式、脚本、偏好和日志与工程内容独立。

特效 DSL 版本 2 在 Core 解析命名作用域和字素安全的文字组，由 `CompileTarget`／`ComposeTarget` 同时返回准备后的字幕范围与动画轨道，Application 一次事务应用完整冻结结果。生成块以脚本 ID、作用域名、父范围 ID 匹配，稳定分组签名与文字跨度决定是否复用身份；重新应用替换该块结果。Rendering 与 Desktop 消费原生范围和轨道，不在播放时解释脚本，详见[特效脚本](effect-dsl.md)。

范围 Offset、主曲线／分量曲线的 Reverse 和生成来源从工程格式 13 开始支持，并保留在当前格式中。读取 v13 之前的格式时，这些可选字段默认为零 Offset、false Reverse、无生成来源；v12 保留既有文字动画字段。旧版本按字段所属对象拒绝新能力，已有字幕位置 Offset 仍合法。生成父引用必须属于同一句字幕且不形成环；手工编辑或文字映射后不要求子范围跨度包含于父范围。

原生样式保存字距、独立填充/描边模糊和换行模式；从旧版本迁移时新增数值为零、换行模式为按字素换行，保留原有外观。新版本要求这些字段完整，旧版本夹带新增字段会被拒绝。字距及双模糊可作为仅适用于字幕的动画轨道和 DSL 属性；整层模糊与阴影模糊保留独立含义。

预览复用编辑/导出的场景几何，显示 SDR 派生图；渲染器在显示/编码前保持线性 F16，worker 导出读取原始媒体帧。异步结果交付前检查时间、修订、请求身份和所有权。

## 参与开发

C# 使用 Allman 花括号、文件级命名空间、单文件单顶层类型，适当使用 `var` 和目标类型 `new()`。开启可空检查、分析器和警告视为错误，公开/受保护 API 文档描述契约。

界面标签与错误提示使用现有语言包；字幕内容、Unicode 测试数据、用户命名、路径和序列化标识保持其业务含义。

测试放在所属 Tests 项目并定向运行。修改原生契约时同步托管绑定、worker 协议、能力/版本检查与布局测试。提交围绕明确范围，采用简短英文 `feat:`、`fix:` 或 `chore:` 标题；排除生成产物，执行 `git diff --check` 和受影响测试。

### 维护文档

用户操作放在快速开始和专题指南中，模块契约放在本页及集成指南中，可执行的构建／发布命令放在构建与发布指南中。优先更新已有主题，再考虑新增指南。`docs/en/` 与 `docs/zh-cn/` 保持相同文件名和行为说明，并维护语言切换与相对链接。版本号以所属常量为准，命令以实际脚本为准。

`docs/assets/` 只保存被维护指南引用的最终图片，`docs/en/examples/effects/` 保存两种语言共用的可运行特效示例。生成产物、日志、临时验证报告和 AI 会话材料不进入维护文档。编辑后检查链接、锚点、图片引用、双语对应和 `git diff --check`；修改可执行示例或记录需要验证的行为时，定向运行相关测试。

继续阅读[工作区集成](composable-workspace.md)、[媒体](media.md)、[渲染](rendering.md)或[构建](building.md)。
