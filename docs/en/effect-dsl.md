# Effect scripts

[English](effect-dsl.md) · [简体中文](../zh-cn/effect-dsl.md) · [All guides](README.md)

## Add and edit an effect

1. Select one or more clips, choose a card in **Effect presets**, adjust its parameters, and click **Add effect**. A double-click also adds the selected template; a single click only selects it.
2. Select the new block in Timeline or its instance in **Effects**. Edit parameters, content-clock start/end, enabled state, or evaluation order. **Reset parameters** restores the source defaults; **Remove effect** deletes only that instance.
3. Choose **Edit clip properties** to return to manual animation. Drag an instance block to move it or either edge to trim it. Release creates one Undo operation; Esc or capture loss cancels the gesture.

Each addition creates an independent instance with a source snapshot. Project files retain parameters, order, and time range; later library edits do not update existing instances. Parameters currently have no keyframes. Manual tracks and hand-created text ranges remain editable. Generated groups and channels exist only in the execution plan.

Open **Edit → Effect Library → Effect Scripts** to create or edit templates. Builtin source is read-only, while its preview parameters remain adjustable. **Validate** reports source locations and **Locate Error** moves the caret. Completion opens while typing or with Cmd/Ctrl + Space; arrows select, Enter/Tab insert, and Esc dismisses.

All new scripts use **version 3**. Earlier source versions must be rewritten; older native projects still retain their existing manual animation.

## Preview a draft

Select one script to loop its current source, including unsaved edits. Adjust **Preview text**, **Duration (seconds)**, **Preview style**, and declared parameters. Embedded fonts in the selected style are used. Parameter trials do not rewrite source defaults or save the template.

Use play/pause, loop, and the position slider to inspect a frame. Space toggles playback in the preview area; text inputs retain normal space input. Cmd/Ctrl + wheel zooms and left drag pans. Leaving the page pauses playback. Source changes update after about 200 ms; errors stay local to preview. Correct invalid parameter input or press Esc to restore only that field.

