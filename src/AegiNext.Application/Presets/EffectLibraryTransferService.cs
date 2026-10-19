using System.Collections.Immutable;
using AegiNext.Core.Effects;
using AegiNext.Core.Presets;
using AegiNext.Core.Fonts;
using AegiNext.Application.Fonts;

namespace AegiNext.Application.Presets;

/// <summary>冻结效果库交换输入，分析冲突并按用户选择原子合并；不拥有宿主仓库。</summary>
public sealed class EffectLibraryTransferService
{
    private readonly EffectLibraryRepository repository;
    private readonly IFontFamilySource fontSource;
    private readonly IFontFamilyValidator fontValidator;
    private readonly ImmutableArray<FontFamilyBundle> frozenFontFamilies;

    /// <summary>使用应用唯一的共享效果库仓库。</summary>
    public EffectLibraryTransferService(EffectLibraryRepository repository, IFontFamilySource fontSource, IFontFamilyValidator fontValidator,
        ImmutableArray<FontFamilyBundle> frozenFontFamilies = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(fontSource);
        ArgumentNullException.ThrowIfNull(fontValidator);
        this.repository = repository;
        this.fontSource = fontSource;
        this.fontValidator = fontValidator;
        this.frozenFontFamilies = frozenFontFamilies;
    }

