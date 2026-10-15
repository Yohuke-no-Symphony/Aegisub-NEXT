# Compiler checks and source map

Paths are relative to the AegiNext checkout. Read the maintained `docs/en/effect-dsl.md` or `docs/zh-cn/effect-dsl.md` for grammar and property tables; this reference records implementation boundaries and checks.

| Responsibility | Source |
|---|---|
| v3-only source entry; explicit legacy entry | `src/AegiNext.Core/Effects/EffectScriptParser.cs` |
| v3 statements, header, diagnostics | `src/AegiNext.Core/Effects/EffectScriptV3Parser.cs`, `EffectScriptStatement.cs` |
| All-branch symbol/type checking, source budgets | `src/AegiNext.Core/Effects/EffectScriptV3Validator.cs` |
| Typed declarations, choices, parameter binding/round-trip | `src/AegiNext.Core/Effects/EffectScriptParameter*.cs` |
| Expressions, exact rationals, unit evaluation | `src/AegiNext.Core/Effects/EffectScriptExpressionParser.cs`, `EffectScriptExpressionEvaluator.cs`, `EffectScriptRational.cs` |
| Grapheme-safe selection and grouping | `src/AegiNext.Core/Effects/EffectScriptGroupResolver.cs` |
| Dynamic expansion, per-group allocation, deterministic temporary IDs | `src/AegiNext.Core/Effects/EffectScriptInstanceCompiler.cs`, `EffectScriptExpansionBudget.cs` |
| Instance compile API and affine result | `src/AegiNext.Core/Effects/EffectScriptCompiler.Instances.cs`, `CompiledEffectScript.cs`, `CompiledEffectChannel.cs` |
| XYZ component preservation | `src/AegiNext.Core/Effects/EffectScriptRotationTracks.cs` |
| Persisted instance and ordered ownership | `src/AegiNext.Core/Projects/EffectInstance.cs`, `ProjectLayer.cs`, `ProjectDocument.cs` |
| Atomic edits, timing/text lifecycle, migration | `src/AegiNext.Application/ProjectEditor.EffectInstances.cs`, `ProjectEditingOperations.EffectInstances.cs`, `EffectInstanceLifecycle.cs`, `EffectInstanceJsonMigration.cs` |
| Immutable preparation and compilation cache | `src/AegiNext.Core/Editing/PreparedProjectScene.cs`, `EffectCompilationCache.cs` |
| Same-frame upstream and affine sampling | `src/AegiNext.Core/Editing/SceneEvaluator.cs`, `EffectRuntimeEvaluation.cs`, `EffectValueEvaluation.cs` |
| Per-grapheme rich-style evaluation | `src/AegiNext.Core/Editing/SubtitleAnimationEvaluation.cs`, `EffectStyleOperation.cs` |
| Native render integration | `src/AegiNext.Rendering/Projects/ProjectSceneRenderer.SubtitleAnimation.cs` |
| Temporary ASS projection and diagnostics | `src/AegiNext.SubtitleFormats/AssEffectInstanceProjection.cs`, `AssSubtitleFormat.cs` |
| Editor grammar, typed drafts/inputs | `src/AegiNext.Desktop/Controls/Editing/EffectScriptLanguage.cs`, `EffectScriptLanguageContext.cs`, `EffectScriptParameterEditor.cs`, `src/AegiNext.Desktop/Editing/EffectScriptParameterDraftSet.cs` |
| Built-ins and reusable source examples | `src/AegiNext.Core/Effects/Scripts/*.aegifx`, `docs/en/examples/effects/*.aegifx` |

In a table cell with several filenames, unqualified filenames share the first path's directory. Do not use the old V2 compiler/ownership/composer as the implementation map for instance evaluation.

## Parameters and expansion

- Use `EffectScriptParameterBinder.Bind(script, values)` to fill defaults and reject unknown names, wrong types, or strict options nonmembership. Options/defaults are constant; suggestions allow custom values. Preserve labels and the typed payload through project serialization.
- Round-trip via `EffectScriptParameterValue.Parse`/`ToSource`. Equivalent decimal scales and duration units must normalize; repeating rational durations must remain exact. Scientific notation and signed zero need round-trip coverage for vector/color values. Invalid vector/color components must fail even in inactive branches.
- All branches are statically checked; only the selected branch evaluates. Test inactive type errors, inactive division-by-zero, and short-circuit boolean behavior separately. Loop bounds/step are integers; check negative steps, zero step, read-only/no-shadow indices, and nesting.
- `group.index` is the zero-based start order after filtering and `order`; `group.count` includes resolved groups whose bodies later expand empty. Empty first/middle groups must leave later stagger slots intact. Scope layout fields are fixed before group expansion.
- Dynamic scope IDs use declaration name plus actual nested loop values, not an execution ordinal. Combine with instance ID and absolute text span for temporary range identity. Recompile the current source/text instead of remapping a persisted generated family.

See `docs/en/examples/effects/parameterized-letters.aegifx` for typed suggestions, conditions, integer loops, exact `at point / 3`, and group-dependent keys. Compile that file itself when maintaining the example.

## Shared timing envelope

For each resolved group `i`, let `Fi` be expanded fixed time, `Wi` total flex weight including empty rests, and `Qi = max(cyclePeriod × Wi / cycleWeight)` over its cyclic flex segments, or zero. Let `N` include empty-body groups, `T` be instance duration, and `S = max(N − 1, 0) × stagger`:

