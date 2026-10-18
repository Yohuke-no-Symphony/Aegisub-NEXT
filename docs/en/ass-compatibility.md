# ASS interoperability

[English](ass-compatibility.md) · [简体中文](../zh-cn/ass-compatibility.md) · [All guides](README.md)

AegiNext uses native subtitle, text-range, karaoke, animation, and mask models. ASS is an exchange format: import converts supported content into native data, while export produces a target file and loss diagnostics. Project persistence does not pass through ASS, so exchange limits do not rewrite the native project.

See the [ASS tag audit](ass-tag-audit.md) for native relationships, conversion in each direction, and implementation status. See [subtitle editing](subtitle-editing.md) for the workflow.

## Corresponding content

| Content | Native mapping and limits |
|---|---|
| Named styles | Export shares named Style definitions referenced by Dialogue events; base properties inherit from the table, with only necessary local differences and effects in the text. Complete source-style tables and override provenance do not survive; see below |
| Text, fonts, and emphasis | Family, size, spacing, bold, italic, underline, strikeout, and inline overrides; embedded fonts, named variants, and custom line height cannot be fully stored in ASS |
| Color and alpha | Fill, stroke, shadow, and karaoke fill before and after activation; ASS uses 8-bit sRGB, so native linear colors, HDR, and independent channels can incur quantization or range loss |
| Layout and placement | Nine-way alignment, three margins, static position, nonnegative 2D scale, and Z rotation; font metrics and the order of scaling and wrapping differ |
| Rotation-origin exchange | Whole-line rotation with explicit placement or measurable fixed automatic layout and positive fixed scale, or static rotation with a single constant-speed move, can map `\org` through existing local pivots and positions; no native fixed-canvas rotation mode is added |
| Karaoke | `\k`, `\kf`/`\K`, `\ko`, and `\kt`; complete group timing and range appearance are stored independently without automatic per-grapheme splitting |
| Animation | Supported numeric, color, shadow-offset, straight-motion, and opacity-envelope animations; power curves or ordered operations convert directly, while other evaluable text tracks are sampled as needed |
| Masks | Rectangular and supported vector clips; representable rectangle animation uses `\t`, while other native mask animation expands into per-frame events |
| Static shapes | Isolated, explicitly placed simple closed ASS drawings import into native paths; independent contours without holes split into shape layers. Static native shapes export as drawing coordinates, with lossy stroke approximation. Mixed flow, automatic drawing placement, clipping, shadow/blur, and animation are outside this import scope |

Parameter correspondence does not mean pixel equivalence. Each renderer retains its own shaping, rasterization, shadow contours, blur, and fade composition. Differing shadow conditions produce `Ass.ShadowComposition`; blur has corresponding appearance and channel-coupling diagnostics.

## Named styles and text differences

Export writes named definitions in `[V4+ Styles]`, referenced by each Dialogue event's Style field. Subtitles with the same name and the same ASS output fields other than margins share a definition. Different native names remain separate even when their fields match. A conflicting definition with the same name receives a unique suffix. Commas, surrounding whitespace, and conflicting names produce `Ass.StyleName` diagnostics without renaming the native project style.

Properties provided by the table, including font family, size, Boolean emphasis, base colors, border, and alignment, are inherited instead of fully expanded into every text body. Without local overrides or additional effects, the body can contain only text. Local differences use the necessary tags. Later style, karaoke, or animation-range boundaries still use `\r` to restore the current Dialogue style, then reapply local differences, geometry, and required animation while retaining the event clock. Editing an exported Style property affects text that inherits it; explicit local overrides in the output remain independent.

Comparisons use actual ASS encodings rather than native floating-point values: numbers use at most nine decimal places, with RGB and alpha compared separately at their 8-bit output precision. Local values that quantize to the baseline are exported as inheritance without preserving explicit-override identity. Actual precision, font-identity, and appearance-semantic losses still produce diagnostics. `\blur`, necessary `\q`, precise weight, independent or signed shadow axes, placement, geometry, karaoke, animation, and masks remain in text as needed. When negative spacing or shadow cannot be inherited from the corresponding style scalar, the table uses zero and the necessary `\fsp` or `\xshad/\yshad` preserves the value in text.

