# 特效脚本

[English](../en/effect-dsl.md) · [简体中文](effect-dsl.md) · [全部指南](README.md)

## 添加与编辑特效

1. 选择一个或多个片段，在**特效预设**选择卡片并点击**添加特效**。卡片按源码默认参数预览和添加。双击也可添加模板，单击只选择。
2. 在时间轴选择新增附属块，打开**特效**实例检查器。可修改参数、内容时钟的开始／结束和启用状态；顶部下拉框可切换实例。**恢复默认参数**读取源码默认值，**删除特效**只移除当前实例。
3. 在时间轴选择父片段，返回手工动画。直接上下拖动任意特效块，可在该片段内调整单行顺序，包括手工属性、文字范围的各外观状态、蒙版节点和脚本实例。左右拖动实例块移动时段，左右拖动边缘裁剪；起始拖动方向锁定本次操作。折叠按钮和关键帧保留各自操作。松开形成一条撤销记录，Esc 或失去捕获取消当前手势。

每次添加都是独立实例，保存源码快照、参数、次序和时段，后续效果库修改不会自动更新它。参数暂不支持关键帧。手工轨道和手建文字范围继续独立编辑；生成的文字组和通道只存在于执行计划中。

行顺序作为统一特效栈随工程保存，按自下而上的顺序求值。手工动画行在所在位置写入当前帧的采样值，脚本实例继续组合当时已有的通道值。手工行与脚本实例交换位置也会改变结果；各文字范围仍保留自身的局部几何层级。

打开**编辑 → 效果库 → 特效脚本**创建或编辑模板。内置源码只读，其预览参数仍可调整。**验证**报告源位置，**定位错误**移动光标。输入时或 Cmd/Ctrl + Space 打开补全，方向键选择，Enter/Tab 插入，Esc 关闭。

新脚本统一使用 **version 3**；旧版本原文需要改写。旧工程中已有手工动画继续保留。

## 草稿预览

选中一个脚本，循环预览当前源码，包括未保存修改。可调整**预览文本**、**时长（秒）**、**预览样式**及声明参数，渲染使用所选样式及应用字体库。试调参数不改写源码默认值，也不保存模板。

使用播放／暂停、循环和进度条检查帧。预览区空格切换播放，文本输入保留正常空格。Cmd/Ctrl＋滚轮缩放，左键拖动平移；切换页面暂停播放。源码停止输入约 200 ms 后更新，错误只显示在预览区。非法参数保留原文，Esc 只恢复当前字段。