The export dialog shares saved personal materials without saving an editor draft. **Export source…** also permits builtin source. See [Share styles and scripts](workbench.md#share-styles-and-scripts).

## Parameters and control flow

The header declares the ID, one explicit short-clip policy, and constant parameter defaults before any scopes. Keywords use lowercase. Parameter names are case-sensitive letters, digits, and underscores, beginning with a letter or underscore. A label is author text, preserved across interface languages:

```text
effect "personal-pulse" version 3
short-clip compress
var duration = 0.1s label "Duration" suggest ["Fast": 50ms, "Slow": 200ms]
var strength = 1.25 label "Strength" options ["Subtle": 1.25, "Strong": 1.5]
var enabled = true
var title = "Pulse"
var movement = vec2(0, -12)
var tilt = vec3(10, 20, 0)
var color = rgba(1, 0.25, 0, 1)
```

`options` is a strict dropdown; its default must be one of the choices. `suggest` offers named values and accepts a custom value of the same type. Available types are number, duration, boolean, string, Vector2, Vector3, and linear RGBA. Choices and defaults are target-independent constants. Durations accept seconds or milliseconds.

Expressions support parentheses, arithmetic, comparisons, `and/or/not`, and vector components. Numeric and time expressions use exact rational calculation; `at i / 3` does not round its time to six decimals. Units are checked. Boolean expressions short-circuit.

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

The loop example requires at least three subtitle graphemes. `if / elif / else / end` and `for i in range(start, stop[, step]) / end` expand scopes, segments, or keyframes at their corresponding nesting level. Loops use integer half-open ranges and a read-only index; step cannot be zero. All branches receive syntax, symbol, and type checking, while only selected branches evaluate. Empty branches pass through. There is no assignment, while loop, file access, custom function, or per-frame control flow.

`clip.duration` is the parent clip's duration; `effect.duration` is the instance's duration. After grouping, `group.index` is the zero-based start order after whitespace filtering and `order`, and `group.count` is the resulting count. These group values can control that group's segments and keyframes; they cannot configure the current scope's grouping.

## Combining instances

The manual animation is evaluated first, followed by enabled instances in their saved order. Every channel reads the upstream value at that same frame: factor multiplies animated upstream input, offset adds to it, and an absolute value replaces it. Undeclared intervals and frames outside the instance's half-open time range pass through. Reordering two instances can change the result; disabling or removing one restores its upstream contribution.

Moving preserves instance duration. Trimming or changing duration reallocates fixed/flex timing. Parent trimming intersects attached ranges and removes empty instances; stretching maps their times proportionally. Copy and split create new instance IDs. Text edits rebuild temporary grouping from the current text; deleting an instance's selected parent text range deletes that instance.

## Grouped text

### Select the source text

| Target | Source |
|---|---|
| `scope letters current` | The text range selected in Effects, or the entire subtitle when no range is selected |
| `scope letters subtitle` | The entire subtitle, regardless of the selected range |
| `scope letters range(4, 2)` | Two complete graphemes starting at grapheme 4 of the original full subtitle |

`range(start, count)` uses one-based grapheme indices, including spaces and hard line breaks in the index; a complete CRLF is one grapheme. It does not use UTF-16 code units or indices relative to the current selection. In `你好 世界`, `range(4, 2)` selects `世界`. An emoji sequence or a combining character remains one complete grapheme. Out-of-bounds ranges fail the entire application; the compiler does not clip them.

The default `unit group` treats the selected source as one group. With `current` and an existing text range it reuses that range. With a whole-subtitle target it applies to the layer and creates no text range, including when the subtitle is empty. `range(...)` creates a temporary execution range. Text-specific units require a matching subtitle; an empty or all-whitespace selection produces no text groups and consumes no stagger slots.

### Group the source

| Unit | Grouping contract |
|---|---|
| `group` | One complete selected target; default |
| `grapheme` | One complete non-whitespace grapheme; spaces and hard breaks are skipped |
| `chunk(2)` | Consecutive pairs of graphemes within each hard line; inline spaces count and move with their group; a short final group is retained |
| `word` | Consecutive non-whitespace text; punctuation stays attached; Chinese without spaces forms one token |
| `line` | One group per explicit LF or CRLF line, excluding its line break |
| `paragraph` | Groups separated by empty or whitespace-only hard lines; internal hard breaks remain inside a paragraph's continuous range |
| `split(" ", "，", "::")` | Literal separators over the entire selected source; matched separators stay in the subtitle but are excluded from groups |

For a fixed-length text group, use `chunk(N)` with a positive grapheme count. For actual paragraphs use `paragraph`; hard lines use `line`. Each group is one continuous original-text range with one shared pivot, including groups spanning several lines.

Hard-line units recognize LF and CRLF. Chunking restarts after each hard line; whitespace-only chunks, lines, paragraphs, and split results are skipped before group indices are assigned. Layout wrapping does not create new groups. A word or group truncated by an explicit scope remains inside that scope.

`split` matches case-sensitive literal text, takes the longest matching separator at a position, and proceeds without overlapping matches. It operates across hard lines, so a separator can contain a complete `\r\n`. Empty or duplicate separators are invalid. Leading/trailing horizontal spaces and tabs are excluded from each emitted range; original subtitle text is preserved. With no match, the selected non-whitespace text becomes one group. A match that cuts a combining grapheme, emoji sequence, or CRLF rejects the entire application.

Quoted separators support `\"`, `\\`, `\n`, `\r`, and `\t`; unknown escapes fail validation. `#` begins a comment only outside quotes, so `split("#")` is valid. Regexes, natural-language tokenization, soft-layout-line grouping, random order, and nested group clocks are not language features.

### Start groups and choose a state

Defaults are `delay 0ms`, `stagger 0ms`, `order forward`, and `state normal`. For group index `i`, starting at zero after whitespace filtering and ordering, its start is:

```text
Effect instance start + delay + i × stagger
```

Instance times use the layer's content clock, including `AnimationOffset`. `order reverse` reverses group start order while preserving the subtitle's text. Delay and stagger are nonnegative. Each scope uses the scope's declared state and defaults to Normal even if another state is selected in Effects. `state active` and `state inactive` target the existing karaoke painting channels; they do not create karaoke timing or change geometry.

### Repeat and cycle

`segment pulse fixed 150ms repeat 3 pingpong` performs three full forward/return pairs, totaling 900 ms before compression. `repeat` is a positive integer and applies only to fixed segments; the default is one. A forward repeated segment must join back to its starting values. Pingpong reverses the source curve itself, including `power(...)`, rather than assigning the same forward easing to the return leg.

Inside a scope, a cyclic flex segment can be written as:

```text
segment pulse flex 1 cycle 300ms pingpong
    at 0 scale base ease-in-out
    at 1 scale factor(1.25, 1.25)
end
```

`cycle 300ms` is the complete period, including both legs when using pingpong. The compiler emits only complete periods using an exact rational floor; any remaining time holds the last value. An 800 ms allocation therefore emits two 300 ms cycles followed by a 200 ms hold. A forward cycle must have equal first and last values so it joins across different Clip lengths. `pingpong` on flex requires `cycle`; `repeat` on flex and `cycle` on fixed are invalid.

### Short Clips

Each group may expand a different set of segments. Let `Fi` be its fixed total and `Qi` the minimum flex allocation needed to fit a complete period in each cyclic segment, accounting for its flex weights. For `N` groups and instance duration `T`:

```text
E = delay + max(N − 1, 0) × stagger + max(Fi + Qi)
k = T / E when T < E; otherwise 1
```

`reject` refuses `T < E`. `compress` scales delay, stagger, fixed durations, and cycle periods together by `k`. Each group shares the resulting animation window; after its fixed total is deducted, its own flex weights divide the remainder. Zero-duration noncyclic segments must have identical collapsed endpoints. Empty expansion passes the upstream result through.

## Properties and values

| Property | Value |
|---|---|
| `position` | Pixel vector: `base`, `(100, 20)`, `offset(-250, 0)` |
| `scale` | Vector: `base`, `(1, 1)`, `factor(0.2, 0.2)` |
| `rotation` | Z degrees: `0`, `base`, `offset(15)`; or XYZ degrees: `vec3(10, 20, 30)` |
| `rotation-x`, `rotation-y` | X/Y degrees: `0`, `base`, `offset(45)` |
| `perspective-distance` | Project pixels, 0.001–1e9: `312.5`, `base`, `factor(0.75)` |
| `opacity` | 0–1 or `base` |
| `fill`, `stroke` | Linear `rgba(r, g, b, a)` or `base` |
| `blur` | 0–512 pixels or `base` |
| `stroke-width` | 0–4096 pixels or `base` |
| `font-size` | Subtitle font size, 0.01–4096 pixels or `base` |
| `letter-spacing` | Subtitle grapheme spacing, −4096–4096 pixels or `base` |
| `fill-blur`, `stroke-blur` | Subtitle fill / outline blur, 0–512 pixels or `base` |
| `shadow-offset` | Subtitle shadow offset vector: `base`, `(3, 4)`, `offset(2, -1)` |
| `shadow-blur` | Subtitle shadow blur, 0–512 pixels or `base` |
| `shadow-color` | Subtitle shadow linear `rgba(r, g, b, a)` or `base` |
| `path-progress` | Explicit 0–1 |
| `mask-rectangle-top-left`, `mask-rectangle-bottom-right` | Mask-local corner vectors |
| `mask-position`, `mask-scale`, `mask-rotation` | Independent mask transform |
| `mask-node(c,n).position`, `.in-handle`, `.out-handle` | Existing node position / relative handle vectors |

`base` reads the upstream value at the current frame. `offset` adds to it and `factor` multiplies it; an absolute value replaces that component. Color and typography bases are evaluated per grapheme, preserving mixed rich text. Colors are straight linear RGBA, including HDR RGB values and alpha from 0 to 1. UI HEX and byte RGBA input uses sRGB. Script colors use `rgba(...)`.

`position` and `scale` require complete vectors. Vector3 parameters use `vec3(...)`; assign one directly to `rotation` for XYZ rotation, or read `.x`, `.y`, and `.z` to drive individual axes. Rotation keeps complete turns and independent component curves. A range's local transform acts on its laid-out geometry without changing occupancy; font size and spacing reflow. Whole-layer, manual-range, and generated-group transforms retain their local geometry hierarchy.

Text ranges support typography, paint, shadow, local position/scale/XYZ rotation, and perspective distance. Active/inactive states support painting properties and share Normal geometry. Opacity, composited layer blur, paths, and masks require a whole-layer target. Unsupported properties fail with source locations. Easing is `hold`, `linear`, `ease-in`, `ease-out`, `ease-in-out`, or `power(positiveExponent)`; each point controls the transition to the next point.

## Masks and validation

Create mask geometry before using mask scripts. One-based node selectors resolve existing nodes, with handles relative to them. Static Anchor/Pivot/Offset remains independent from animated mask translation. Scripts never create geometry.

Script IDs and declared scope/segment names use lowercase ASCII letters, digits, `.` and `-`, start with a letter, and contain at most 64 characters. Source limits include 128 scopes, 128 segments, 4096 keyframes, and 262144 characters. Control nesting is limited to 16 levels and expression trees to 64 levels, one loop to 4096 iterations, and one target to 100000 expanded statements. Output range/track/keyframe budgets also apply. Overflow, invalid references, and failed target validation reject the entire edit without adding an Undo entry; nothing is silently truncated.

## Native project and ASS exchange

Project format v19 saves source snapshots and typed parameters separately from manual animation. Older supported projects load with an empty instance collection. Save `.aeginext` to retain complete editable instances.

Preview and video export use the native ordered stack. ASS creates a temporary sampled projection, without modifying the project, and reports `Ass.EffectInstances`: source, parameters, and instance identity cannot be retained in ASS. Existing sampling and geometry loss diagnostics still apply; range translation and perspective may be unrepresentable. SRT and subtitle-only mux paths report `Srt.EffectInstances` for enabled visual instances. Disabled instances produce no visual contribution. See [ASS interoperability](ass-compatibility.md).

## Examples

- [Parameterized letters with conditions and loops](examples/effects/parameterized-letters.aegifx)
- [Builtin fade](../../src/AegiNext.Core/Effects/Scripts/fade-in-out.aegifx)
- [Slide/pop](examples/effects/slide-pop.aegifx)
- [3D tilt and perspective](examples/effects/spatial-tilt.aegifx)
- [Linear color cycle](examples/effects/color-cycle.aegifx)
- [Mask slide](examples/effects/mask-slide.aegifx)
- [Mask morph](examples/effects/mask-morph.aegifx)
- [Builtin letter bounce](../../src/AegiNext.Core/Effects/Scripts/letter-bounce.aegifx)
- [Builtin letter pulse](../../src/AegiNext.Core/Effects/Scripts/letter-pulse.aegifx)
- [Grouped words](examples/effects/grouped-words.aegifx)
- [Pairs of graphemes](examples/effects/grouped-pairs.aegifx)
- [Literal separators](examples/effects/grouped-split.aegifx)
- [Hard lines](examples/effects/grouped-lines.aegifx)
- [Whole-subtitle fade with grouped pulses](examples/effects/scoped-fade-pulse.aegifx)

Parser/compiler: `src/AegiNext.Core/Effects/`. Native integration uses `EffectScriptParser.Parse`, `EffectScriptParameterBinder.Bind`, and `EffectScriptCompiler.CompileInstance`; the compiled ranges and affine channels are transient. Project changes use the atomic effect-instance operations. For assisted authoring, use the repository [effect DSL skill](../../.agents/skills/aeginext-effect-dsl/SKILL.md).
