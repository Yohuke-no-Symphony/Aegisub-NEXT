using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace AegiNext.Core.Projects;

/// <summary>工程边界校验；无效快照在进入编辑历史、渲染或持久化前整体拒绝。</summary>
public static partial class ProjectValidator
{
    /// <summary>验证版本、资源引用、轨道片段、关键帧与文本区间，不修改输入。</summary>
    public static void Validate(ProjectDocument document, bool enforceAnimationRange = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        NotNull(document.TimelineViewState, "时间轴视图状态不能为 null。");
        document.TimelineViewState.Validate();
        Require(document.Version == ProjectDocument.CURRENT_VERSION, "不支持的项目版本。");
        Require(document.Id != Guid.Empty && document.Name is { Length: <= 1024 }, "项目标识或名称无效。");
        ValidateText(document.Name);
        Require(document.Width is > 0 and <= 32768 && document.Height is > 0 and <= 32768 &&
            (long)document.Width * document.Height <= 33177600, "项目画布超过像素预算。");
        Require(document.FrameRate is { Numerator: > 0 } &&
            (double)document.FrameRate.Numerator / document.FrameRate.Denominator <= 1000, "项目帧率无效。");
        Number(document.ReferenceWhiteNits, 0.001, 10000, "参考白");
        Require(!document.Assets.IsDefault && document.Assets.Length <= 10000 &&
            !document.Tracks.IsDefault && document.Tracks.Length <= 10000 &&
            !document.Subtitles.IsDefault && document.Subtitles.Length <= 100000 &&
            !document.Layers.IsDefault && document.Layers.Length <= 10000 && !document.Presets.IsDefault, "项目集合无效或过大。");
        var assets = new Dictionary<Guid, ProjectAsset>();
        foreach (var asset in document.Assets)
        {
            NotNull(asset, "数据项不能为 null。");
            Require(asset.Id != Guid.Empty && assets.TryAdd(asset.Id, asset), "资源标识为空或重复。");
            Require(Enum.IsDefined(asset.Kind), "未知资源类型。");
            Require(Enum.IsDefined(asset.Storage) && (asset.Kind != ProjectAssetKind.FONT || asset.Storage == ProjectAssetStorage.MANAGED),
                "资源存储方式无效。");
            if (asset.ExternalPath is { } external)
            {
                Require((asset.Kind == ProjectAssetKind.MEDIA || asset.Kind == ProjectAssetKind.IMAGE && asset.Storage == ProjectAssetStorage.LINKED) &&
                    asset.RelativePath == string.Empty && ProjectAssetLocation.IsAbsoluteReference(external), "仅媒体和链接图片允许绝对外部引用。");
            }
            else
            {
                ValidateRelativePath(asset.RelativePath);
            }
            Require(asset.Sha256 is null || asset.Sha256.Length == 64 && asset.Sha256.All(Uri.IsHexDigit), "资源哈希无效。");
        }

        ValidateAssetLibrary(document.AssetLibrary, assets);

        if (document.Media is { } media)
        {
            Asset(assets, media.AssetId, ProjectAssetKind.MEDIA);
            Require(media.VideoStreamIndex >= 0 && media.AudioStreamIndex is null or >= 0, "媒体流索引无效。");
        }

        var trackIds = new HashSet<Guid>();
        foreach (var track in document.Tracks)
        {
            NotNull(track, "轨道不能为 null。");
            Require(track.Id != Guid.Empty && trackIds.Add(track.Id) && !string.IsNullOrWhiteSpace(track.Name) &&
                track.Name.Length <= 128 && !track.Name.Any(char.IsControl), "轨道标识或名称无效。");
            ValidateText(track.Name);
            Require(track.DefaultStyle is null
                ? track.StylePresetId is null && track.StylePresetName is null
                : track.StylePresetId is { } presetId && presetId != Guid.Empty &&
                    track.StylePresetName is { Length: > 0 and <= 1024 }, "轨道样式预设来源不完整。");
            if (track.DefaultStyle is { } defaultStyle)
            {
                Style(defaultStyle, assets);
                ValidateText(track.StylePresetName!);
            }
        }

        SubtitleColorTagValidator.Validate(document.ColorTags);
        var colorTagIds = document.ColorTags.Select(tag => tag.Id).ToHashSet();
        var subtitles = new Dictionary<Guid, SubtitleLine>();
        var rangeIds = new HashSet<Guid>();
        foreach (var line in document.Subtitles)
        {
            NotNull(line, "数据项不能为 null。");
            Require(line.Id != Guid.Empty && subtitles.TryAdd(line.Id, line), "字幕标识为空或重复。");
            Require(line.Start < line.End && line.Text is not null && !line.Karaoke.IsDefault &&
                !line.InactiveKaraoke.IsDefault && !line.KaraokeStyleSpans.IsDefault &&
                !line.InlineSpans.IsDefault && !line.AnimationRanges.IsDefault && line.AnimationRanges.Length <= 256, "字幕区间或文本无效。");
            ValidateText(line.Text);
            ValidateSubtitleStyleName(line.StyleName);
            Require(line.StylePresetId is null || line.StylePresetId != Guid.Empty, "字幕样式预设标识无效。");
            Require(line.ColorTagId is null || colorTagIds.Contains(line.ColorTagId.Value), "字幕引用不存在的颜色标记。");
            Style(line.Style, assets);
            var boundaries = line.InlineSpans.IsEmpty && line.Karaoke.IsEmpty && line.InactiveKaraoke.IsEmpty &&
                line.KaraokeStyleSpans.IsEmpty && line.AnimationRanges.IsEmpty
                ? null : new SubtitleTextBoundaries(line.Text);
            var previousEnd = 0;
            foreach (var span in line.InlineSpans)
            {
                NotNull(span, "局部样式不能为 null。");
                Require(span.Utf16Start >= previousEnd && span.Utf16Length > 0 &&
                    (long)span.Utf16Start + span.Utf16Length <= line.Text.Length, "局部样式文本区间重叠或越界。");
                var end = checked(span.Utf16Start + span.Utf16Length);
                Require(boundaries!.Contains(span.Utf16Start) && boundaries.Contains(end), "局部样式不能拆开字素。");
                var inlineStyle = span.Style;
                NotNull(inlineStyle, "局部样式不能为 null。");
                Require(inlineStyle.HasOverrides && !(inlineStyle.ClearFontAsset && inlineStyle.FontAssetId.HasValue) &&
                    !(inlineStyle.ClearFontVariant && inlineStyle.FontVariant is not null),
                    "局部样式为空或字体覆盖冲突。");
                Style(inlineStyle.ApplyTo(line.Style), assets);
                previousEnd = end;
            }
            ValidateSubtitleKaraokeCore(line, boundaries);
            foreach (var range in line.AnimationRanges)
            {
                NotNull(range, "文字动画范围不能为 null。");
                Require(range.Id != Guid.Empty && rangeIds.Add(range.Id) && Enum.IsDefined(range.Pivot), "文字动画范围标识或轴心无效。");
                Require(range.Utf16Start >= 0 && range.Utf16Length > 0 &&
                    (long)range.Utf16Start + range.Utf16Length <= line.Text.Length, "文字动画范围越界或为空。");
                Require(boundaries!.Contains(range.Utf16Start) && boundaries.Contains(range.Utf16Start + range.Utf16Length),
                    "文字动画范围不能拆开字素。");
                Number(range.Scale.X, -10000, 10000, "范围 Scale X");
                Number(range.Scale.Y, -10000, 10000, "范围 Scale Y");
                Number(range.Rotation, -1e9, 1e9, "范围 Rotation");
                Point(range.Offset);
            }
            ValidateGeneratedRangeOrigins(line.AnimationRanges);
        }

        var ids = new HashSet<Guid>();
        var referenced = new HashSet<Guid>();
        foreach (var layer in document.Layers)
        {
            Layer(layer, assets, subtitles, ids, referenced, enforceAnimationRange);
            Require(trackIds.Contains(layer.TrackId), "片段引用不存在的轨道。");
        }

        foreach (var track in document.Layers.GroupBy(layer => layer.TrackId))
        {
            ProjectLayer? previous = null;
            foreach (var layer in track.OrderBy(layer => layer.Start))
            {
                Require(previous is null || previous.End <= layer.Start, "同一轨道的片段不能重叠。");
                previous = layer;
            }
        }

        Require(referenced.Count == subtitles.Count, "每个字幕必须由唯一字幕层引用。");
        var presetIds = new HashSet<Guid>();
        Require(document.Presets.Length <= 10000, "预设数量过大。");
        foreach (var preset in document.Presets)
        {
            NotNull(preset, "数据项不能为 null。");
            Require(preset.Id != Guid.Empty && presetIds.Add(preset.Id) &&
                preset.Name is { Length: > 0 and <= 1024 } && Enum.IsDefined(preset.Blend), "预设无效。");
            ValidateText(preset.Name);
            Tracks(preset.Tracks, allowLegacyColors: true);
            Motion(preset.MotionPath);
        }
    }