    /// <summary>读取多个独立文件和包，生成绑定到当前修订的完整冲突预览，不提交任何导入内容。</summary>
    public async Task<EffectLibraryImportPlan> AnalyzeImportAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var inputs = paths.ToArray();
        await repository.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var captured = repository.Snapshot;
        var entries = new List<EffectLibraryImportEntry>();
        long inputBytes = 0;
        var inputFonts = new Dictionary<string, FontFamilyBundle>(StringComparer.OrdinalIgnoreCase);
        var inputFontData = new Dictionary<string, ImmutableArray<byte>>(StringComparer.Ordinal);
        foreach (var path in inputs)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            cancellationToken.ThrowIfCancellationRequested();
            var sourceName = Path.GetFileName(path);
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".aegistyles":
                    var collection = await SubtitleStylePresetStore.LoadAsync(path, cancellationToken).ConfigureAwait(false);
                    RegisterFamilies(collection.FontFamilies, inputFonts, inputFontData);
                    foreach (var preset in collection.Styles.Presets)
                    {
                        AddInput(entries, StyleEntry(preset, inputFonts.Values, sourceName), ref inputBytes);
                    }
                    break;
                case ".aegieffect":
                    var complete = await EffectScriptPresetStore.ReadPresetAsync(path, cancellationToken).ConfigureAwait(false);
                    AddInput(entries, EffectEntry(complete, true, sourceName), ref inputBytes);
                    break;
                case ".aegifx":
                    var script = await EffectScriptPresetStore.ReadScriptAsync(path, cancellationToken).ConfigureAwait(false);
                    AddInput(entries, new()
                    {
                        Kind = EffectLibraryItemKind.EFFECT,
                        Effect = new(Guid.NewGuid(), script.Script.Id, script.Source),
                        DslId = script.Script.Id,
                        SourceName = sourceName
                    }, ref inputBytes);
                    break;
                case ".aegilibrary":
                    var package = await EffectLibraryPackageStore.LoadAsync(path, cancellationToken).ConfigureAwait(false);
                    RegisterFamilies(package.FontFamilies, inputFonts, inputFontData);
                    foreach (var preset in package.Styles.Presets)
                    {
                        AddInput(entries, StyleEntry(preset, inputFonts.Values, sourceName), ref inputBytes);
                    }
                    foreach (var preset in package.Effects.Presets)
                    {
                        AddInput(entries, EffectEntry(preset, true, sourceName), ref inputBytes);
                    }
                    break;
                default:
                    throw new InvalidDataException("导入文件必须是样式、完整特效、特效脚本或效果库包。");
            }
            PrivateFontFiles.ValidateInputBudget(inputFonts.Values, inputBytes);
        }
        var frozen = entries.ToImmutableArray();
        var rows = frozen.Select((entry, index) => AnalyzeRow(entry, index, captured)).ToArray();
        GroupInputVersions(frozen, rows);
        cancellationToken.ThrowIfCancellationRequested();
        return new(repository, captured.Revision, rows.ToImmutableArray(), frozen);
    }

    /// <summary>按行号冻结用户选择，仅在预览修订仍有效时一次合并两类库；冲突组至多接受一个版本。</summary>
    public Task CommitImportAsync(EffectLibraryImportPlan plan, IReadOnlyDictionary<int, EffectLibraryImportDecision> decisions,
        Action? beforeCommit = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(decisions);
        if (!ReferenceEquals(plan.Owner, repository))
        {
            throw new ArgumentException("导入预览属于另一个效果库。", nameof(plan));
        }
        var frozen = decisions.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (frozen.Any(pair => pair.Key < 0 || pair.Key >= plan.Rows.Length || !Enum.IsDefined(pair.Value)))
        {
            throw new InvalidDataException("导入选择包含未知行或无效决策。");
        }
        var choices = plan.Rows.Select(row => frozen.GetValueOrDefault(row.Index, DefaultDecision(row))).ToArray();
        foreach (var row in plan.Rows)
        {
            var decision = choices[row.Index];
            if (decision == EffectLibraryImportDecision.IMPORT && !row.CanImport ||
                decision == EffectLibraryImportDecision.REPLACE && !row.CanReplace)
            {
                throw new InvalidDataException("该导入行不允许所选操作。");
            }
        }
        if (plan.Rows.Where(row => row.ConflictGroupId.HasValue && choices[row.Index] != EffectLibraryImportDecision.SKIP)
            .GroupBy(row => row.ConflictGroupId).Any(group => group.Count() > 1))
        {
            throw new InvalidDataException("同一冲突组只能选择一个导入版本。");
        }
        return repository.UpdateAsync(plan.ExpectedRevision, current => Merge(current, plan, choices), beforeCommit, cancellationToken);
    }

    /// <summary>导出一个样式及其完整字体族。</summary>
    public Task ExportStyleAsync(SubtitleStylePreset preset, string path, Action? beforeCommit = null,
        CancellationToken cancellationToken = default)
    {
        return ExportStylesAsync(new() { Presets = [preset] }, path, beforeCommit, cancellationToken);
    }

    /// <summary>导出冻结的样式选择，全部字体族收集完成后才写入目标。</summary>
    public async Task ExportStylesAsync(SubtitleStylePresetCollection styles, string path, Action? beforeCommit = null,
        CancellationToken cancellationToken = default)
    {
        RequireExportPath(path);
        var prepared = await CaptureExportFamiliesAsync(styles.Presets, repository.Snapshot.FontFamilies, cancellationToken).ConfigureAwait(false);
        await SubtitleStylePresetStore.SaveAsync(new() { Styles = styles with { Presets = prepared.Styles }, FontFamilies = prepared.Families }, path, beforeCommit, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>导出一个完整个人特效，保留既有库身份、展示名称和源文。</summary>
    public Task ExportEffectAsync(EffectScriptPreset preset, string path, Action? beforeCommit = null,
        CancellationToken cancellationToken = default)
    {
        RequireExportPath(path);
        return EffectScriptPresetStore.WritePresetAsync(preset, path, beforeCommit, cancellationToken);
    }

    /// <summary>导出内置或个人特效的原始 .aegifx 源文。</summary>
    public Task ExportEffectSourceAsync(EffectScriptPreset preset, string path, Action? beforeCommit = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        RequireExportPath(path);
        return EffectScriptPresetStore.WriteScriptAsync(preset.Source, path, beforeCommit, cancellationToken);
    }

    /// <summary>返回与完整独立文件导出一致的目标路径，供宿主声明任务资源和冻结目标。</summary>
    public IReadOnlyList<string> GetIndependentDestinations(IEnumerable<SubtitleStylePreset> styles,
        IEnumerable<EffectScriptPreset> effects, string directory)
    {
        ArgumentNullException.ThrowIfNull(styles);
        ArgumentNullException.ThrowIfNull(effects);
        RequireExportPath(directory);
        var stylePaths = PresetBatchExporter.GetStyleDestinations(styles, directory);
        var effectPaths = PresetBatchExporter.GetEffectDestinations(effects, directory).Select(path => Path.ChangeExtension(path, ".aegieffect"));
        var destinations = stylePaths.Concat(effectPaths).ToArray();
        foreach (var path in destinations)
        {
            RequireExportPath(path);
        }
        return destinations;
    }

    /// <summary>全部个人选择项先暂存，再逐个发布独立完整文件；拒绝覆盖既有目标，失败时清理本次输出。</summary>
    public async Task ExportIndependentAsync(IEnumerable<SubtitleStylePreset> styles, IEnumerable<EffectScriptPreset> effects,
        string directory, Action? beforeCommit = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(styles);
        ArgumentNullException.ThrowIfNull(effects);
        var selectedStyles = styles.ToImmutableArray();
        var selectedEffects = effects.ToImmutableArray();
        cancellationToken.ThrowIfCancellationRequested();
        _ = SubtitleStylePresetMetadataStore.Serialize(new() { Presets = selectedStyles });
        _ = EffectScriptPresetStore.Serialize(new() { Presets = selectedEffects });
        RequireExportPath(directory);
        var destinations = GetIndependentDestinations(selectedStyles, selectedEffects, directory);
        if (destinations.Count == 0)
        {
            return;
        }
        foreach (var path in destinations)
        {
            RequireExportPath(path);
            if (File.Exists(path) || Directory.Exists(path))
            {
                throw new IOException($"导出目标已存在，未覆盖任何文件：{path}");
            }
        }
        var prepared = await CaptureExportFamiliesAsync(selectedStyles, repository.Snapshot.FontFamilies, cancellationToken).ConfigureAwait(false);
        selectedStyles = prepared.Styles;
        var families = prepared.Families;
        var destination = Path.GetFullPath(directory);
        Directory.CreateDirectory(destination);
        var staging = Path.Combine(destination, $".aeginext-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        var published = new List<string>();
        try
        {
            for (var index = 0; index < destinations.Count; index++)
            {
                var stagedPath = Path.Combine(staging, Path.GetFileName(destinations[index]));
                if (index < selectedStyles.Length)
                {
                    var preset = selectedStyles[index];
                    await SubtitleStylePresetStore.SaveAsync(new()
                    {
                        Styles = new() { Presets = [preset] },
                        FontFamilies = families.Where(family => string.Equals(family.FamilyName, preset.Style.FontFamily, StringComparison.OrdinalIgnoreCase)).ToImmutableArray()
                    }, stagedPath, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await EffectScriptPresetStore.WritePresetAsync(selectedEffects[index - selectedStyles.Length], stagedPath, cancellationToken).ConfigureAwait(false);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            beforeCommit?.Invoke();
            var publicationToken = beforeCommit is null ? cancellationToken : CancellationToken.None;
            foreach (var path in destinations)
            {
                publicationToken.ThrowIfCancellationRequested();
                RequireExportPath(path);
                File.Move(Path.Combine(staging, Path.GetFileName(path)), path);
                published.Add(path);
            }
            Directory.Delete(staging);
        }
        catch (Exception failure)
        {
            var errors = CleanupExport(published, staging);
            if (errors.Count != 0)
            {
                throw new AggregateException("独立文件导出失败，部分本次输出未能清理。", [failure, .. errors]);
            }
            throw;
        }
    }

    /// <summary>收集完整字体族后将冻结选择打包。</summary>
    public Task ExportPackageAsync(IEnumerable<SubtitleStylePreset> styles, IEnumerable<EffectScriptPreset> effects,
        string path, Action? beforeCommit = null, CancellationToken cancellationToken = default)
    {
        return ExportPackageCoreAsync(styles, effects, repository.Snapshot.FontFamilies, path, beforeCommit, cancellationToken);
    }

    /// <summary>冻结当前整个个人效果库并导出。</summary>
    public async Task ExportAllPackageAsync(string path, Action? beforeCommit = null, CancellationToken cancellationToken = default)
    {
        await repository.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await ExportAllPackageAsync(repository.Snapshot, path, beforeCommit, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>以显示选择器前冻结的样式、特效和私有字体快照导出。</summary>
    public Task ExportAllPackageAsync(EffectLibrarySnapshot snapshot, string path, Action? beforeCommit = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return ExportPackageCoreAsync(snapshot.Styles.Presets, snapshot.Effects.Presets, snapshot.FontFamilies, path, beforeCommit, cancellationToken);
    }

    private async Task ExportPackageCoreAsync(IEnumerable<SubtitleStylePreset> styles, IEnumerable<EffectScriptPreset> effects,
        ImmutableArray<FontFamilyBundle> frozenFonts, string path, Action? beforeCommit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(styles);
        ArgumentNullException.ThrowIfNull(effects);
        RequireExportPath(path);
        var selected = styles.ToImmutableArray();
        var selectedEffects = effects.ToImmutableArray();
        var prepared = await CaptureExportFamiliesAsync(selected, frozenFonts, cancellationToken).ConfigureAwait(false);
        selected = prepared.Styles;
        var families = prepared.Families;
        await EffectLibraryPackageStore.SaveAsync(new()
        {
            Styles = new() { Presets = selected }, Effects = new() { Presets = selectedEffects }, FontFamilies = families
        }, path, beforeCommit, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(ImmutableArray<SubtitleStylePreset> Styles, ImmutableArray<FontFamilyBundle> Families)> CaptureExportFamiliesAsync(
        IEnumerable<SubtitleStylePreset> styles, ImmutableArray<FontFamilyBundle> availableFonts, CancellationToken cancellationToken)
    {
        var selected = styles.ToImmutableArray();
        var captured = new Dictionary<string, FontFamilyBundle>(StringComparer.OrdinalIgnoreCase);
        var fonts = frozenFontFamilies.IsDefault ? availableFonts : frozenFontFamilies;
        foreach (var name in selected.Select(preset => preset.Style.FontFamily).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var family = fonts.FirstOrDefault(family => string.Equals(family.FamilyName, name, StringComparison.OrdinalIgnoreCase)) ??
                await fontSource.CaptureAsync(name, cancellationToken: cancellationToken).ConfigureAwait(false);
            fontValidator.Validate(family);
            captured.Add(name, family);
        }
        var families = captured.Values.DistinctBy(family => family.FamilyName, StringComparer.OrdinalIgnoreCase).ToImmutableArray();
        FontFamilyBundleValidator.Validate(families);
        return (selected.Select(preset => preset with { Style = preset.Style with { FontFamily = captured[preset.Style.FontFamily].FamilyName } }).ToImmutableArray(), families);
    }

    private void RegisterFamilies(ImmutableArray<FontFamilyBundle> families, Dictionary<string, FontFamilyBundle> known,
        Dictionary<string, ImmutableArray<byte>> files)
    {
        foreach (var family in families)
        {
            if (known.TryGetValue(family.FamilyName, out var previous))
            {
                if (!PrivateFontFiles.SameFamily(previous, family))
                {
                    throw new InvalidDataException("导入输入包含同一字体族的不同版本。");
                }
                continue;
            }
            fontValidator.Validate(family);
            known.Add(family.FamilyName, PrivateFontFiles.ShareFontData(family, files));
        }
    }

    private static EffectLibraryImportRow AnalyzeRow(EffectLibraryImportEntry entry, int index, EffectLibrarySnapshot snapshot)
    {
        var row = new EffectLibraryImportRow
        {
            Index = index,
            Kind = entry.Kind,
            Name = entry.Name,
            SourceName = entry.SourceName,
            DslId = entry.DslId
        };
        if (entry.Kind == EffectLibraryItemKind.EFFECT && BuiltinEffectScripts.Templates.FirstOrDefault(template =>
                template.Script.Id == entry.DslId) is { } builtin)
        {
            var same = string.Equals(builtin.Source, entry.Effect!.Source, StringComparison.Ordinal);
            return row with
            {
                Status = same ? EffectLibraryImportStatus.IDENTICAL : EffectLibraryImportStatus.CONFLICT,
                ConflictReason = same ? EffectLibraryImportConflictReason.NONE : EffectLibraryImportConflictReason.BUILTIN_READ_ONLY
            };
        }
        var targets = new Dictionary<Guid, string>();
        if (entry.Kind == EffectLibraryItemKind.STYLE)
        {
            foreach (var preset in snapshot.Styles.Presets.Where(preset => preset.Id == entry.ExternalId ||
                         string.Equals(preset.Name, entry.Name, StringComparison.OrdinalIgnoreCase)))
            {
                targets.Add(preset.Id, preset.Name);
            }
        }
        else
        {
            foreach (var preset in snapshot.Effects.Presets)
            {
                if (preset.Id == entry.ExternalId || string.Equals(preset.Name.Trim(), entry.Name.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(EffectScriptParser.Parse(preset.Source).Id, entry.DslId, StringComparison.Ordinal))
                {
                    targets.Add(preset.Id, preset.Name);
                }
            }
        }
        if (targets.Count > 1)
        {
            return row with
            {
                Status = EffectLibraryImportStatus.CONFLICT,
                ConflictReason = EffectLibraryImportConflictReason.MULTIPLE_TARGETS,
                ConflictingNames = targets.Values.ToImmutableArray()
            };
        }
        if (targets.Count == 0)
        {
            return row with { Status = EffectLibraryImportStatus.NEW, CanImport = true };
        }
        var targetId = targets.Keys.Single();
        var sameContent = entry.Kind == EffectLibraryItemKind.STYLE
            ? SameStyle(entry.Style!, snapshot.Styles.Presets.Single(preset => preset.Id == targetId)) &&
                SameFonts(entry.FontFamilies, snapshot.FontFamilies)
            : SameEffect(entry.Effect!, entry.HasEffectName, snapshot.Effects.Presets.Single(preset => preset.Id == targetId), true);
        return row with
        {
            TargetId = targetId,
            PreviousDslId = entry.Kind == EffectLibraryItemKind.EFFECT
                ? EffectScriptParser.Parse(snapshot.Effects.Presets.Single(preset => preset.Id == targetId).Source).Id : null,
            Status = sameContent ? EffectLibraryImportStatus.IDENTICAL : EffectLibraryImportStatus.CONFLICT,
            CanReplace = !sameContent,
            ConflictReason = sameContent ? EffectLibraryImportConflictReason.NONE : EffectLibraryImportConflictReason.DIFFERENT_CONTENT,
            ConflictingNames = [targets[targetId]]
        };
    }

    private static void GroupInputVersions(ImmutableArray<EffectLibraryImportEntry> entries, EffectLibraryImportRow[] rows)
    {
        var parents = Enumerable.Range(0, rows.Length).ToArray();
        var keys = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < rows.Length; index++)
        {
            if (rows[index].ConflictReason is EffectLibraryImportConflictReason.MULTIPLE_TARGETS or EffectLibraryImportConflictReason.BUILTIN_READ_ONLY ||
                rows[index].TargetId is null && rows[index].Status == EffectLibraryImportStatus.IDENTICAL)
            {
                continue;
            }
            foreach (var key in InputKeys(entries[index], rows[index]))
            {
                if (keys.TryGetValue(key, out var other))
                {
                    var first = FindGroup(parents, index);
                    var second = FindGroup(parents, other);
                    parents[Math.Max(first, second)] = Math.Min(first, second);
                }
                else
                {
                    keys.Add(key, index);
                }
            }
        }
        foreach (var group in Enumerable.Range(0, rows.Length).GroupBy(index => FindGroup(parents, index)))
        {
            var indices = group.OrderByDescending(index => entries[index].HasEffectName).ThenBy(index => index).ToArray();
            if (indices.Length < 2)
            {
                continue;
            }
            var representatives = new List<int>();
            foreach (var index in indices)
            {
                if (representatives.Any(other => rows[other].TargetId == rows[index].TargetId && SameInput(entries[other], entries[index])))
                {
                    rows[index] = rows[index] with { Status = EffectLibraryImportStatus.IDENTICAL, CanImport = false, CanReplace = false,
                        ConflictReason = EffectLibraryImportConflictReason.NONE };
                }
                else
                {
                    representatives.Add(index);
                }
            }
            if (representatives.Count < 2)
            {
                continue;
            }
            var groupId = indices.Min();
            var names = representatives.Select(index => rows[index].Name).ToImmutableArray();
            foreach (var index in indices)
            {
                var row = rows[index];
                rows[index] = row with
                {
                    ConflictGroupId = groupId,
                    Status = row.Status == EffectLibraryImportStatus.NEW ? EffectLibraryImportStatus.CONFLICT : row.Status,
                    ConflictReason = row.Status == EffectLibraryImportStatus.IDENTICAL ? row.ConflictReason : EffectLibraryImportConflictReason.MULTIPLE_INPUT_VERSIONS,
                    ConflictingNames = names
                };
            }
        }
    }

    private static IEnumerable<string> InputKeys(EffectLibraryImportEntry entry, EffectLibraryImportRow row)
    {
        var prefix = entry.Kind == EffectLibraryItemKind.STYLE ? "style:" : "effect:";
        if (entry.ExternalId is { } id)
        {
            yield return prefix + "id:" + id.ToString("N");
        }
        yield return prefix + "name:" + entry.Name.Trim().ToUpperInvariant();
        if (entry.DslId is { } dslId)
        {
            yield return prefix + "dsl:" + dslId;
        }
        if (row.TargetId is { } target)
        {
            yield return prefix + "target:" + target.ToString("N");
        }
    }

    private static int FindGroup(int[] parents, int index)
    {
        while (parents[index] != index)
        {
            parents[index] = parents[parents[index]];
            index = parents[index];
        }
        return index;
    }

    private static EffectLibrarySnapshot Merge(EffectLibrarySnapshot current, EffectLibraryImportPlan plan,
        EffectLibraryImportDecision[] choices)
    {
        var styles = current.Styles.Presets;
        var effects = current.Effects.Presets;
        var fonts = current.FontFamilies;
        foreach (var row in plan.Rows)
        {
            var decision = choices[row.Index];
            if (decision == EffectLibraryImportDecision.SKIP)
            {
                continue;
            }
            var entry = plan.Entries[row.Index];
            if (entry.Kind == EffectLibraryItemKind.STYLE)
            {
                var preset = entry.Style!;
                fonts = PrivateFontFiles.Merge(fonts, entry.FontFamilies);
                if (decision == EffectLibraryImportDecision.IMPORT)
                {
                    styles = styles.Add(preset);
                }
                else
                {
                    var target = styles.Single(item => item.Id == row.TargetId);
                    styles = styles.SetItem(styles.IndexOf(target), preset with { Id = target.Id });
                }
            }
            else
            {
                var preset = entry.Effect!;
                if (decision == EffectLibraryImportDecision.IMPORT)
                {
                    effects = effects.Add(preset);
                }
                else
                {
                    var target = effects.Single(item => item.Id == row.TargetId);
                    effects = effects.SetItem(effects.IndexOf(target), preset with { Id = target.Id, Name = entry.HasEffectName ? preset.Name : target.Name });
                }
            }
        }
        return current with { Styles = current.Styles with { Presets = styles }, Effects = current.Effects with { Presets = effects }, FontFamilies = fonts };
    }

    private static EffectLibraryImportDecision DefaultDecision(EffectLibraryImportRow row)
    {
        return row.Status == EffectLibraryImportStatus.NEW && row.ConflictGroupId is null
            ? EffectLibraryImportDecision.IMPORT : EffectLibraryImportDecision.SKIP;
    }

    private static bool SameInput(EffectLibraryImportEntry first, EffectLibraryImportEntry second)
    {
        return first.Kind == second.Kind && (first.Kind == EffectLibraryItemKind.STYLE
            ? SameStyle(first.Style!, second.Style!) : SameEffect(first.Effect!, first.HasEffectName, second.Effect!, second.HasEffectName));
    }

    private static bool SameStyle(SubtitleStylePreset first, SubtitleStylePreset second)
    {
        var left = SubtitleStylePresetMetadataStore.Serialize(new() { Presets = [first with { Id = second.Id }] });
        var right = SubtitleStylePresetMetadataStore.Serialize(new() { Presets = [second] });
        return left.AsSpan().SequenceEqual(right);
    }

    private static bool SameFonts(ImmutableArray<FontFamilyBundle> incoming, ImmutableArray<FontFamilyBundle> installed)
    {
        return incoming.All(family => installed.Any(other => PrivateFontFiles.SameFamily(family, other)));
    }

    private static bool SameEffect(EffectScriptPreset first, bool firstHasName, EffectScriptPreset second, bool secondHasName)
    {
        return string.Equals(first.Source, second.Source, StringComparison.Ordinal) &&
            (!firstHasName || !secondHasName || string.Equals(first.Name, second.Name, StringComparison.Ordinal));
    }

    private static EffectLibraryImportEntry StyleEntry(SubtitleStylePreset preset, IEnumerable<FontFamilyBundle> families, string sourceName)
    {
        return new() { Kind = EffectLibraryItemKind.STYLE, Style = preset, ExternalId = preset.Id, SourceName = sourceName,
            FontFamilies = families.Where(family => string.Equals(family.FamilyName, preset.Style.FontFamily, StringComparison.OrdinalIgnoreCase)).ToImmutableArray() };
    }

    private static EffectLibraryImportEntry EffectEntry(EffectScriptPreset preset, bool complete, string sourceName)
    {
        return new() { Kind = EffectLibraryItemKind.EFFECT, Effect = preset, HasEffectName = complete,
            ExternalId = complete ? preset.Id : null, DslId = EffectScriptParser.Parse(preset.Source).Id, SourceName = sourceName };
    }

    private static void AddInput(List<EffectLibraryImportEntry> entries, EffectLibraryImportEntry entry, ref long bytes)
    {
        bytes = checked(bytes + (entry.Kind == EffectLibraryItemKind.STYLE
            ? SubtitleStylePresetMetadataStore.Serialize(new() { Presets = [entry.Style!] }).Length
            : entry.HasEffectName ? EffectScriptPresetStore.SerializePreset(entry.Effect!).Length
            : System.Text.Encoding.UTF8.GetByteCount(entry.Effect!.Source)));
        if (bytes > EffectLibraryPackageStore.MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("本次效果库导入内容超过 1 GiB。");
        }
        entries.Add(entry);
    }

    private void RequireExportPath(string path)
    {
        if (repository.IsManagedPath(path))
        {
            throw new InvalidDataException("导出位置不能覆盖应用管理的效果库内容。");
        }
    }

    private static List<Exception> CleanupExport(List<string> published, string staging)
    {
        var errors = new List<Exception>();
        foreach (var path in published)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                errors.Add(error);
            }
        }
        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            errors.Add(error);
        }
        return errors;
    }
}