```text
E = delay + S + max_i(Fi + Qi)
k = min(1, T / E)                 # k = 1 when E = 0
L = T - k × (delay + S)
Ri = L - k × Fi
start_i = instance.Start + k × (delay + i × stagger)
```

`reject` fails if `T < E`; `compress` uses the same `k` for delay, stagger, fixed legs, and cycle periods. Divide `Ri` by that group's own flex weights. Keep rational time through allocation and cycle flooring; never pass through the decimal default observation projection. Forward repeat/cycle endpoints close symbolically; a zero-length noncyclic flex segment must hold. Pingpong reverses the original interpolation, including nonlinear POWER; a cycle period includes the return leg.

## Dynamic composition

Each key describes coefficients, interpolated with the authored curve before combining with the upstream value at that frame:

| Value | Multiplier A | Offset B |
|---|---|---|
| `base` | 1 | 0 |
| `offset(v)` | 1 | v |
| `factor(v)` | v | 0 |
| absolute `v` | 0 | v |

Result is `A(t) × upstream(t) + B(t)`. Test base-to-absolute interpolation against nonlinear hand animation and multiple ordered instances; a finite baked key list generally cannot preserve that composition. Keep X/Y/Z component curves independent, including reversed legs and complete turns. Use half-open windows so adjacent channels do not apply a shared endpoint twice.

Rich-text typography/paint bases resolve per grapheme; do not reject mixed styles or choose the first style. Local generated transforms start neutral and retain parent/sibling geometry composition. Runtime bounds/nonfinite failures need source diagnostics; absolute literals can be checked at compile time. ASS sampling is a lossy export projection, not the native runtime representation.

## Budgets and atomic failure

- Source: 262144 characters, 128 scope declarations, 128 segment declarations, 4096 keys across all branches. Declaration IDs are lowercase ASCII letter followed by lowercase ASCII letters/digits/dot/hyphen, at most 64 characters. Scope declaration names are unique across branches.
- Expressions: parser recursion and completed AST depth are each limited to 64. Check the AST with an explicit stack before recursive type inference/evaluation; flat left-associative arithmetic still builds a deep tree. Test 64/65-term boundaries without running a stack-overflowing old-source fixture.
- Expansion: nesting 16, one loop 4096 iterations, one target 100000 executed statements/iterations. Source declaration counts do not count implicit group clones. Each expanded group remains bounded to 128 segments and 4096 keys.
- Output: at most 256 resolved groups per scope and 256 manual plus temporary text ranges per target; at most 8192 range/state targets. Check each property's aggregate key-entry budget through `AnimationPropertyMetadata` before emission, including huge repeat/cycle counts. Never truncate or convert an unchecked huge count to a machine integer.
- Fixed durations/cycle periods are positive and at most 24 hours; flex weights are positive and at most 1000. Exact rational arithmetic still has representability/complexity limits. Preserve cancellation and reject invalid/overflowed expansions atomically.
- Validate existing subtitle/range/mask/path/node identities and property compatibility. One failure in a batch must leave the document and Undo history unchanged. Instances never take ownership of native tracks/ranges.

## Regression selection

Run affected suites serially from the checkout root, using the established configuration/platform. Coordinate shared build outputs with other agents. Do not install/change dependencies to run these commands; narrow filters further for a local change.

```sh
dotnet test Tests/AegiNext.Core.Tests/AegiNext.Core.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~EffectScript|FullyQualifiedName~EffectInstanceEvaluation|FullyQualifiedName~ReverseAnimationCurve'
dotnet test Tests/AegiNext.Application.Tests/AegiNext.Application.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~EffectInstance|FullyQualifiedName~EffectScript'
dotnet test Tests/AegiNext.Desktop.Tests/AegiNext.Desktop.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~EffectScript|FullyQualifiedName~EffectParameter|FullyQualifiedName~EffectPresetPreview|FullyQualifiedName~TimelineEffect'
dotnet test Tests/AegiNext.Rendering.Tests/AegiNext.Rendering.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~EffectInstance|FullyQualifiedName~GroupedEffect|FullyQualifiedName~SubtitleRangeAnimation|FullyQualifiedName~SubtitleRangePosition'
dotnet test Tests/AegiNext.SubtitleFormats.Tests/AegiNext.SubtitleFormats.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~EffectInstance|FullyQualifiedName~EffectScopeAss'
```

Test long/exact/short/tiny positive windows, different per-group bodies, weighted cycles/remainders, emoji/combining text, CRLF and separator boundaries, mixed rich styles, actual animated upstream, effect order/disable/remove, text edits, parent-range deletion, move/trim/stretch/copy/split, v19 save/reload and old-project empty-instance migration. Preserve original native data through rendering/export. Real UI checks include drafts/completion/caret/input-method behavior and close every test window.

## Explicit legacy regressions

`EffectScriptParser.ParseLegacy` and the concrete compiler/composer family remain for old baked-animation tooling and regression tests. `Tests/AegiNext.Core.Tests/Effects/EffectScriptRegressionParser.cs` is a test-only fixture adapter; it does not relax production source parsing. Keep old generated-origin/family replacement tests in that explicit boundary. New UI application, preview, persistence, and playback must use v3 instances and `CompileInstance`.