    /// <summary>Validates a stable subtitle style name as nonempty Unicode without control characters.</summary>
    public static void ValidateSubtitleStyleName(string styleName)
    {
        Require(!string.IsNullOrWhiteSpace(styleName) && styleName.Length <= 1024 && !styleName.Any(char.IsControl),
            "Subtitle style names must be nonempty, at most 1024 characters, and contain no control characters.");
        ValidateText(styleName);
    }

    /// <summary>验证启用及禁用高亮的完整字素范围、共享标识、视觉覆盖和精确时钟，不裁剪旧时间。</summary>
    public static void ValidateSubtitleKaraoke(SubtitleLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        Require(line.Text is not null, "字幕文字不能为 null。");
        ValidateText(line.Text);
        var boundaries = line.Karaoke.IsDefaultOrEmpty && line.InactiveKaraoke.IsDefaultOrEmpty &&
            line.KaraokeStyleSpans.IsDefaultOrEmpty
            ? null : new SubtitleTextBoundaries(line.Text);
        ValidateSubtitleKaraokeCore(line, boundaries);
    }

    /// <summary>使用绑定到同一文字实例的字素索引验证高亮，使调用方可以复用已构建的边界。</summary>
    public static void ValidateSubtitleKaraoke(SubtitleLine line, SubtitleTextBoundaries boundaries)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(boundaries);
        Require(line.Text is not null, "字幕文字不能为 null。");
        if (!ReferenceEquals(line.Text, boundaries.Text))
        {
            throw new ArgumentException("字素索引必须绑定到字幕的同一文字实例。", nameof(boundaries));
        }
        ValidateText(line.Text);
        ValidateSubtitleKaraokeCore(line, boundaries);
    }

    private static void ValidateSubtitleKaraokeCore(SubtitleLine line, SubtitleTextBoundaries? boundaries)
    {
        Require(!line.Karaoke.IsDefault && !line.InactiveKaraoke.IsDefault && !line.KaraokeStyleSpans.IsDefault,
            "卡拉 OK 数组无效。");
        var previousStyleEnd = 0;
        foreach (var span in line.KaraokeStyleSpans)
        {
            NotNull(span, "数据项不能为 null。");
            Require(span.Utf16Start >= previousStyleEnd && span.Utf16Length > 0 &&
                (long)span.Utf16Start + span.Utf16Length <= line.Text.Length, "高亮样式文本区间重叠或越界。");
            var end = checked(span.Utf16Start + span.Utf16Length);
            Require(boundaries!.Contains(span.Utf16Start) && boundaries.Contains(end), "高亮样式不能拆开字素。");
            Require(span.ActiveStyle is { HasOverrides: true } || span.InactiveStyle is { HasOverrides: true },
                "高亮样式范围必须包含视觉覆盖。");
            if (span.ActiveStyle is { } activeStyle)
            {
                ValidateSubtitleStyle(activeStyle.ApplyTo(line.Style));
            }
            if (span.InactiveStyle is { } inactiveStyle)
            {
                ValidateSubtitleStyle(inactiveStyle.ApplyTo(line.Style));
            }
            previousStyleEnd = end;
        }
        if (line.KaraokeStyle is { } karaokeStyle)
        {
            Require(karaokeStyle.PresetId != Guid.Empty && karaokeStyle.PresetName is { Length: > 0 and <= 1024 },
                "逐字高亮样式预设来源无效。");
            ValidateText(karaokeStyle.PresetName);
            Number(karaokeStyle.StrokeWidth, 0, 4096, "高亮描边");
            Number(karaokeStyle.FillBlur, 0, 512, "高亮填充模糊");
            Number(karaokeStyle.StrokeBlur, 0, 512, "高亮描边模糊");
            Number(karaokeStyle.ShadowBlur, 0, 512, "高亮阴影模糊");
            Point(karaokeStyle.ShadowOffset);
            Color(karaokeStyle.Fill);
            Color(karaokeStyle.Stroke);
            Color(karaokeStyle.ShadowColor);
        }
        var segmentIds = new HashSet<Guid>();
        ValidateKaraokeSegments(line.Karaoke, line, boundaries, segmentIds);
        ValidateKaraokeSegments(line.InactiveKaraoke, line, boundaries, segmentIds);
        var activeIndex = 0;
        var inactiveIndex = 0;
        while (activeIndex < line.Karaoke.Length && inactiveIndex < line.InactiveKaraoke.Length)
        {
            var active = line.Karaoke[activeIndex];
            var inactive = line.InactiveKaraoke[inactiveIndex];
            if (active.Utf16Start + active.Utf16Length <= inactive.Utf16Start)
            {
                activeIndex++;
            }
            else if (inactive.Utf16Start + inactive.Utf16Length <= active.Utf16Start)
            {
                inactiveIndex++;
            }
            else
            {
                throw new InvalidDataException("启用和禁用高亮的文字区间不能重叠。");
            }
        }
    }

    private static void ValidateKaraokeSegments(ImmutableArray<KaraokeSegment> segments, SubtitleLine line,
        SubtitleTextBoundaries? boundaries, HashSet<Guid> segmentIds)
    {
        var previousEnd = 0;
        foreach (var segment in segments)
        {
            NotNull(segment, "数据项不能为 null。");
            Require(segment.Id != Guid.Empty && segmentIds.Add(segment.Id) && Enum.IsDefined(segment.HighlightKind),
                "卡拉 OK 标识为空、重复或高亮类型无效。");
            Require(segment.Utf16Start >= previousEnd && segment.Utf16Length > 0 &&
                (long)segment.Utf16Start + segment.Utf16Length <= line.Text.Length, "卡拉 OK 文本区间重叠或越界。");
            var end = checked(segment.Utf16Start + segment.Utf16Length);
            Require(boundaries!.Contains(segment.Utf16Start) && boundaries.Contains(end), "卡拉 OK 不能拆开字素。");
            Require(segment.Start >= Timing.MediaTime.Zero && segment.Start < segment.End, "卡拉 OK 时间越界。");
            Color(segment.HighlightColor);
            previousEnd = end;
        }
    }

    /// <summary>验证跨平台的工程内相对资源路径。</summary>
    public static void ValidateRelativePath(string path)
    {
        Require(!string.IsNullOrWhiteSpace(path) && path.Length <= 4096 && !path.Contains('\\') && !path.Contains(':') &&
            !path.Any(char.IsControl) && !System.IO.Path.IsPathRooted(path) &&
            path.Split('/').All(segment => segment is not "" and not "." and not ".."), "资源必须使用项目内的规范相对路径。");
        ValidateText(path);
    }

    /// <summary>拒绝不完整 UTF-16 和空字符，避免 JSON 或文本编码时悄悄替换内容。</summary>
    public static void ValidateText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var remaining = text.AsSpan();
        Require(remaining.IndexOf('\0') < 0, "文本包含无效 Unicode 或空字符。");
        while (!remaining.IsEmpty)
        {
            var index = remaining.IndexOfAnyInRange('\uD800', '\uDFFF');
            if (index < 0)
            {
                return;
            }
            Require(index + 1 < remaining.Length && char.IsHighSurrogate(remaining[index]) &&
                char.IsLowSurrogate(remaining[index + 1]), "文本包含无效 Unicode 或空字符。");
            remaining = remaining[(index + 2)..];
        }
    }

    private static void Layer(ProjectLayer? layer, Dictionary<Guid, ProjectAsset> assets,
        Dictionary<Guid, SubtitleLine> subtitles, HashSet<Guid> ids, HashSet<Guid> referenced, bool enforceAnimationRange)
    {
        Require(layer is not null && layer.Id != Guid.Empty && ids.Add(layer.Id) && ids.Count <= 10000,
            "片段过多或存在重复标识。");
        Require(layer.Start < layer.End && layer.Name is { Length: <= 1024 } &&
            Enum.IsDefined(layer.Kind) && Enum.IsDefined(layer.Blend), "片段数据无效。");
        ValidateText(layer.Name);
        Require(layer.Transform is not null, "缺少图层变换。");
        var transform = layer.Transform;
        Number(transform.X, -1e9, 1e9, "X");
        Number(transform.Y, -1e9, 1e9, "Y");
        Number(transform.AnchorX, -1e9, 1e9, "AnchorX");
        Number(transform.AnchorY, -1e9, 1e9, "AnchorY");
        Number(transform.ScaleX, -10000, 10000, "ScaleX");
        Number(transform.ScaleY, -10000, 10000, "ScaleY");
        Number(transform.Rotation, -1e9, 1e9, "Rotation");
        Number(layer.Opacity, 0, 1, "Opacity");
        Number(layer.StrokeWidth, 0, 4096, "StrokeWidth");
        Number(layer.Blur, 0, 512, "Blur");
        Color(layer.Fill);
        Color(layer.Stroke);
        if (layer.Mask is { } mask)
        {
            Require(layer.Kind == LayerKind.SUBTITLE && layer.SubtitleId is { } maskSubtitleId &&
                subtitles.ContainsKey(maskSubtitleId), "只有引用有效字幕的字幕片段可以持有蒙版。");
            Mask(mask);
        }

        var subtitle = layer.SubtitleId is { } animationSubtitleId && subtitles.TryGetValue(animationSubtitleId, out var animationLine)
            ? animationLine : null;
        Tracks(layer.Tracks, mask: layer.Mask, allowNegativeKeyTimes: true, subtitle: subtitle);
        Require(layer.Kind == LayerKind.SUBTITLE || layer.Tracks.All(track => !AnimationPropertyMetadata.IsSubtitleOnlyProperty(track.Property)),
            "字幕排版和分通道模糊动画只能应用于字幕片段。");
        if (enforceAnimationRange)
        {
            var (minimumKeyTime, maximumKeyTime) = Editing.LayerAnimationTiming.GetRange(layer);
            Require(layer.Tracks.All(track => track.Keyframes.All(frame => frame.Time >= minimumKeyTime && frame.Time <= maximumKeyTime)),
                "关键帧必须位于图层片段时间内。");
        }
        Motion(layer.MotionPath);
        Require((layer.Kind == LayerKind.SUBTITLE) == layer.SubtitleId.HasValue &&
            (layer.Kind == LayerKind.SHAPE) == (layer.Shape is not null) &&
            (layer.Kind == LayerKind.IMAGE) == (layer.Image is not null), "图层载荷与类型不匹配。");
        if (layer.SubtitleId is { } subtitleId)
        {
            Require(subtitles.TryGetValue(subtitleId, out var line) && referenced.Add(subtitleId), "字幕层引用不存在或重复。");
            Require(layer.Start == line.Start && layer.End == line.End, "字幕层与字幕行的时间必须一致。");
        }

        if (layer.Shape is { } shape)
        {
            ValidateLayerShape(shape);
        }

        if (layer.Image is { } image)
        {
            Asset(assets, image.AssetId, ProjectAssetKind.IMAGE);
            Number(image.Width, 0.001, 1e6, "图片宽度");
            Number(image.Height, 0.001, 1e6, "图片高度");
            Require(Enum.IsDefined(image.Fit), "未知图片适配方式。");
            ValidateImageCrop(image.Crop);
            if (image.Playback is { } playback)
            {
                Require(Enum.IsDefined(playback.Mode) && double.IsFinite(playback.Speed) && playback.Speed > 0,
                    "图片播放模式或速度无效。");
            }
        }

    }

    /// <summary>验证字幕样式本身的排版、颜色及数值，不解析工程字体引用。</summary>
    public static void ValidateSubtitleStyle([NotNull] SubtitleStyle? style)
    {
        Require(style is not null && style.FontFamily is { Length: > 0 and <= 512 } && Enum.IsDefined(style.Alignment), "字幕样式无效。");
        Require(style.TextAlign is null || Enum.IsDefined(style.TextAlign.Value), "字幕文字对齐无效。");
        Require(Enum.IsDefined(style.WrapMode), "字幕换行模式无效。");
        ValidateText(style.FontFamily);
        if (style.FontVariant is { } variant)
        {
            Require(!string.IsNullOrWhiteSpace(variant.Name) && variant.Name.Length <= 512 &&
                !variant.Name.Any(char.IsControl), "字体变体名称无效。");
            ValidateText(variant.Name);
            if (variant.PostScriptName is { } postScriptName)
            {
                Require(!string.IsNullOrWhiteSpace(postScriptName) && postScriptName.Length <= 512 &&
                    !postScriptName.Any(char.IsControl), "字体变体 PostScript 名称无效。");
                ValidateText(postScriptName);
            }
            Require(variant.Weight is >= 1 and <= 1000 && variant.Width is >= 1 and <= 9, "字体变体样式特征无效。");
        }
        Number(style.FontSize, 0.01, 4096, "字号");
        Number(style.LetterSpacing, -4096, 4096, "字距");
        Number(style.FillBlur, 0, 512, "填充模糊");
        Number(style.StrokeBlur, 0, 512, "描边模糊");
        Number(style.StrokeWidth, 0, 4096, "描边");
        Number(style.Margins.Left, 0, 32768, "字幕左边距");
        Number(style.Margins.Right, 0, 32768, "字幕右边距");
        Number(style.Margins.Vertical, 0, 32768, "字幕垂直边距");
        Number(style.LineHeight, 0.1, 10, "行高");
        Number(style.ShadowBlur, 0, 512, "阴影模糊");
        if (style.Position is { } position)
        {
            Number(position.Anchor.X, 0, 1, "字幕 Anchor X");
            Number(position.Anchor.Y, 0, 1, "字幕 Anchor Y");
            Number(position.Pivot.X, 0, 1, "字幕 Pivot X");
            Number(position.Pivot.Y, 0, 1, "字幕 Pivot Y");
            Point(position.Offset);
        }
        Point(style.ShadowOffset);
        Color(style.Fill);
        Color(style.Stroke);
        Color(style.ShadowColor);
    }

    private static void Style(SubtitleStyle? style, Dictionary<Guid, ProjectAsset> assets)
    {
        ValidateSubtitleStyle(style);
        if (style.FontAssetId is { } font)
        {
            Asset(assets, font, ProjectAssetKind.FONT);
        }
    }

    private static void Tracks(ImmutableArray<AnimationTrack> tracks, bool allowLegacyColors = false, ClipMask? mask = null,
        bool allowNegativeKeyTimes = false, SubtitleLine? subtitle = null)
    {
        Require(!tracks.IsDefault && tracks.Length <= 38256 &&
            tracks.Count(track => track is not null && !AnimationPropertyMetadata.IsNodeProperty(track.Property) &&
                !track.Target.TextRangeId.HasValue && track.Target.State == SubtitleAnimationState.NORMAL) <= 64 &&
            tracks.Count(track => track is not null && AnimationPropertyMetadata.IsNodeProperty(track.Property)) <= 30000 &&
            tracks.Count(track => track is not null && (track.Target.TextRangeId.HasValue || track.Target.State != SubtitleAnimationState.NORMAL)) <= 8192,
            "普通、蒙版节点或文字动画轨道超过独立预算。");
        var targets = new HashSet<AnimationTrackTarget>();
        var maskNodes = mask is VectorClipMask vector ? vector.Contours.SelectMany(contour => contour.Nodes).Select(node => node.Id).ToHashSet() : null;
        foreach (var track in tracks)
        {
            NotNull(track, "数据项不能为 null。");
            var legacyColor = track.Property is >= AnimationProperty.FILL_RED and <= AnimationProperty.STROKE_ALPHA;
            Require(Enum.IsDefined(track.Property) &&
                (!AnimationPropertyMetadata.IsLegacyComponent(track.Property) || allowLegacyColors && legacyColor) &&
                AnimationPropertyMetadata.IsNodeProperty(track.Property) == track.Target.NodeId.HasValue &&
                track.Target.NodeId != Guid.Empty && targets.Add(track.Target) && Enum.IsDefined(track.ColorSpace) &&
                (track.ColorSpace == AnimationColorSpace.LINEAR_RGB || AnimationPropertyMetadata.GetValueKind(track.Property) == AnimationValueKind.COLOR),
                "动画目标未知、重复、携带无效节点或尚未完成分量迁移。");
            if (!legacyColor)
            {
                SubtitleAnimationTargetValidation.ValidateIdentity(track.Target);
            }
            Require(!allowLegacyColors || !track.Target.TextRangeId.HasValue, "预设不能保存真实文字动画范围身份。");
            if (!allowLegacyColors)
            {
                SubtitleAnimationTargetValidation.Validate(track.Target, subtitle, mask);
            }
            if (AnimationPropertyMetadata.IsMaskProperty(track.Property) && !allowLegacyColors)
            {
                Require(mask is not null, "蒙版动画缺少蒙版几何。");
                Require(!AnimationPropertyMetadata.IsNodeProperty(track.Property) || maskNodes?.Contains(track.Target.NodeId!.Value) == true,
                    "蒙版动画目标节点不存在。");
                Require(track.Property is not (AnimationProperty.MASK_RECTANGLE_TOP_LEFT or AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT) || mask is RectangleClipMask,
                    "矩形边界动画与蒙版形状不匹配。");
            }

            var dimension = AnimationPropertyMetadata.GetComponentCount(track.Property);
            Require(!track.Keyframes.IsDefault && !track.Transforms.IsDefault &&
                track.Keyframes.Length <= AnimationPropertyMetadata.GetMaximumTrackEntries(track.Property) &&
                track.Transforms.Length <= AnimationPropertyMetadata.GetMaximumTrackEntries(track.Property),
                "属性轨道超出分量时间并集预算。");
            if (track.IsOrdered)
            {
                Require(track.Keyframes.IsEmpty && track.InitialValue.HasValue, "有序变换与关键帧互斥且必须提供初始值。");
                AnimationValue(track.Property, track.InitialValue.Value);
                var operationIds = new HashSet<Guid>();
                foreach (var operation in track.Transforms)
                {
                    NotNull(operation, "有序变换操作不能为 null。");
                    Require(operation.Id != Guid.Empty && operationIds.Add(operation.Id) && operation.End >= operation.Start,
                        "有序变换标识或时间无效。");
                    Require(double.IsFinite(operation.Acceleration) && operation.Acceleration >= 0, "有序变换指数必须为有限非负数。");
                    Require(Enum.IsDefined(operation.Mode) && operation.ComponentMask >= 0 &&
                        operation.ComponentMask < (1 << dimension), "有序变换模式或分量掩码无效。");
                    if (operation.Mode == AnimationTransformMode.MULTIPLY_BY)
                    {
                        if (track.Property == AnimationProperty.FONT_SIZE)
                        {
                            Require(operation.Value.Kind == AnimationValueKind.SCALAR && double.IsFinite(operation.Value.Scalar) &&
                                operation.Value.Scalar > 0 && operation.Value.Scalar <= 409600, "字号乘法系数必须为有限正数。");
                        }
                        else
                        {
                            Require(AnimationPropertyMetadata.GetValueKind(track.Property) == AnimationValueKind.COLOR &&
                                operation.Value.Kind == AnimationValueKind.COLOR &&
                                operation.Value.Color.Red == 1 && operation.Value.Color.Green == 1 && operation.Value.Color.Blue == 1 &&
                                double.IsFinite(operation.Value.Color.Alpha) && operation.Value.Color.Alpha is >= 0 and <= 1 &&
                                (operation.ComponentMask == 0 || operation.ComponentMask == 8), "颜色乘法仅允许透明度衰减。");
                        }
                    }
                    else
                    {
                        AnimationValue(track.Property, operation.Value);
                    }
                }

                if (track.Property == AnimationProperty.FONT_SIZE)
                {
                    ValidateFontSizeTransformBounds(track);
                }

                continue;
            }

            Require(!track.Keyframes.IsEmpty && track.InitialValue is null, "关键帧轨道不能为空或包含有序变换初始值。");
            Timing.MediaTime? previous = null;
            foreach (var frame in track.Keyframes)
            {
                NotNull(frame, "数据项不能为 null。");
                Require(allowNegativeKeyTimes || frame.Time >= Timing.MediaTime.Zero, "预设关键帧时间必须非负。");
                Require(!previous.HasValue || frame.Time > previous.Value, "关键帧时间必须严格递增。");
                Curve(new(frame.Interpolation, frame.CurveStart, frame.CurveEnd) { Exponent = frame.Exponent, Reverse = frame.Reverse });
                AnimationValue(track.Property, frame.Value);
                Require(!frame.ComponentCurves.IsDefault && (frame.ComponentCurves.IsEmpty || frame.ComponentCurves.Length == dimension - 1),
                    "分量曲线数量与动画属性不一致。");
                foreach (var curve in frame.ComponentCurves)
                {
                    if (curve is not null)
                    {
                        Curve(curve);
                    }
                }

                previous = frame.Time;
            }
        }

        foreach (var property in new[] { AnimationProperty.FILL, AnimationProperty.STROKE })
        {
            Require(!targets.Contains(new(property)) ||
                !AnimationPropertyMetadata.GetLegacyComponents(property).Any(component => targets.Contains(new(component))),
                "完整颜色轨道不能与同组旧分量同时存在。");
        }
    }

    private static void AnimationValue(AnimationProperty property, AnimationValue value)
    {
        Require(value.Kind == AnimationPropertyMetadata.GetValueKind(property), "动画值维度与属性不一致。");
        for (var component = 0; component < value.ComponentCount; component++)
        {
            Number(value.GetComponent(component), AnimationPropertyMetadata.GetMinimum(property, component),
                AnimationPropertyMetadata.GetMaximum(property, component), "动画值分量");
        }
    }

    private static void ValidateFontSizeTransformBounds(AnimationTrack track)
    {
        var minimum = track.InitialValue!.Value.Scalar;
        var maximum = minimum;
        foreach (var operation in track.Transforms)
        {
            var value = operation.Value.Scalar;
            if (operation.Mode == AnimationTransformMode.MULTIPLY_BY)
            {
                minimum *= Math.Min(1, value);
                maximum *= Math.Max(1, value);
            }
            else
            {
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
        }
        Number(minimum, 0.01, 4096, "有序字号变换的保守下界");
        Number(maximum, 0.01, 4096, "有序字号变换的保守上界");
    }

    private static void Mask(ClipMask mask)
    {
        Require(mask.Transform is not null, "缺少蒙版变换。");
        Point(mask.Transform.Position);
        Point(mask.Transform.Pivot);
        Number(mask.Transform.Scale.X, -10000, 10000, "蒙版 Scale X");
        Number(mask.Transform.Scale.Y, -10000, 10000, "蒙版 Scale Y");
        Number(mask.Transform.Rotation, -1e9, 1e9, "蒙版 Rotation");

        switch (mask)
        {
            case RectangleClipMask rectangle:
                Point(rectangle.TopLeft);
                Point(rectangle.BottomRight);
                Require(rectangle.TopLeft.X <= rectangle.BottomRight.X && rectangle.TopLeft.Y <= rectangle.BottomRight.Y,
                    "矩形蒙版边界必须按左上角到右下角排列。");
                break;
            case VectorClipMask vector:
                Require(!vector.Contours.IsDefaultOrEmpty && vector.Contours.Length <= 10000, "蒙版轮廓为空或过大。");
                var identities = new HashSet<Guid>();
                var nodeCount = 0;
                foreach (var contour in vector.Contours)
                {
                    NotNull(contour, "蒙版轮廓不能为 null。");
                    Require(contour.Id != Guid.Empty && identities.Add(contour.Id), "蒙版轮廓标识为空或重复。");
                    Require(!contour.Nodes.IsDefaultOrEmpty, "闭合蒙版轮廓必须包含节点。");
                    Require(contour.Nodes.Length <= 10000 - nodeCount, "蒙版节点总量超过预算。");
                    nodeCount += contour.Nodes.Length;
                    foreach (var node in contour.Nodes)
                    {
                        NotNull(node, "蒙版节点不能为 null。");
                        Require(node.Id != Guid.Empty && identities.Add(node.Id), "蒙版节点标识为空或重复。");
                        Point(node.Position);
                        Point(node.InHandle);
                        Point(node.OutHandle);
                    }
                }

                break;
            default:
                throw new InvalidDataException("未知蒙版类型。");
        }
    }

    private static void Curve(AnimationCurve curve)
    {
        Require(Enum.IsDefined(curve.Interpolation) && double.IsFinite(curve.CurveStart) && double.IsFinite(curve.CurveEnd) &&
            curve.CurveStart >= 0 && curve.CurveStart < curve.CurveEnd && curve.CurveEnd <= 1 &&
            double.IsFinite(curve.Exponent) && curve.Exponent > 0,
            "关键帧插值及裁剪相位必须有效且位于零到一之间。");
    }

    private static void ValidateGeneratedRangeOrigins(ImmutableArray<SubtitleAnimationRange> ranges)
    {
        var byId = ranges.ToDictionary(range => range.Id);
        foreach (var range in ranges)
        {
            if (range.GeneratedOrigin is not { } origin)
            {
                continue;
            }
            Require(IsEffectIdentifier(origin.EffectId) && IsEffectIdentifier(origin.ScopeName), "范围生成来源的脚本或作用域标识无效。");
            Require(!string.IsNullOrWhiteSpace(origin.UnitDefinition) && origin.UnitDefinition.Length <= 262144,
                "范围生成来源的分组定义为空或过大。");
            ValidateText(origin.UnitDefinition);
            var visited = new HashSet<Guid> { range.Id };
            var parentId = origin.ParentRangeId;
            while (parentId is { } id)
            {
                Require(byId.TryGetValue(id, out var parent) && visited.Add(id), "范围生成来源引用不存在的父范围或形成循环。");
                parentId = parent.GeneratedOrigin?.ParentRangeId;
            }
        }
    }

    private static bool IsEffectIdentifier(string value)
    {
        return value is { Length: > 0 and <= 64 } && value[0] is >= 'a' and <= 'z' &&
            value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-');
    }

    private static void Motion(MotionPath? motion)
    {
        if (motion is null)
        {
            return;
        }

        Require(motion.Duration > Timing.MediaTime.Zero, "路径时长必须大于零。");
        Path(motion.Path);
    }

    private static void Path(PathGeometry? path)
    {
        Require(path is not null && !path.Segments.IsDefaultOrEmpty && path.Segments.Length <= 10000, "路径为空或过大。");
        Point(path.Start);
        foreach (CubicBezierSegment? segment in path.Segments)
        {
            Require(segment is not null, "路径段为空。");
            Point(segment.Control1);
            Point(segment.Control2);
            Point(segment.End);
        }
    }

    private static void Point(ScenePoint point)
    {
        Number(point.X, -1e9, 1e9, "路径 X");
        Number(point.Y, -1e9, 1e9, "路径 Y");
    }

    private static void Color(SceneColor color)
    {
        Number(color.Red, -65504, 65504, "Red");
        Number(color.Green, -65504, 65504, "Green");
        Number(color.Blue, -65504, 65504, "Blue");
        Number(color.Alpha, 0, 1, "Alpha");
    }

    private static void Asset(Dictionary<Guid, ProjectAsset> assets, Guid id, ProjectAssetKind kind)
    {
        Require(assets.TryGetValue(id, out var asset) && asset.Kind == kind, "资源引用不存在或类型不符。");
    }

    private static void Number(double value, double minimum, double maximum, string name)
    {
        Require(double.IsFinite(value) && value >= minimum && value <= maximum, $"{name} 超出有限范围。");
    }

    private static void NotNull([NotNull] object? value, string message)
    {
        if (value is null)
        {
            throw new InvalidDataException(message);
        }
    }

    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
