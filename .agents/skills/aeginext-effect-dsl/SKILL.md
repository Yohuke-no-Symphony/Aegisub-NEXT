---
name: aeginext-effect-dsl
description: Write, review, debug, or extend AegiNext subtitle effect scripts (.aegifx), including typed parameters, bounded control flow, grouped text, adaptive timing, and ordered effect instances. Use for effect authoring or parser/compiler/editor integration, not unrelated shader or scripting languages.
---

# AegiNext effect authoring

Use the repository's actual v3 parser, binder, compiler, and runtime as the authority. The DSL expands a bounded animation plan when inputs change; it does not execute source every frame.

## Establish the target

1. Locate the checkout (`git rev-parse --show-toplevel` when already inside it). Resolve symlinks before deriving a root from this skill's location.
2. Read [the English guide](../../../docs/en/effect-dsl.md) or [the Chinese guide](../../../docs/zh-cn/effect-dsl.md), then the closest source in `src/AegiNext.Core/Effects/Scripts/` or `docs/en/examples/effects/`. Read [authoring checks](references/authoring-checks.md) for implementation anchors and regression boundaries.
3. Determine visual intent, target text/grouping, properties, start order, instance window, and short-clip policy. Use the supplied policy; otherwise state the chosen `compress` behavior. Ask only when a missing decision materially changes the effect.
4. Give personal scripts a unique stable lowercase ID. Preserve existing built-in IDs; library display names are separate from source IDs.

## Author v3 source

- Begin with `effect "personal-id" version 3`. Before the body, declare exactly one `short-clip compress` or `short-clip reject` and any `var` parameters, in either order. New source entry points reject v1/v2.
- Use typed constant defaults, for example `var duration = 100ms label "Duration" suggest ["Fast":50ms,"Slow":200ms]`. Types are number, duration, boolean, string, `vec2`, `vec3`, and linear `rgba`. `options [...]` is strict and must contain the default; `suggest [...]` accepts custom values of the same type. Keywords are lowercase and case-sensitive; preserve the case of parameter names and author text. Names use ASCII letters/digits/underscores, starting with a letter or underscore.
- Reference parameters by name. Arithmetic, comparisons, `and/or/not`, and valid vector/color components are typed. Numeric and duration calculations retain exact rationals: preserve `at i / 3` rather than rounding to six decimals. Use `EffectScriptParameterValue.Parse`/`ToSource` for editable literals, not culture-dependent formatting.
- Use `if / elif / else / end` and `for i in range(start, stop[, step]) / end`. Ranges are half-open integers with a nonzero step, including negative steps; the index is read-only. Top-level control generates scopes, scope control generates segments, and segment control generates keys. All branches receive syntax/symbol/type checks; only selected branches evaluate, with short-circuit boolean operators. Empty expansion passes upstream through.
- `clip.duration` is the parent visible duration; `effect.duration` is the instance window. `group.index/count` are available only inside the scope's animation body after grouping, whitespace filtering, and ordering. They cannot determine that scope's target, unit, state, order, delay, or stagger.
- Do not invent assignment, while loops, custom functions, file access, or per-frame source execution. Masks, paths, and node selectors require existing matching geometry.

## Preserve grouping and timing contracts

- Declare scopes with `current`, `subtitle`, or one-based `range(start,count)` over complete graphemes in the original full subtitle. Choose `group`, `grapheme`, `chunk(N)`, `word`, `line`, `paragraph`, or literal `split(...)` using the guide's grouping table. Do not substitute UTF-16 indices, layout wrapping, or language-aware tokenization. Quoted `#` is data; comments begin outside quotes.
- Put scope fields before animation/control blocks. State defaults to Normal independently of panel selection; active/inactive affect painting without creating karaoke timing. Local group transforms act on laid-out geometry; font size/spacing can reflow.
- Fixed duration is one leg; repeat counts complete repetitions. Pingpong reverses the authored curve, including POWER. A flex cycle period includes both legs; emit complete periods and hold the remainder. Include flex weight from empty rests. Read the guide/reference before changing the shared envelope for groups with different segment expansions.
- Each animated property needs progress 0 and 1 and strictly increasing intermediate keys. Shared endpoints must be symbolically continuous. A collapsed noncyclic flex segment must hold a constant value. Delay, stagger, fixed legs, and cycle periods share compression; never allocate stagger independently.
- An empty group's conditional body emits nothing but retains its original `group.index/count` and stagger slot. Within one instance, reject overlapping declarations for the same complete target or overlapping XYZ components. Different instances form an ordered stack; distinct text ranges retain local composition.
- Read current `AnimationPropertyMetadata`/`EffectScriptPropertyMetadata` before choosing limits. Preserve complete XY vectors and independent XYZ curves. Colors are straight linear RGBA, with alpha 0–1; UI HEX/byte input is sRGB. Colors do not support offset/factor.