Shared styles handle left, right, and vertical margins independently. If every subtitle in a group has the same encoded value on an axis, Style stores that common value and Dialogue uses zero to inherit it. Otherwise, Style stores zero on that axis and each Dialogue stores its actual value. Margin differences alone therefore do not split styles, and a true zero cannot accidentally inherit another subtitle's nonzero value. Grouping compares serialized values without first truncating them to integers. Events produced by mask expansion reuse the same source style reference and margins.

These rules preserve corresponding style properties and sharing, without reconstructing the complete source file. Unused Style definitions, original `\rStyle` references, whether an attribute came from a Style or text override, and original margin override/inheritance syntax are not retained. SecondaryColour unused by timed text is not stored as an independent source-style field. Style Encoding is not restored and export writes a fixed value. Scale and rotation can become native geometry and return as equivalent tags rather than their original Style fields. Save `.aeginext` to continue editing with native data intact.

## Exchange limits

ASS dialogue time uses centiseconds and transform time uses integer milliseconds; the project retains exact time internally. Export rounds dialogue starts down and ends up, quantizes karaoke endpoints independently, and retains at least one centisecond for collapsed positive durations. Numbers use at most nine decimal places; actual numeric, color, and alpha rounding produces separate diagnostics.

For external files, the three effective margins come from the final reset style in the event, including a `\rStyle` after the text. Each nonzero Dialogue margin overrides its corresponding style value; zero inherits it. Style resets retain event alignment and explicit placement.

Independent native fill, stroke, and shadow blur cannot fully map to the single ASS `\blur` channel. Nonuniform border or blur resampling uses an approximation with a loss diagnostic. Missing valid `LayoutRes` values require an estimate from available resolution information.

Continuous karaoke border/shadow animation uses lossy text export. Each range's shared channel selects one track in inactive, active, then normal order, retaining text and timing while both highlight states share the track. Blur merges into one channel with its edge selected at the event origin. Unselected tracks, corresponding activation jumps, and state identity are lost with diagnostics. Native projects and advanced editing projection retain independent data.

Native mirroring, independent highlight-edge animation, overlapping range geometry, complex motion paths, layer blur, images, and shape composition may be approximated or omitted. Export changes only the output file; diagnostics identify omitted content and its reason.

Independent text-range translation, including grouped bounce POSITION tracks and static range Offset, has no direct ASS text-tag equivalent; full event or glyph baking remains unfinished. Export reports `Ass.RangeTranslation` and omits it rather than moving the entire subtitle. Reversed nonlinear curves require text-animation sampling, numeric/movement/opacity approximation, or mask event expansion according to the affected property; native project curves retain their exact timing.

ASS 3D rotation, shear, independent X/Y borders, repeated box edge blur, character-encoding overrides, and inline drawing lack complete current adapters. The tag audit distinguishes missing native representation, ASS format limits, and unfinished adapters.

## Rotation pivots in file exchange

Native tools continue to edit local pivots, positions, and animation. File import maps `\org` into existing pixel pivots and positions with explicit `\pos`, a convertible `\move`, or measurable fixed automatic layout, positive fixed scale, and geometry consistent across the line. Desktop and CLI provide final fixed layout measurements through the shared renderer, preserving origin coordinates without promising libass automatic-layout pixel equivalence. Fixed placement permits whole-line rotation animation; simultaneous movement and animated rotation do not use this mapping. The first valid origin in an event wins, survives `\r`, and acts as a fixed event property even when it appears after text or inside `\t`. Leading explanatory text in the `\t` tag list does not prevent origin extraction.

Fixed geometry is checked over the half-open visible event window `[Start, End)`. Transforms completed before the window, starting after it, or completing immediately at its start qualify only when both source operations and imported tracks prove constant and agree throughout that window. Relevant ordered tracks are normalized to constants after successful conversion for subsequent export. Changes within the window, source geometry requiring sampling, or discarded geometry transforms cannot be proved safe from endpoint values or remaining tracks.

Export converts supported native geometry into a fixed `\org` and equivalent placement, movement, and rotation tags. The origin is independent of the clipped first movement value, and events produced by mask expansion share it. Unedited imports within this scope can recover their effective origin and motion after project save/load. Other native pivots produce geometrically equivalent ASS expressions without promising identical native fields after reimport. Original tag presence, order, repetition, and spelling are not preserved.