导出窗口分享已保存的个人素材，不会保存当前编辑草稿。**导出原文…**也允许选择内置源码，详见[分享样式与脚本](workbench.md#分享样式与脚本)。

## 参数与控制流

头部声明 ID、一次明确的短片段策略，以及作用域之前的常量参数默认值。关键词统一使用小写。参数名区分大小写，由字母、数字和下划线组成，以字母或下划线开头；显示标签属于作者文本，切换界面语言时保留：

```text
effect "personal-pulse" version 3
short-clip compress
var duration = 0.1s label "时长" suggest ["快": 50ms, "慢": 200ms]
var strength = 1.25 label "幅度" options ["轻": 1.25, "强": 1.5]
var enabled = true
var title = "脉冲"
var movement = vec2(0, -12)
var tilt = vec3(10, 20, 0)
var color = rgba(1, 0.25, 0, 1)
```

`options` 使用严格下拉，默认值必须属于选项；`suggest` 提供带名称的建议值，同时允许同类型自定义输入。类型包括数字、时长、布尔、文本、Vector2、Vector3 和线性 RGBA。默认值和选项是与目标无关的常量，时长支持秒及毫秒。

表达式支持括号、算术、比较、`and/or/not` 和向量分量。数字与时间表达式使用精确有理数，`at i / 3` 不会被舍入为六位小数；单位会校验，布尔表达式短路。

```text
if enabled
    for i in range(0, 3)
        scope pulse range(i + 1, 1)
            delay i * 100ms
            segment grow fixed duration pingpong
                at 0 scale base
                at 1 scale factor(strength, strength)
            end
            segment rest flex 1
            end
        end
    end
elif strength > 1
    scope fallback current
        segment stay flex 1
            at 0 scale base
            at 1 scale base
        end
    end
else
end
```

上面的循环示例需要字幕至少包含三个完整字素。`if / elif / else / end` 与 `for i in range(start, stop[, step]) / end` 在对应层级展开作用域、段或关键帧。循环为整数半开区间，索引只读，步长不能为零；所有分支检查语法、符号和类型，仅命中分支求值。空分支透传，不支持赋值、while、文件访问、自定义函数或逐帧控制流。

`clip.duration` 是父片段时长，`effect.duration` 是实例时长。分组后，`group.index` 表示过滤空白、应用 `order` 后从零开始的启动顺序，`group.count` 为组数；它们可控制该组的段和关键帧，不能配置当前作用域如何分组。

## 组合特效实例

手工动画行与启用的实例按照统一特效栈自下而上执行；没有自定义顺序的片段仍以手工动画在下、实例在上。脚本通道读取同一帧的上游值：factor 乘以上游动画，offset 在其上相加，绝对值覆盖；手工行在执行时以当前帧采样值替换其完整目标。未声明区间以及实例半开时段之外的帧透传。交换行可能改变结果，禁用或删除实例会露出其上游贡献。

移动保持时长，裁剪或改长后重新分配 fixed／flex。父片段裁剪取实例交集，空交集删除；拉伸同比例映射时间。复制、拆分产生新实例 ID。修改文字会根据当前原文重新生成临时分组；删除实例引用的父文字范围会删除对应实例。

## 文字分组

### 选择原文范围

| 目标 | 原文范围 |
|---|---|
| `scope letters current` | 特效面板当前选中的文字范围；未选范围时为整句字幕 |
| `scope letters subtitle` | 整句字幕，不受面板当前范围影响 |
| `scope letters range(4, 2)` | 从原始整句字幕的第 4 个字素起，取 2 个完整字素 |

`range(start, count)` 使用从 1 开始的字素序号，空格和硬换行也参与序号计数，完整 CRLF 算一个字素；它不是 UTF-16 码元索引，也不是相对于当前选区的索引。例如 `你好 世界` 的 `range(4, 2)` 是 `世界`。组合字符和 emoji 序列保持完整字素。范围越界时整次应用失败，不自动裁剪。

默认 `unit group` 将目标整体作为一组。`current` 选中已有文字范围时复用该范围；目标是整句时作用于图层，不创建文字范围，空字幕也可以执行整层特效。`range(...)` 创建临时执行范围。其他文字分组必须有匹配的字幕；空目标或纯空白目标不生成文字组，也不消耗错峰序号。

### 文字分组

| 单位 | 分组规则 |
|---|---|
| `group` | 整个目标为一组；默认值 |
| `grapheme` | 每个非空白完整字素为一组；跳过空格与硬换行 |
| `chunk(2)` | 每个硬行内连续两个字素一组；行内空格算字素且随组变换；末尾不足两个仍保留 |
| `word` | 连续非空白文字为一组，标点附着在词上；没有空格的中文整段为一个词组 |
| `line` | 每个显式 LF 或 CRLF 行为一组，不包含行末换行 |
| `paragraph` | 空行或纯空白硬行分段；段落内部硬换行保留在同一个连续范围中 |
| `split(" ", "，", "::")` | 在整个目标中匹配字面分隔符；分隔符保留在字幕中，但不属于动画组 |

定长文字组用 `chunk(N)`，`N` 是正数字素数量；真正的段落用 `paragraph`，硬行用 `line`。每组都对应原文中的一个连续范围，共用一个轴心；跨行段落也只有一个组轴心。

硬行识别 LF 和 CRLF。`chunk` 在每个硬行重新计数；纯空白分块、行、段落或 split 结果在分配组序号前跳过。排版自动折行不产生新组。显式作用范围截断了一个单词时，分组仍只在该作用范围内进行。

`split` 按区分大小写的字面文本匹配，同一位置优先最长分隔符，不重叠匹配。它覆盖整个目标，可跨硬行，因此分隔符可以包含完整 `\r\n`。空分隔符和重复分隔符非法。每组开头、结尾的水平空格和 Tab 不纳入范围，原始文字不删除；未命中分隔符时，目标中的非纯空白内容作为一组。命中若切开组合字、emoji 或 CRLF 的完整字素，整次应用失败。

分隔符字符串支持 `\"`、`\\`、`\n`、`\r`、`\t`，未知转义拒绝。`#` 仅在引号外开始注释，所以 `split("#")` 合法。当前语言不提供正则、自然语言分词、自动折行分组、随机顺序或嵌套组时钟。

### 启动顺序与视觉状态

默认是 `delay 0ms`、`stagger 0ms`、`order forward`、`state normal`。过滤空白并确定顺序后，组序号 `i` 从零开始，该组启动时间为：

```text
特效实例起点 + delay + i × stagger
```

实例时间使用包含 `AnimationOffset` 的图层内容时钟。`order reverse` 反转组的启动顺序，不改变字幕文字。`delay` 和 `stagger` 必须非负。各作用域使用自己声明的状态；即使面板选中其他状态，省略 `state` 仍为普通状态。`state active`、`state inactive` 作用于已有卡拉 OK 绘制通道，不创建逐字计时，也不改变字形几何。

### 固定重复与弹性循环

`segment pulse fixed 150ms repeat 3 pingpong` 执行三次完整往返，压缩前共 900 ms。`repeat` 为正整数，仅用于固定段，默认一次。正向重复段必须回到起始值再接下一次。Pingpong 会反转原始曲线本身，包括 `power(...)`；不会把同一条正向缓动简单套到返程。

作用域内可写循环 flex 段：

```text
segment pulse flex 1 cycle 300ms pingpong
    at 0 scale base ease-in-out
    at 1 scale factor(1.25, 1.25)
end
```

`cycle 300ms` 是完整周期；有 pingpong 时包含两程。编译器用精确有理数向下取整，只输出完整周期，余下时间保持最后值。例如分配到 800 ms 时输出两个 300 ms 周期，再保持 200 ms。正向 cycle 的起止值必须相等，以便不同 Clip 时长下重复接合。flex 使用 pingpong 时必须同时指定 cycle；flex 不能使用 repeat，fixed 不能使用 cycle。

### 短片段规则

不同文字组可以展开不同的段。设 `Fi` 是该组固定总时长，`Qi` 是考虑 flex 权重后，保证每个循环段至少一个完整周期所需的最小弹性时长。对 `N` 组、实例时长 `T`：

```text
E = delay + max(N − 1, 0) × stagger + max(Fi + Qi)
k = T < E 时取 T / E，否则为 1
```

`reject` 在 `T < E` 时拒绝应用；`compress` 将 delay、stagger、固定时长和 cycle 周期统一乘以 `k`。所有组共用压缩后的动画窗口，各组扣除自己的固定总时长，再按自己的 flex 权重分配剩余时长。非循环段压缩为零时，折叠的关键帧值必须一致；空展开直接透传上游结果。

## 属性与值

| 属性 | 值 |
|---|---|
| `position` | 像素向量：`base`、`(100, 20)`、`offset(-250, 0)` |
| `scale` | 向量：`base`、`(1, 1)`、`factor(0.2, 0.2)` |
| `rotation` | Z 轴角度：`0`、`base`、`offset(15)`；或 XYZ 角度：`vec3(10, 20, 30)` |
| `rotation-x`、`rotation-y` | X／Y 轴角度：`0`、`base`、`offset(45)` |
| `perspective-distance` | 工程像素，0.001–1e9：`312.5`、`base`、`factor(0.75)` |
| `opacity` | 0–1 或 `base` |
| `fill`、`stroke` | 线性 `rgba(r, g, b, a)` 或 `base` |
| `blur` | 0–512 像素或 `base` |
| `stroke-width` | 0–4096 像素或 `base` |
| `font-size` | 字幕字号，0.01–4096 像素或 `base` |
| `letter-spacing` | 字幕字素间距，−4096–4096 像素或 `base` |
| `fill-blur`、`stroke-blur` | 字幕填充／描边模糊，0–512 像素或 `base` |
| `shadow-offset` | 字幕阴影偏移向量：`base`、`(3, 4)`、`offset(2, -1)` |
| `shadow-blur` | 字幕阴影模糊，0–512 像素或 `base` |
| `shadow-color` | 字幕阴影线性 `rgba(r, g, b, a)` 或 `base` |
| `path-progress` | 显式 0–1 |
| `mask-rectangle-top-left`、`mask-rectangle-bottom-right` | 蒙版局部坐标的矩形角向量 |
| `mask-position`、`mask-scale`、`mask-rotation` | 独立蒙版变换 |
| `mask-node(c,n).position`、`.in-handle`、`.out-handle` | 已有节点位置 / 相对控制柄向量 |

`base` 读取上游在当前帧的值，`offset` 相加，`factor` 相乘，绝对值覆盖对应分量。颜色和排版属性逐字素读取基础值，保留混合富文本样式。颜色采用非预乘线性 RGBA，RGB 可表达 HDR，alpha 为 0–1；界面 HEX 和字节 RGBA 使用 sRGB，脚本中使用 `rgba(...)`。

`position` 与 `scale` 使用完整二维向量。三维参数写作 `vec3(...)`，可直接赋给 `rotation` 驱动 XYZ 旋转，也可通过 `.x`、`.y`、`.z` 分量分别驱动各轴。角度保留完整圈数和分量曲线。文字范围的局部变换作用于已排版几何，保留排版占位；字号和字距会重新排版。整层、手建范围及临时组保留各自局部几何层级。

文字范围支持排版、绘制、阴影、局部位移／缩放／XYZ 旋转和透视距离。激活／未激活状态只支持绘制属性，沿用普通状态几何；透明度、整层模糊、路径和蒙版属性必须针对整层。不支持的目标会报告源位置。插值支持 `hold`、`linear`、`ease-in`、`ease-out`、`ease-in-out`、`power(正指数)`；每个点控制到下一点的过渡。

## 蒙版与校验

先创建几何，再使用蒙版脚本。节点选择器从 1 开始，解析已有节点，控制柄相对节点定位；静态 Anchor／Pivot／Offset 与蒙版动画位移独立。脚本不创建几何。

脚本 ID、作用域名、段名使用小写 ASCII 字母、数字、`.` 和 `-`，以字母开头，最多 64 字符。源码限制为 128 个作用域、128 个段、4096 个关键帧和 262144 个字符；控制嵌套最多 16 层，表达式树最多 64 层，单循环最多 4096 次，单目标最多展开 100000 条语句。输出范围、通道和关键帧仍有预算。越界、引用无效或目标验证失败时拒绝整个编辑，不添加撤销记录，不会悄悄截断结果。

## 原生工程与 ASS 交换

工程格式 v19 将源码快照和强类型参数与手工动画分别保存；支持的旧工程加载为空实例集合。保存 `.aeginext` 才能完整保留可编辑实例。

预览和视频导出使用原生有序栈。ASS 使用临时采样投影，不修改工程，并报告 `Ass.EffectInstances`：ASS 无法保留源码、参数和实例身份。已有采样和几何有损提示继续生效，范围平移及透视可能无法表达。SRT 及仅封装字幕路径对启用的视觉实例报告 `Srt.EffectInstances`；禁用实例不贡献画面。详见 [ASS 互转换能力](ass-compatibility.md)。

## 示例

- [包含条件与循环的参数化字母效果](../en/examples/effects/parameterized-letters.aegifx)
- [内置淡入淡出](../../src/AegiNext.Core/Effects/Scripts/fade-in-out.aegifx)
- [滑入/弹出](../en/examples/effects/slide-pop.aegifx)
- [三维倾斜与透视](../en/examples/effects/spatial-tilt.aegifx)
- [线性颜色循环](../en/examples/effects/color-cycle.aegifx)
- [蒙版滑动](../en/examples/effects/mask-slide.aegifx)
- [蒙版形变](../en/examples/effects/mask-morph.aegifx)
- [内置逐字弹跳](../../src/AegiNext.Core/Effects/Scripts/letter-bounce.aegifx)
- [内置逐字放大往返](../../src/AegiNext.Core/Effects/Scripts/letter-pulse.aegifx)
- [按单词分组](../en/examples/effects/grouped-words.aegifx)
- [两个字素一组](../en/examples/effects/grouped-pairs.aegifx)
- [按字面分隔符分组](../en/examples/effects/grouped-split.aegifx)
- [按硬行分组](../en/examples/effects/grouped-lines.aegifx)
- [整句淡入淡出与逐组放大](../en/examples/effects/scoped-fade-pulse.aegifx)

解析／编译入口为 `src/AegiNext.Core/Effects/`。原生集成使用 `EffectScriptParser.Parse`、`EffectScriptParameterBinder.Bind` 和 `EffectScriptCompiler.CompileInstance`；返回的范围及仿射通道是临时结果。工程编辑使用原子的特效实例事务。辅助编写可使用仓库[特效 DSL Skill](../../.agents/skills/aeginext-effect-dsl/SKILL.md)。