## Keep effects as owned instances

- Project format 19 stores ordered `ProjectLayer.Effects`: identity, source snapshot, typed parameters, content-clock start/end, enabled state, and optional target context. Adding from a preset creates an independent instance; later library edits do not rewrite it. Parameters currently have no keyframes.
- `ProjectLayer.EffectRowOrder` is the unified stack of complete native targets and instance IDs in visual top-to-bottom order; evaluate bottom to top. Missing order retains the default manual-then-instance behavior. Each native row writes its current sampled absolute value at its stack position. Enabled script instances read the value reached so far: `base` passes it through, `offset(v)` adds, `factor(v)` multiplies, and an absolute value replaces. Keep symbolic affine channels `A(t) × upstream(t) + B(t)`; do not freeze base at entry or bake an approximation into manual tracks. Base-to-absolute interpolation also uses that frame's upstream value. Native and scripted typography/paint share the ordered per-grapheme style flow, including range/state coverage; local geometry hierarchies remain intact.
- `CompileInstance` produces temporary ranges and coefficient channels, cached when compilation inputs are unchanged. Generated identities derive from instance ID, scope declaration, actual loop-value path, and text span. They are not editable persisted ranges/keyframes. Removing/disabling an instance reveals upstream without restoring snapshots or deleting unrelated data.
- Use the same prepared-scene/evaluator pipeline for canvas, library preview, rendering, and export. Source/parameter/time/text/topology changes can rebuild plans; playback samples cached channels. Respect half-open instance/channel intervals and retain cancellation, exact time, and source diagnostics.
- Native projects retain editable instances; ASS uses a temporary sampled projection and reports losses, including instance identity/source/parameters. SRT/subtitle-only mux reports omitted visual instances. Do not claim exact ASS parity or mutate native tracks during export.

## Validate and integrate

Validate the exact source with `EffectScriptParser.Parse`, `EffectScriptParameterBinder.Bind`, and `EffectScriptCompiler.CompileInstance` against representative layers and original subtitle/style/geometry. Parse success or conservative `RequiresSubtitle` scanning is not target availability. Use atomic effect-instance editing operations so a failed target rejects the entire edit and adds no history.

Check long, exact-envelope, short, and tiny positive windows; selected and unselected branches; negative loops; strict/custom parameter choices; rich text; grapheme boundaries; reversed order; empty group bodies; nonlinear animated upstream; multiple instances; save/reload; and the relevant export path. Apply the budgets in the reference before expansion and never truncate output. Report only validation that actually ran.

When changing language behavior, update AST/parser/type checking/binding/compilation, runtime integration as needed, editor highlighting/completion and typed inputs, import/export, both maintained guides, and affected tests together. Built-in templates must expose meaningful duration/amplitude/scale/color parameters, keep stable IDs, and preserve default visuals where possible. Test the actual shipped `.aegifx` source; do not validate a copied approximation.

Keep `ParseLegacy` and the concrete `CompileTarget`/`ComposeTarget` family only for explicit legacy tooling/regressions. Their baked ownership/origin rules do not govern v3 instances. `EffectScript.Scopes/Segments` may be a concrete default observation projection; execute `Statements` through `CompileInstance` for authoritative v3 semantics and exact timing.

## Deliver

Provide complete UTF-8 source or a clickable saved-file path, declared parameters, timing/policy behavior, affected properties, and exact validation evidence. Put requested reusable examples in `docs/en/examples/effects/`. Use the maintained guide for library/preview/share UI steps rather than duplicating that manual here. Real UI checks must close test windows and include parameter drafts, keyboard completion, invalid-source preservation, and read-only built-in source.
