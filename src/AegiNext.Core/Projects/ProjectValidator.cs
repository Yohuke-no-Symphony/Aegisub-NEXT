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
        NotNull(document.TimelineViewState, "The timeline view state cannot be null.");
        document.TimelineViewState.Validate();
        Require(document.Version == ProjectDocument.CURRENT_VERSION, "Unsupported project version.");
        Require(document.Id != Guid.Empty && document.Name is { Length: <= 1024 }, "The project ID or name is invalid.");
        ValidateText(document.Name);
        Require(document.Width is > 0 and <= 32768 && document.Height is > 0 and <= 32768 &&
            (long)document.Width * document.Height <= 33177600, "The project canvas exceeds the pixel budget.");
        Require(document.FrameRate is { Numerator: > 0 } &&
            (double)document.FrameRate.Numerator / document.FrameRate.Denominator <= 1000, "The project frame rate is invalid.");
        Number(document.ReferenceWhiteNits, 0.001, 10000, "Reference white");
        Require(!document.Assets.IsDefault && document.Assets.Length <= 10000 &&
            !document.Tracks.IsDefault && document.Tracks.Length <= 10000 &&
            !document.Subtitles.IsDefault && document.Subtitles.Length <= 100000 &&
            !document.Layers.IsDefault && document.Layers.Length <= 10000 && !document.Presets.IsDefault, "Project collections are invalid or too large.");
        var assets = new Dictionary<Guid, ProjectAsset>();
        foreach (var asset in document.Assets)
        {
            NotNull(asset, "A data item cannot be null.");
            Require(asset.Id != Guid.Empty && assets.TryAdd(asset.Id, asset), "An asset ID is empty or duplicated.");
            Require(Enum.IsDefined(asset.Kind), "Unknown asset kind.");
            Require(Enum.IsDefined(asset.Storage) && (asset.Kind != ProjectAssetKind.FONT || asset.Storage == ProjectAssetStorage.MANAGED),
                "The asset storage mode is invalid.");
            if (asset.ExternalPath is { } external)
            {
                Require((asset.Kind == ProjectAssetKind.MEDIA || asset.Kind == ProjectAssetKind.IMAGE && asset.Storage == ProjectAssetStorage.LINKED) &&
                    asset.RelativePath == string.Empty && ProjectAssetLocation.IsAbsoluteReference(external), "Only media and linked images can use absolute external references.");
            }
            else
            {
                ValidateRelativePath(asset.RelativePath);
            }
            Require(asset.Sha256 is null || asset.Sha256.Length == 64 && asset.Sha256.All(Uri.IsHexDigit), "The asset hash is invalid.");
        }

        ValidateAssetLibrary(document.AssetLibrary, assets);

        if (document.Media is { } media)
        {
            Asset(assets, media.AssetId, ProjectAssetKind.MEDIA);
            Require(media.VideoStreamIndex >= 0 && media.AudioStreamIndex is null or >= 0, "The media stream index is invalid.");
        }

        var trackIds = new HashSet<Guid>();
        foreach (var track in document.Tracks)
        {
            NotNull(track, "A track cannot be null.");
            Require(track.Id != Guid.Empty && trackIds.Add(track.Id) && !string.IsNullOrWhiteSpace(track.Name) &&
                track.Name.Length <= 128 && !track.Name.Any(char.IsControl), "The track ID or name is invalid.");
            ValidateText(track.Name);
            Require(track.DefaultStyle is null
                ? track.StylePresetId is null && track.StylePresetName is null
                : track.StylePresetId is { } presetId && presetId != Guid.Empty &&
                    track.StylePresetName is { Length: > 0 and <= 1024 }, "The track style preset source is incomplete.");
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
            NotNull(line, "A data item cannot be null.");
            Require(line.Id != Guid.Empty && subtitles.TryAdd(line.Id, line), "A subtitle ID is empty or duplicated.");
            Require(line.Start < line.End && line.Text is not null && !line.Karaoke.IsDefault &&
                !line.InactiveKaraoke.IsDefault && !line.KaraokeStyleSpans.IsDefault &&
                !line.InlineSpans.IsDefault && !line.AnimationRanges.IsDefault && line.AnimationRanges.Length <= 256, "The subtitle time interval or text is invalid.");
            ValidateText(line.Text);
            ValidateSubtitleStyleName(line.StyleName);
            Require(line.StylePresetId is null || line.StylePresetId != Guid.Empty, "The subtitle style preset ID is invalid.");
            Require(line.ColorTagId is null || colorTagIds.Contains(line.ColorTagId.Value), "The subtitle references a missing color tag.");
            Style(line.Style, assets);
            var boundaries = line.InlineSpans.IsEmpty && line.Karaoke.IsEmpty && line.InactiveKaraoke.IsEmpty &&
                line.KaraokeStyleSpans.IsEmpty && line.AnimationRanges.IsEmpty
                ? null : new SubtitleTextBoundaries(line.Text);
            var previousEnd = 0;
            foreach (var span in line.InlineSpans)
            {
                NotNull(span, "An inline style cannot be null.");
                Require(span.Utf16Start >= previousEnd && span.Utf16Length > 0 &&
                    (long)span.Utf16Start + span.Utf16Length <= line.Text.Length, "Inline style text ranges overlap or exceed the text bounds.");
                var end = checked(span.Utf16Start + span.Utf16Length);
                Require(boundaries!.Contains(span.Utf16Start) && boundaries.Contains(end), "An inline style cannot split a grapheme.");
                var inlineStyle = span.Style;
                NotNull(inlineStyle, "An inline style cannot be null.");
                Require(inlineStyle.HasOverrides && !(inlineStyle.ClearFontAsset && inlineStyle.FontAssetId.HasValue) &&
                    !(inlineStyle.ClearFontVariant && inlineStyle.FontVariant is not null),
                    "The inline style is empty or contains conflicting font overrides.");
                Style(inlineStyle.ApplyTo(line.Style), assets);
                previousEnd = end;
            }
            ValidateSubtitleKaraokeCore(line, boundaries);
            foreach (var range in line.AnimationRanges)
            {
                NotNull(range, "A text animation range cannot be null.");
                Require(range.Id != Guid.Empty && rangeIds.Add(range.Id) && Enum.IsDefined(range.Pivot), "The text animation range ID or pivot is invalid.");
                Require(range.Utf16Start >= 0 && range.Utf16Length > 0 &&
                    (long)range.Utf16Start + range.Utf16Length <= line.Text.Length, "The text animation range is empty or exceeds the text bounds.");
                Require(boundaries!.Contains(range.Utf16Start) && boundaries.Contains(range.Utf16Start + range.Utf16Length),
                    "A text animation range cannot split a grapheme.");
                Number(range.Scale.X, -10000, 10000, "Range Scale X");
                Number(range.Scale.Y, -10000, 10000, "Range Scale Y");
                Number(range.Rotation, -1e9, 1e9, "Range Rotation");
                Number(range.RotationX, -1e9, 1e9, "Range Rotation X");
                Number(range.RotationY, -1e9, 1e9, "Range Rotation Y");
                Projection(range.PerspectiveDistance, range.ProjectionTranslation);
                Point(range.Offset);
            }
            ValidateGeneratedRangeOrigins(line.AnimationRanges);
        }

        var ids = new HashSet<Guid>();
        var referenced = new HashSet<Guid>();
        foreach (var layer in document.Layers)
        {
            Layer(layer, assets, subtitles, ids, referenced, enforceAnimationRange);
            Require(trackIds.Contains(layer.TrackId), "The clip references a missing track.");
        }

        foreach (var track in document.Layers.GroupBy(layer => layer.TrackId))
        {
            ProjectLayer? previous = null;
            foreach (var layer in track.OrderBy(layer => layer.Start))
            {
                Require(previous is null || previous.End <= layer.Start, "Clips on the same track cannot overlap.");
                previous = layer;
            }
        }

        Require(referenced.Count == subtitles.Count, "Each subtitle must be referenced by exactly one subtitle layer.");
        var presetIds = new HashSet<Guid>();
        Require(document.Presets.Length <= 10000, "The preset count exceeds the budget.");
        foreach (var preset in document.Presets)
        {
            NotNull(preset, "A data item cannot be null.");
            Require(preset.Id != Guid.Empty && presetIds.Add(preset.Id) &&
                preset.Name is { Length: > 0 and <= 1024 } && Enum.IsDefined(preset.Blend), "The preset is invalid.");
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
        Require(line.Text is not null, "Subtitle text cannot be null.");
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
        Require(line.Text is not null, "Subtitle text cannot be null.");
        if (!ReferenceEquals(line.Text, boundaries.Text))
        {
            throw new ArgumentException("The grapheme index must reference the same text instance as the subtitle.", nameof(boundaries));
        }
        ValidateText(line.Text);
        ValidateSubtitleKaraokeCore(line, boundaries);
    }

    private static void ValidateSubtitleKaraokeCore(SubtitleLine line, SubtitleTextBoundaries? boundaries)
    {
        Require(!line.Karaoke.IsDefault && !line.InactiveKaraoke.IsDefault && !line.KaraokeStyleSpans.IsDefault,
            "The karaoke arrays are invalid.");
        var previousStyleEnd = 0;
        foreach (var span in line.KaraokeStyleSpans)
        {
            NotNull(span, "A data item cannot be null.");
            Require(span.Utf16Start >= previousStyleEnd && span.Utf16Length > 0 &&
                (long)span.Utf16Start + span.Utf16Length <= line.Text.Length, "Karaoke style text ranges overlap or exceed the text bounds.");
            var end = checked(span.Utf16Start + span.Utf16Length);
            Require(boundaries!.Contains(span.Utf16Start) && boundaries.Contains(end), "A karaoke style cannot split a grapheme.");
            Require(span.ActiveStyle is { HasOverrides: true } || span.InactiveStyle is { HasOverrides: true },
                "A karaoke style range must contain visual overrides.");
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
                "The karaoke style preset source is invalid.");
            ValidateText(karaokeStyle.PresetName);
            Number(karaokeStyle.StrokeWidth, 0, 4096, "Karaoke stroke width");
            Number(karaokeStyle.FillBlur, 0, 512, "Karaoke fill blur");
            Number(karaokeStyle.StrokeBlur, 0, 512, "Karaoke stroke blur");
            Number(karaokeStyle.ShadowBlur, 0, 512, "Karaoke shadow blur");
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
                throw new InvalidDataException("Active and inactive karaoke text ranges cannot overlap.");
            }
        }
    }

    private static void ValidateKaraokeSegments(ImmutableArray<KaraokeSegment> segments, SubtitleLine line,
        SubtitleTextBoundaries? boundaries, HashSet<Guid> segmentIds)
    {
        var previousEnd = 0;
        foreach (var segment in segments)
        {
            NotNull(segment, "A data item cannot be null.");
            Require(segment.Id != Guid.Empty && segmentIds.Add(segment.Id) && Enum.IsDefined(segment.HighlightKind),
                "A karaoke ID is empty or duplicated, or its highlight kind is invalid.");
            Require(segment.Utf16Start >= previousEnd && segment.Utf16Length > 0 &&
                (long)segment.Utf16Start + segment.Utf16Length <= line.Text.Length, "Karaoke text ranges overlap or exceed the text bounds.");
            var end = checked(segment.Utf16Start + segment.Utf16Length);
            Require(boundaries!.Contains(segment.Utf16Start) && boundaries.Contains(end), "Karaoke cannot split a grapheme.");
            Require(segment.Start >= Timing.MediaTime.Zero && segment.Start < segment.End, "Karaoke timing is out of bounds.");
            Color(segment.HighlightColor);
            previousEnd = end;
        }
    }

    /// <summary>验证跨平台的工程内相对资源路径。</summary>
    public static void ValidateRelativePath(string path)
    {
        Require(!string.IsNullOrWhiteSpace(path) && path.Length <= 4096 && !path.Contains('\\') && !path.Contains(':') &&
            !path.Any(char.IsControl) && !System.IO.Path.IsPathRooted(path) &&
            path.Split('/').All(segment => segment is not "" and not "." and not ".."), "Assets must use canonical relative paths within the project.");
        ValidateText(path);
    }

    /// <summary>拒绝不完整 UTF-16 和空字符，避免 JSON 或文本编码时悄悄替换内容。</summary>
    public static void ValidateText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var remaining = text.AsSpan();
        Require(remaining.IndexOf('\0') < 0, "The text contains invalid Unicode or a null character.");
        while (!remaining.IsEmpty)
        {
            var index = remaining.IndexOfAnyInRange('\uD800', '\uDFFF');
            if (index < 0)
            {
                return;
            }
            Require(index + 1 < remaining.Length && char.IsHighSurrogate(remaining[index]) &&
                char.IsLowSurrogate(remaining[index + 1]), "The text contains invalid Unicode or a null character.");
            remaining = remaining[(index + 2)..];
        }
    }

    private static void Layer(ProjectLayer? layer, Dictionary<Guid, ProjectAsset> assets,
        Dictionary<Guid, SubtitleLine> subtitles, HashSet<Guid> ids, HashSet<Guid> referenced, bool enforceAnimationRange)
    {
        Require(layer is not null && layer.Id != Guid.Empty && ids.Add(layer.Id) && ids.Count <= 10000,
            "The clip count exceeds the budget or contains duplicate IDs.");
        Require(layer.Start < layer.End && layer.Name is { Length: <= 1024 } &&
            Enum.IsDefined(layer.Kind) && Enum.IsDefined(layer.Blend), "The clip data is invalid.");
        ValidateText(layer.Name);
        Require(layer.Transform is not null, "The layer transform is missing.");
        if (layer.Position is { } position)
        {
            Require(layer.Kind != LayerKind.SUBTITLE, "Subtitle placement must be stored in the subtitle style.");
            Position(position);
        }
        var transform = layer.Transform;
        Number(transform.X, -1e9, 1e9, "X");
        Number(transform.Y, -1e9, 1e9, "Y");
        Number(transform.AnchorX, -1e9, 1e9, "AnchorX");
        Number(transform.AnchorY, -1e9, 1e9, "AnchorY");
        Number(transform.ScaleX, -10000, 10000, "ScaleX");
        Number(transform.ScaleY, -10000, 10000, "ScaleY");
        Number(transform.Rotation, -1e9, 1e9, "Rotation");
        Number(transform.RotationX, -1e9, 1e9, "Rotation X");
        Number(transform.RotationY, -1e9, 1e9, "Rotation Y");
        Projection(transform.PerspectiveDistance, transform.ProjectionTranslation);
        Number(layer.Opacity, 0, 1, "Opacity");
        Number(layer.StrokeWidth, 0, 4096, "StrokeWidth");
        Number(layer.Blur, 0, 512, "Blur");
        Color(layer.Fill);
        Color(layer.Stroke);
        if (layer.Mask is { } mask)
        {
            Require(layer.Kind == LayerKind.SUBTITLE && layer.SubtitleId is { } maskSubtitleId &&
                subtitles.ContainsKey(maskSubtitleId), "Only subtitle clips referencing a valid subtitle can contain a mask.");
            Mask(mask);
        }

        var subtitle = layer.SubtitleId is { } animationSubtitleId && subtitles.TryGetValue(animationSubtitleId, out var animationLine)
            ? animationLine : null;
        Tracks(layer.Tracks, mask: layer.Mask, allowNegativeKeyTimes: true, subtitle: subtitle);
        Require(layer.Kind == LayerKind.SUBTITLE || layer.Tracks.All(track => !AnimationPropertyMetadata.IsSubtitleOnlyProperty(track.Property)),
            "Subtitle layout and channel-blur animation can only be applied to subtitle clips.");
        if (enforceAnimationRange)
        {
            var (minimumKeyTime, maximumKeyTime) = Editing.LayerAnimationTiming.GetRange(layer);
            Require(layer.Tracks.All(track => track.Keyframes.All(frame => frame.Time >= minimumKeyTime && frame.Time <= maximumKeyTime)),
                "Keyframes must fall within the layer clip's time interval.");
        }
        Motion(layer.MotionPath);
        Require((layer.Kind == LayerKind.SUBTITLE) == layer.SubtitleId.HasValue &&
            (layer.Kind == LayerKind.SHAPE) == (layer.Shape is not null) &&
            (layer.Kind == LayerKind.IMAGE) == (layer.Image is not null), "The layer payload does not match its kind.");
        if (layer.SubtitleId is { } subtitleId)
        {
            Require(subtitles.TryGetValue(subtitleId, out var line) && referenced.Add(subtitleId), "A subtitle layer reference is missing or duplicated.");
            Require(layer.Start == line.Start && layer.End == line.End, "Subtitle layer timing must match the subtitle line.");
        }

        if (layer.Shape is { } shape)
        {
            ValidateLayerShape(shape);
        }

        if (layer.Image is { } image)
        {
            Asset(assets, image.AssetId, ProjectAssetKind.IMAGE);
            Number(image.Width, 0.001, 1e6, "Image width");
            Number(image.Height, 0.001, 1e6, "Image height");
            Require(Enum.IsDefined(image.Fit), "Unknown image fit mode.");
            ValidateImageCrop(image.Crop);
            if (image.Playback is { } playback)
            {
                Require(Enum.IsDefined(playback.Mode) && double.IsFinite(playback.Speed) && playback.Speed > 0,
                    "The image playback mode or speed is invalid.");
            }
        }

    }

    /// <summary>验证字幕样式本身的排版、颜色及数值，不解析工程字体引用。</summary>
    public static void ValidateSubtitleStyle([NotNull] SubtitleStyle? style)
    {
        Require(style is not null && style.FontFamily is { Length: > 0 and <= 512 } && Enum.IsDefined(style.Alignment), "The subtitle style is invalid.");
        Require(style.TextAlign is null || Enum.IsDefined(style.TextAlign.Value), "The subtitle text alignment is invalid.");
        Require(Enum.IsDefined(style.WrapMode), "The subtitle wrapping mode is invalid.");
        ValidateSubtitleScaling(style.Scaling);
        ValidateText(style.FontFamily);
        if (style.FontVariant is { } variant)
        {
            Require(!string.IsNullOrWhiteSpace(variant.Name) && variant.Name.Length <= 512 &&
                !variant.Name.Any(char.IsControl), "The font variant name is invalid.");
            ValidateText(variant.Name);
            if (variant.PostScriptName is { } postScriptName)
            {
                Require(!string.IsNullOrWhiteSpace(postScriptName) && postScriptName.Length <= 512 &&
                    !postScriptName.Any(char.IsControl), "The font variant PostScript name is invalid.");
                ValidateText(postScriptName);
            }
            Require(variant.Weight is >= 1 and <= 1000 && variant.Width is >= 1 and <= 9, "The font variant style attributes are invalid.");
        }
        Number(style.FontSize, 0.01, 4096, "Font size");
        Number(style.LetterSpacing, -4096, 4096, "Letter spacing");
        Number(style.FillBlur, 0, 512, "Fill blur");
        Number(style.StrokeBlur, 0, 512, "Stroke blur");
        Number(style.StrokeWidth, 0, 4096, "Stroke width");
        Number(style.Margins.Left, 0, 32768, "Subtitle left margin");
        Number(style.Margins.Right, 0, 32768, "Subtitle right margin");
        Number(style.Margins.Vertical, 0, 32768, "Subtitle vertical margin");
        Number(style.LineHeight, 0.1, 10, "Line height");
        Number(style.ShadowBlur, 0, 512, "Shadow blur");
        if (style.Position is { } position)
        {
            Position(position);
        }
        Point(style.ShadowOffset);
        Color(style.Fill);
        Color(style.Stroke);
        Color(style.ShadowColor);
    }

    /// <summary>验证字幕参考分辨率与宽高匹配权重；参考尺寸不分配渲染表面。</summary>
    public static void ValidateSubtitleScaling([NotNull] SubtitleScalingSettings? settings)
    {
        Require(settings is not null, "Subtitle reference dimensions cannot be null.");
        Require(settings.ReferenceWidth is > 0 and <= 32768 && settings.ReferenceHeight is > 0 and <= 32768,
            "Subtitle reference dimensions must be integers between 1 and 32768.");
        Number(settings.MatchWidthOrHeight, 0, 1, "Subtitle width/height matching weight");
    }

    private static void Position(LayerPosition position)
    {
        Number(position.Anchor.X, 0, 1, "Anchor X");
        Number(position.Anchor.Y, 0, 1, "Anchor Y");
        Number(position.Pivot.X, 0, 1, "Pivot X");
        Number(position.Pivot.Y, 0, 1, "Pivot Y");
        Point(position.Offset);
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
            "Regular, mask-node, or text animation tracks exceed their individual budgets.");
        var targets = new HashSet<AnimationTrackTarget>();
        var maskNodes = mask is VectorClipMask vector ? vector.Contours.SelectMany(contour => contour.Nodes).Select(node => node.Id).ToHashSet() : null;
        foreach (var track in tracks)
        {
            NotNull(track, "A data item cannot be null.");
            var legacyColor = track.Property is >= AnimationProperty.FILL_RED and <= AnimationProperty.STROKE_ALPHA;
            Require(Enum.IsDefined(track.Property) &&
                (!AnimationPropertyMetadata.IsLegacyComponent(track.Property) || allowLegacyColors && legacyColor) &&
                AnimationPropertyMetadata.IsNodeProperty(track.Property) == track.Target.NodeId.HasValue &&
                track.Target.NodeId != Guid.Empty && targets.Add(track.Target) && Enum.IsDefined(track.ColorSpace) &&
                (track.ColorSpace == AnimationColorSpace.LINEAR_RGB || AnimationPropertyMetadata.GetValueKind(track.Property) == AnimationValueKind.COLOR),
                "An animation target is unknown or duplicated, contains an invalid node, or requires component migration.");
            if (!legacyColor)
            {
                SubtitleAnimationTargetValidation.ValidateIdentity(track.Target);
            }
            Require(!allowLegacyColors || !track.Target.TextRangeId.HasValue, "Presets cannot store actual text animation range identities.");
            if (!allowLegacyColors)
            {
                SubtitleAnimationTargetValidation.Validate(track.Target, subtitle, mask);
            }
            if (AnimationPropertyMetadata.IsMaskProperty(track.Property) && !allowLegacyColors)
            {
                Require(mask is not null, "Mask animation is missing its mask geometry.");
                Require(!AnimationPropertyMetadata.IsNodeProperty(track.Property) || maskNodes?.Contains(track.Target.NodeId!.Value) == true,
                    "The mask animation target node does not exist.");
                Require(track.Property is not (AnimationProperty.MASK_RECTANGLE_TOP_LEFT or AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT) || mask is RectangleClipMask,
                    "Rectangle-boundary animation does not match the mask shape.");
            }

            ValidateAnimationTrackData(track, allowNegativeKeyTimes);
        }

        foreach (var property in new[] { AnimationProperty.FILL, AnimationProperty.STROKE })
        {
            Require(!targets.Contains(new(property)) ||
                !AnimationPropertyMetadata.GetLegacyComponents(property).Any(component => targets.Contains(new(component))),
                "A complete color track cannot coexist with legacy components from the same group.");
        }

        foreach (var target in targets.Where(target => target.Property == AnimationProperty.ROTATION_3D))
        {
            Require(!AnimationPropertyMetadata.GetLegacyComponents(AnimationProperty.ROTATION_3D)
                .Any(property => targets.Contains(target with { Property = property })),
                "A complete rotation track cannot coexist with scalar axes from the same target.");
        }
    }

    internal static void ValidateAnimationTrackData(AnimationTrack track, bool allowNegativeKeyTimes = false)
    {
        Require(Enum.IsDefined(track.ColorSpace) &&
            (track.ColorSpace == AnimationColorSpace.LINEAR_RGB || AnimationPropertyMetadata.GetValueKind(track.Property) == AnimationValueKind.COLOR),
            "The animation color space does not match the property.");
        var dimension = AnimationPropertyMetadata.GetComponentCount(track.Property);
        Require(!track.Keyframes.IsDefault && !track.Transforms.IsDefault &&
            track.Keyframes.Length <= AnimationPropertyMetadata.GetMaximumTrackEntries(track.Property) &&
            track.Transforms.Length <= AnimationPropertyMetadata.GetMaximumTrackEntries(track.Property),
            "The property track exceeds the component time-union budget.");
        if (track.IsOrdered)
        {
            Require(track.Keyframes.IsEmpty && track.InitialValue.HasValue, "Ordered transforms and keyframes are mutually exclusive, and ordered transforms require an initial value.");
            AnimationValue(track.Property, track.InitialValue.Value);
            var operationIds = new HashSet<Guid>();
            foreach (var operation in track.Transforms)
            {
                NotNull(operation, "An ordered transform operation cannot be null.");
                Require(operation.Id != Guid.Empty && operationIds.Add(operation.Id) && operation.End >= operation.Start,
                    "The ordered transform ID or timing is invalid.");
                Require(double.IsFinite(operation.Acceleration) && operation.Acceleration >= 0, "The ordered transform exponent must be finite and nonnegative.");
                if (operation.Curve is { } curve)
                {
                    Curve(curve);
                }
                Require(Enum.IsDefined(operation.Mode) && operation.ComponentMask >= 0 &&
                    operation.ComponentMask < (1 << dimension), "The ordered transform mode or component mask is invalid.");
                if (operation.Mode == AnimationTransformMode.MULTIPLY_BY)
                {
                    if (track.Property == AnimationProperty.FONT_SIZE)
                    {
                        Require(operation.Value.Kind == AnimationValueKind.SCALAR && double.IsFinite(operation.Value.Scalar) &&
                            operation.Value.Scalar > 0 && operation.Value.Scalar <= 409600, "The font-size multiplier must be finite and positive.");
                    }
                    else
                    {
                        Require(AnimationPropertyMetadata.GetValueKind(track.Property) == AnimationValueKind.COLOR &&
                            operation.Value.Kind == AnimationValueKind.COLOR &&
                            operation.Value.Color.Red == 1 && operation.Value.Color.Green == 1 && operation.Value.Color.Blue == 1 &&
                            double.IsFinite(operation.Value.Color.Alpha) && operation.Value.Color.Alpha is >= 0 and <= 1 &&
                            (operation.ComponentMask == 0 || operation.ComponentMask == 8), "Color multiplication only permits opacity attenuation.");
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

            return;
        }

        Require(!track.Keyframes.IsEmpty && track.InitialValue is null, "A keyframe track cannot be empty or contain an ordered-transform initial value.");
        Timing.MediaTime? previous = null;
        foreach (var frame in track.Keyframes)
        {
            NotNull(frame, "A data item cannot be null.");
            Require(allowNegativeKeyTimes || frame.Time >= Timing.MediaTime.Zero, "Preset keyframe times must be nonnegative.");
            Require(!previous.HasValue || frame.Time > previous.Value, "Keyframe times must be strictly increasing.");
            Curve(new(frame.Interpolation, frame.CurveStart, frame.CurveEnd) { Exponent = frame.Exponent, Reverse = frame.Reverse });
            AnimationValue(track.Property, frame.Value);
            Require(!frame.ComponentCurves.IsDefault && (frame.ComponentCurves.IsEmpty || frame.ComponentCurves.Length == dimension - 1),
                "The number of component curves does not match the animation property.");
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

    private static void AnimationValue(AnimationProperty property, AnimationValue value)
    {
        Require(value.Kind == AnimationPropertyMetadata.GetValueKind(property), "The animation value dimensions do not match the property.");
        for (var component = 0; component < value.ComponentCount; component++)
        {
            Number(value.GetComponent(component), AnimationPropertyMetadata.GetMinimum(property, component),
                AnimationPropertyMetadata.GetMaximum(property, component), "Animation value component");
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
        Number(minimum, 0.01, 4096, "Conservative lower bound of ordered font-size transforms");
        Number(maximum, 0.01, 4096, "Conservative upper bound of ordered font-size transforms");
    }

    private static void Projection(double distance, SceneVector3 translation)
    {
        Number(distance, SceneProjection.MIN_DISTANCE, SceneProjection.MAX_DISTANCE, "Perspective distance");
        Number(translation.X, -1e9, 1e9, "Projection translation X");
        Number(translation.Y, -1e9, 1e9, "Projection translation Y");
        Number(translation.Z, -1e9, 1e9, "Projection translation Z");
    }

    private static void Mask(ClipMask mask)
    {
        Require(mask.Transform is not null, "The mask transform is missing.");
        Point(mask.Transform.Position);
        Point(mask.Transform.Pivot);
        Number(mask.Transform.Scale.X, -10000, 10000, "Mask Scale X");
        Number(mask.Transform.Scale.Y, -10000, 10000, "Mask Scale Y");
        Number(mask.Transform.Rotation, -1e9, 1e9, "Mask Rotation");

        switch (mask)
        {
            case RectangleClipMask rectangle:
                Point(rectangle.TopLeft);
                Point(rectangle.BottomRight);
                Require(rectangle.TopLeft.X <= rectangle.BottomRight.X && rectangle.TopLeft.Y <= rectangle.BottomRight.Y,
                    "Rectangle mask bounds must run from the top-left to the bottom-right.");
                break;
            case VectorClipMask vector:
                Require(!vector.Contours.IsDefaultOrEmpty && vector.Contours.Length <= 10000, "The mask contours are empty or exceed the budget.");
                var identities = new HashSet<Guid>();
                var nodeCount = 0;
                foreach (var contour in vector.Contours)
                {
                    NotNull(contour, "A mask contour cannot be null.");
                    Require(contour.Id != Guid.Empty && identities.Add(contour.Id), "A mask contour ID is empty or duplicated.");
                    Require(!contour.Nodes.IsDefaultOrEmpty, "A closed mask contour must contain nodes.");
                    Require(contour.Nodes.Length <= 10000 - nodeCount, "The total mask node count exceeds the budget.");
                    nodeCount += contour.Nodes.Length;
                    foreach (var node in contour.Nodes)
                    {
                        NotNull(node, "A mask node cannot be null.");
                        Require(node.Id != Guid.Empty && identities.Add(node.Id), "A mask node ID is empty or duplicated.");
                        Point(node.Position);
                        Point(node.InHandle);
                        Point(node.OutHandle);
                    }
                }

                break;
            default:
                throw new InvalidDataException("Unknown mask kind.");
        }
    }

    private static void Curve(AnimationCurve curve)
    {
        Require(Enum.IsDefined(curve.Interpolation) && double.IsFinite(curve.CurveStart) && double.IsFinite(curve.CurveEnd) &&
            curve.CurveStart >= 0 && curve.CurveStart < curve.CurveEnd && curve.CurveEnd <= 1 &&
            double.IsFinite(curve.Exponent) && curve.Exponent > 0,
            "Keyframe interpolation and clipping phases must be valid and between zero and one.");
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
            Require(IsEffectIdentifier(origin.EffectId) && IsEffectIdentifier(origin.ScopeName), "The range's generated-origin script or scope ID is invalid.");
            Require(!string.IsNullOrWhiteSpace(origin.UnitDefinition) && origin.UnitDefinition.Length <= 262144,
                "The range's generated-origin unit definition is empty or too large.");
            ValidateText(origin.UnitDefinition);
            var visited = new HashSet<Guid> { range.Id };
            var parentId = origin.ParentRangeId;
            while (parentId is { } id)
            {
                Require(byId.TryGetValue(id, out var parent) && visited.Add(id), "The range's generated origin references a missing parent range or forms a cycle.");
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

        Require(motion.Duration > Timing.MediaTime.Zero, "The path duration must be greater than zero.");
        Path(motion.Path);
    }

    private static void Path(PathGeometry? path)
    {
        Require(path is not null && !path.Segments.IsDefaultOrEmpty && path.Segments.Length <= 10000, "The path is empty or exceeds the budget.");
        Point(path.Start);
        foreach (CubicBezierSegment? segment in path.Segments)
        {
            Require(segment is not null, "The path segment is null.");
            Point(segment.Control1);
            Point(segment.Control2);
            Point(segment.End);
        }
    }

    private static void Point(ScenePoint point)
    {
        Number(point.X, -1e9, 1e9, "Path X");
        Number(point.Y, -1e9, 1e9, "Path Y");
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
        Require(assets.TryGetValue(id, out var asset) && asset.Kind == kind, "The asset reference does not exist or has the wrong kind.");
    }

    private static void Number(double value, double minimum, double maximum, string name)
    {
        Require(double.IsFinite(value) && value >= minimum && value <= maximum, $"{name} is outside the permitted finite range.");
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