Automatic origins without fixed layout measurements, dynamic scale, effective range translation/scale/rotation, zero scale axes, and nonuniform canvas resampling combined with rotation are outside the current exact adapter scope. Color-only ranges, grouping provenance, and ranges with effective unit scale and zero rotation do not prevent conversion. Export preserves whole-line geometry and animation across these ranges. Importing or exporting automatic placement or custom layout pivots requires complete fixed layout measurements; one static measurement cannot compensate for changing font size or spacing. Unsupported combinations or derived coordinates outside native limits discard only the origin adapter and retain other convertible content, without extending the native transform model or sampling origin compensation.

Identified numeric, timing, and layout losses are reported separately. When both integer-millisecond `\move` times are nonpositive, import uses the complete dialogue duration. A move with only its start before zero retains its true visible phase after clipping.

## Advanced ASS editing and file import

The advanced ASS editor provides a temporary text projection of native data. Unchanged timing, color precision, font resources, independent appearances, and preservable native tracks recover their original values. Ordinary text or inline-style changes do not automatically reduce the project to ASS precision. Editing a tag updates its corresponding representable property.

File-export style compaction does not apply to this editor. Advanced editing retains its complete style projection and the existing property-editing and native-data restoration rules.

Range Offset, generated-block origins and their parent references, local POSITION tracks, and unrepresentable reversed text curves are retained through this projection. Sampled ASS tags do not become the authoritative replacement for a preserved native reverse curve. To regroup or retime a generated effect after text edits, reapply its template as described in [Effect scripts](effect-dsl.md).

File import follows external ASS semantics, including source-operation order, empty-parameter resets, the karaoke clock, and resolution conversion. The advanced projection and external files have different boundaries. Projection text is not a complete project backup; save `.aeginext` to retain native capabilities.

The `\org` coordinate adapter is limited to file exchange. The advanced ASS editor continues to manage geometry through native pivot properties and does not accept external `\org` placement semantics.

## Diagnostics and budgets

Conversion results include diagnostics associated with subtitles, source tags, or properties. Unsupported tags, tracks, and some invalid values or transform times discard only the affected part. Invalid file structure, dialogue endpoints, UTF-8, or hard resource budgets reject the operation. Canceling import or export does not commit the conversion.

| Diagnostic category | Meaning |
|---|---|
| `Ass.StyleName` | Output style names were adjusted for separators, surrounding whitespace, or conflicting definitions; native names are unchanged |
| `Ass.UnsupportedTag`, `Ass.Drawing` | Source content lacks a current native adapter |
| `Ass.TransformLayout`, `Ass.TextRangeGeometry` | Parameters survive, but layout and transform order can change appearance |
| `Ass.RangeTranslation` | Output omits independent text-range translation |
| `Ass.RotationOrigin` | Origin parameters or geometry do not meet the current adapter conditions; other convertible content is retained |
| `Ass.KaraokeVisual`, `Ass.KaraokeAnimation` | Independent highlight edges or state tracks cannot be fully represented |
| `Ass.NumberPrecision`, `Ass.ColorPrecision`, `Ass.AlphaPrecision` | Output values undergo numeric or 8-bit quantization |
| `Ass.TimeQuantization`, `Ass.TransformTimeQuantization`, `Ass.KaraokeQuantization` | Output time undergoes quantization |
| `Ass.AnimationSampling`, `Ass.AnimationSamplingLimit` | Interpolation becomes samples; reaching a limit can exceed the sampling error target in some intervals |
| `Ass.MaskAnimationExpanded`, `Ass.MaskQuantization`, `Ass.MaskTimeQuantization` | Mask event expansion, coordinate or time quantization |
| `Ass.MixedMaskAnimation` | Import omits rectangle animation combined with a static vector; the full mixed-animation adapter remains unfinished |
| `Ass.Image`, `Ass.AnimatedImage`, `Ass.ShapeAnimation`, `Ass.ShapeBlend`, `Ass.ShapeBlur`, `Ass.ShapeOpenStroke` | Output omits the corresponding image or unsupported shape clip; supported static shapes still export |
| `Subtitle.Composition` | Subtitle layers contain unconverted tracks, motion paths, or non-normal blend modes |

Text-animation sampling uses at most 4096 points per track on a 1 ms grid, and export allows at most 100,000 events. These are exchange budgets. The limit of 256 animated text ranges per subtitle is also a native project validation constraint.

Implementation resides in [SubtitleFormats](../../src/AegiNext.SubtitleFormats/). [Core/Projects](../../src/AegiNext.Core/Projects/) defines native capabilities, and [Rendering](../../src/AegiNext.Rendering/) evaluates and composites the scene.
