using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

/// <summary>ASS v4+ 交换；将不支持的效果列为损失，拒绝非法结构和时间。</summary>
public static class AssSubtitleFormat
{
    /// <summary>以目标画布重采样静态字幕布局，保留对白来源的稳定顺序。</summary>
    public static AssImportResult Parse(string source, int targetWidth = 1920, int targetHeight = 1080,
        IAssFontWeightResolver? fontWeightResolver = null, ISubtitlePlacementMeasurer? placementMeasurer = null,
        ISubtitleMaskGeometryCombiner? maskGeometryCombiner = null)
    {
        return ParseCore(source, targetWidth, targetHeight, fontWeightResolver, placementMeasurer, maskGeometryCombiner).Result;
    }

    /// <summary>按源 PlayRes 导入，不重采样已声明的轴；缺失的宽、高分别采用 1920、1080 并报告诊断。</summary>
    public static AssSourceImportResult ParseAtSourceResolution(string source, IAssFontWeightResolver? fontWeightResolver = null,
        ISubtitlePlacementMeasurer? placementMeasurer = null, ISubtitleMaskGeometryCombiner? maskGeometryCombiner = null)
    {
        return ParseCore(source, null, null, fontWeightResolver, placementMeasurer, maskGeometryCombiner);
    }

    private static AssSourceImportResult ParseCore(string source, int? requestedWidth, int? requestedHeight,
        IAssFontWeightResolver? fontWeightResolver, ISubtitlePlacementMeasurer? placementMeasurer,
        ISubtitleMaskGeometryCombiner? maskGeometryCombiner)
    {
        AssFormatValues.CheckText(source);
        var targetWidth = requestedWidth ?? 1920;
        var targetHeight = requestedHeight ?? 1080;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetHeight);
        var rows = source.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var info = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var styleRows = new List<Dictionary<string, string>>();
        var events = new List<Dictionary<string, string>>();
        var section = string.Empty;
        string[]? styleFormat = null;
        string[]? eventFormat = null;
        var sawInfo = false;
        var sawEvents = false;
        var attachments = false;
        foreach (var row in rows)
        {
            var trimmed = row.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';'))
            {
                continue;
            }
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                section = trimmed;
                sawInfo |= section.Equals("[Script Info]", StringComparison.OrdinalIgnoreCase);
                sawEvents |= section.Equals("[Events]", StringComparison.OrdinalIgnoreCase);
                attachments |= section.Equals("[Fonts]", StringComparison.OrdinalIgnoreCase) || section.Equals("[Graphics]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!section.Equals("[Script Info]", StringComparison.OrdinalIgnoreCase) && !section.Equals("[V4+ Styles]", StringComparison.OrdinalIgnoreCase) && !section.Equals("[Events]", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var content = row.TrimStart();
            var colon = content.IndexOf(':');
            if (colon < 0)
            {
                throw new InvalidDataException("An ASS line is missing a field separator.");
            }
            var key = content[..colon].Trim();
            var value = content[(colon + 1)..].TrimStart();
            if (section.Equals("[Script Info]", StringComparison.OrdinalIgnoreCase))
            {
                info[key] = value;
            }
            else if (section.Equals("[V4+ Styles]", StringComparison.OrdinalIgnoreCase))
            {
                if (key.Equals("Format", StringComparison.OrdinalIgnoreCase))
                {
                    styleFormat = ParseFormat(value);
                }
                else if (key.Equals("Style", StringComparison.OrdinalIgnoreCase))
                {
                    styleRows.Add(ParseFields(value, styleFormat ?? throw new InvalidDataException("ASS styles are missing a Format declaration.")));
                }
            }
            else if (section.Equals("[Events]", StringComparison.OrdinalIgnoreCase))
            {
                if (key.Equals("Format", StringComparison.OrdinalIgnoreCase))
                {
                    eventFormat = ParseFormat(value);
                    if (!eventFormat[^1].Equals("Text", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("ASS Text must be the last dialogue field.");
                    }
                }
                else if (key.Equals("Dialogue", StringComparison.OrdinalIgnoreCase))
                {
                    if (events.Count >= 100000)
                    {
                        throw new InvalidDataException("The ASS dialogue count exceeds the budget.");
                    }
                    events.Add(ParseFields(value, eventFormat ?? throw new InvalidDataException("ASS dialogue is missing a Format declaration.")));
                }
            }
        }
        if (!sawInfo || !sawEvents || info.TryGetValue("ScriptType", out var scriptType) && !scriptType.Equals("v4.00+", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Only ASS v4+ files with Script Info and Events sections are supported.");
        }
        var hasWidth = info.TryGetValue("PlayResX", out var x);
        var hasHeight = info.TryGetValue("PlayResY", out var y);
        var width = hasWidth ? AssFormatValues.Integer(x!) : targetWidth;
        var height = hasHeight ? AssFormatValues.Integer(y!) : targetHeight;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("ASS PlayRes values must be positive.");
        }
        if (requestedWidth is null)
        {
            ProjectValidator.Validate(new ProjectDocument { Width = width, Height = height });
            targetWidth = width;
            targetHeight = height;
        }
        var scaleX = (double)targetWidth / width;
        var scaleY = (double)targetHeight / height;
        var diagnostics = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();
        var colorMatrix = Get(info, "YCbCr Matrix", string.Empty).Trim().ToUpperInvariant();
        if (colorMatrix is "TV.601" or "PC.601" or "TV.709" or "PC.709" or "TV.240M" or "PC.240M" or "TV.FCC" or "PC.FCC")
        {
            diagnostics.Add(new("Ass.YCbCrMatrix", $"ASS YCbCr Matrix ({colorMatrix}) color-matching information was not imported. The project preserves RGB values; colors may differ if the source player matches them to the video color space."));
        }
        var wrapStyle = AssFormatValues.Integer(Get(info, "WrapStyle", "0"));
        if (wrapStyle is < 0 or > 3)
        {
            diagnostics.Add(new("Ass.WrapStyle", "ASS WrapStyle must be between 0 and 3. Default smart wrapping mode 0 was used."));
            wrapStyle = 0;
        }
        var layoutWidth = AssFormatValues.Integer(Get(info, "LayoutResX", "0"));
        var layoutHeight = AssFormatValues.Integer(Get(info, "LayoutResY", "0"));
        var blurUsesPlayRes = layoutWidth <= 0 || layoutHeight <= 0;
        var blurScaleX = (double)targetWidth / (blurUsesPlayRes ? width : layoutWidth);
        var blurScaleY = (double)targetHeight / (blurUsesPlayRes ? height : layoutHeight);
        var perspectiveDistance = SceneProjection.DEFAULT_DISTANCE * (blurUsesPlayRes ? 1 : (double)targetHeight / layoutHeight);
        var scaledBorder = !Get(info, "ScaledBorderAndShadow", "yes").Equals("no", StringComparison.OrdinalIgnoreCase);
        var resolution = scaledBorder ? new AssResolutionContext(scaleX, scaleY) :
            blurUsesPlayRes ? new(1, 1) : new((double)targetWidth / layoutWidth, (double)targetHeight / layoutHeight);
        if (!scaledBorder && blurUsesPlayRes)
        {
            diagnostics.Add(new("Ass.BorderLayoutResolution", "ASS does not provide complete, valid LayoutRes values. Unscaled stroke and shadow are interpreted in target canvas pixels; their appearance may differ if the original video dimensions differ."));
        }
        var styles = new Dictionary<string, AssStyleDefinition>(StringComparer.Ordinal);
        var unsupportedGeometry = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fields in styleRows)
        {
            var style = ParseStyle(fields, scaleX, scaleY, wrapStyle, resolution, diagnostics,
                new() { ReferenceWidth = targetWidth, ReferenceHeight = targetHeight });
            if (!styles.TryAdd(style.Name, style))
            {
                throw new InvalidDataException("ASS style names are duplicated.");
            }
            if (Get(fields, "BorderStyle", "1") != "1")
            {
                unsupportedGeometry.Add(style.Name);
            }
        }
        var fallback = new AssStyleDefinition("Default", new()
        {
            Scaling = new() { ReferenceWidth = targetWidth, ReferenceHeight = targetHeight },
            FontSize = 20 * scaleY, ShadowBlur = 0,
            WrapMode = wrapStyle == 2 ? SubtitleWrapMode.NO_WRAP : SubtitleWrapMode.NATURAL
        }, SceneColor.White);
        var lines = ImmutableArray.CreateBuilder<SubtitleLine>();
        var clips = ImmutableArray.CreateBuilder<SubtitleClipImport>();
        var layers = ImmutableArray.CreateBuilder<ProjectLayer>();
        if (!hasWidth || !hasHeight)
        {
            if (requestedWidth is not null)
            {
                diagnostics.Add(new("Ass.PlayRes", "Missing ASS PlayRes axes use the target canvas dimensions; declared axes are still resampled from their source dimensions."));
            }
            else
            {
                var missingAxes = new List<string>();
                if (!hasWidth)
                {
                    missingAxes.Add("PlayResX=1920");
                }
                if (!hasHeight)
                {
                    missingAxes.Add("PlayResY=1080");
                }
                diagnostics.Add(new("Ass.PlayRes", $"Missing ASS PlayRes axes use default dimensions ({string.Join(", ", missingAxes)}); declared axes retain their source dimensions."));
            }
        }
        if (attachments)
        {
            diagnostics.Add(new("Ass.Attachments", "Embedded ASS fonts or images were not imported. Add these assets to the project separately."));
        }
        foreach (var fields in events.OrderBy(row => AssFormatValues.Integer(Get(row, "Layer", "0"))))
        {
            var name = Get(fields, "Style", "Default");
            ProjectValidator.ValidateSubtitleStyleName(name);
            var definition = styles.TryGetValue(name, out var declared) ? declared : fallback;
            var start = AssFormatValues.ParseTime(Required(fields, "Start"));
            var end = AssFormatValues.ParseTime(Required(fields, "End"));
            if (start >= end)
            {
                throw new InvalidDataException("ASS end times must be later than start times.");
            }
            var line = new SubtitleLine { Start = start, End = end, Style = definition.Style, StyleName = name };
            var marginL = AssFormatValues.Number(Get(fields, "MarginL", "0"));
            var marginR = AssFormatValues.Number(Get(fields, "MarginR", "0"));
            var marginV = AssFormatValues.Number(Get(fields, "MarginV", "0"));
            if (marginL != 0 || marginR != 0 || marginV != 0)
            {
                line = line with
                {
                    Style = line.Style with
                    {
                        Margins = new(
                            marginL == 0 ? line.Style.Margins.Left : marginL * scaleX,
                            marginR == 0 ? line.Style.Margins.Right : marginR * scaleX,
                            marginV == 0 ? line.Style.Margins.Vertical : marginV * scaleY)
                    }
                };
            }
            var body = Required(fields, "Text");
            var parsed = new AssTextParser(line, styles, definition.Secondary, scaleX, scaleY, canvasWidth: targetWidth,
                canvasHeight: targetHeight, wrapStyle: wrapStyle, blurScaleX: blurScaleX, blurScaleY: blurScaleY,
                blurUsesPlayRes: blurUsesPlayRes, resolution: resolution, fontWeightResolver: fontWeightResolver,
                dialogueMargins: new(marginL * scaleX, marginR * scaleX, marginV * scaleY),
                placementMeasurer: placementMeasurer, maskGeometryCombiner: maskGeometryCombiner,
                perspectiveDistance: perspectiveDistance).Parse(body);
            var drawing = AssShapeImport.Convert(body, line, definition, parsed, scaleX, scaleY);
            var importedLine = parsed.Line;
            var keepSubtitle = !drawing.HasDrawing || importedLine.Text.Length > 0;
            if (keepSubtitle)
            {
                var clip = new SubtitleClipImport(importedLine, parsed.Mask,
                    parsed.MaskTracks.AddRange(parsed.PlacementTracks).AddRange(parsed.OpacityTracks).AddRange(parsed.NumericTracks), parsed.ContentOffset)
                {
                    Transform = parsed.Transform
                };
                lines.Add(importedLine);
                clips.Add(clip);
                layers.Add(new()
                {
                    Id = importedLine.Id, Kind = LayerKind.SUBTITLE, Name = "Subtitle", SubtitleId = importedLine.Id,
                    Start = importedLine.Start, End = importedLine.End,
                    Mask = clip.Mask, Tracks = clip.Tracks, Transform = clip.Transform, AnimationOffset = clip.ContentOffset
                });
            }
            layers.AddRange(drawing.Layers);
            if (layers.Count > 10000)
            {
                throw new InvalidDataException("Expanded ASS subtitle and drawing layers exceed the native 10,000-layer budget.");
            }
            var drawingLayerId = drawing.Layers.IsEmpty ? (Guid?)null : drawing.Layers[0].Id;
            diagnostics.AddRange(parsed.Diagnostics.Where(item => drawing.Layers.IsEmpty ||
                item.Code is not ("Ass.Drawing" or "Ass.DrawingBaseline" or "Ass.TransformLayout" or "Ass.RotationOrigin" or "Ass.WrapModeApproximation"))
                .Select(item => keepSubtitle ? item : item with { SubtitleId = null, LayerId = item.LayerId ?? drawingLayerId }));
            diagnostics.AddRange(drawing.Diagnostics.Select(item => keepSubtitle || item.SubtitleId is null ? item :
                item with { SubtitleId = null, LayerId = item.LayerId ?? drawingLayerId }));
            if (!styles.ContainsKey(name))
            {
                diagnostics.Add(new("Ass.UnknownStyle", $"Style '{name}' does not exist. The default style was used.",
                    SubtitleId: keepSubtitle ? line.Id : null, LayerId: drawingLayerId));
            }
            if (Get(fields, "Effect", "").Length > 0)
            {
                diagnostics.Add(new("Ass.Effect", "The ASS dialogue Effect field was not imported.",
                    SubtitleId: keepSubtitle ? line.Id : null, LayerId: drawingLayerId));
            }
            if (unsupportedGeometry.Contains(name))
            {
                diagnostics.Add(new("Ass.StyleGeometry", "The ASS style background box was not imported.",
                    SubtitleId: keepSubtitle ? line.Id : null, LayerId: drawingLayerId));
            }
        }
        return new(width, height, new(lines.ToImmutable(), diagnostics.ToImmutable())
        {
            Clips = clips.ToImmutable(), Layers = layers.ToImmutable()
        });
    }

    /// <summary>按工程合成层顺序导出字幕及可表达的静态形状，静态样式去重，时间显式量化到厘秒。</summary>
    public static SubtitleFormatWriteResult Write(ProjectDocument document, MediaTime timeOffset = default,
        ISubtitlePlacementMeasurer? placementMeasurer = null)
    {
        ProjectValidator.Validate(document);
        var sourceDocument = document;
        document = CanvasProjection(document);
        var result = new StringBuilder();
        result.AppendLine("[Script Info]").AppendLine("ScriptType: v4.00+")
            .AppendLine("PlayResX: " + document.Width.ToString(CultureInfo.InvariantCulture))
            .AppendLine("PlayResY: " + document.Height.ToString(CultureInfo.InvariantCulture))
            .AppendLine("LayoutResX: " + document.Width.ToString(CultureInfo.InvariantCulture))
            .AppendLine("LayoutResY: " + document.Height.ToString(CultureInfo.InvariantCulture))
            .AppendLine("YCbCr Matrix: None").AppendLine("WrapStyle: 1").AppendLine("ScaledBorderAndShadow: yes").AppendLine();
        result.AppendLine("[V4+ Styles]").AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        var diagnostics = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();
        var styles = AssStyleTable.Write(document.Subtitles, result, diagnostics);
        var graphicStyle = document.Layers.Any(layer => layer.Kind == LayerKind.SHAPE)
            ? AssShapeWriter.WriteStyle(styles.Values, result) : string.Empty;
        var reportedDiagnostics = new HashSet<(Guid? SubtitleId, string Code)>();
        result.AppendLine().AppendLine("[Events]").AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
        var byId = document.Subtitles.ToDictionary(line => line.Id);
        var order = 0;
        var exportedCount = 0;
        foreach (var layer in new ProjectClipIndex(document).LayersInDrawingOrder)
        {
            if (layer.Kind != LayerKind.SUBTITLE)
            {
                var drawing = AssShapeWriter.Write(layer, new(document.Width, document.Height), timeOffset, diagnostics);
                if (drawing is not null)
                {
                    if (exportedCount++ >= 100000)
                    {
                        throw new InvalidDataException("The total exported ASS dialogue count exceeds the 100,000-entry budget.");
                    }
                    result.AppendLine(string.Create(CultureInfo.InvariantCulture,
                        $"Dialogue: {order},{AssFormatValues.Time(layer.Start + timeOffset, MediaTimeRounding.FLOOR)},{AssFormatValues.Time(layer.End + timeOffset, MediaTimeRounding.CEILING)},{graphicStyle},,0,0,0,,{drawing}"));
                }
                order++;
                continue;
            }
            var line = byId[layer.SubtitleId!.Value];
            if (line.Start + timeOffset < MediaTime.Zero)
            {
                throw new InvalidDataException($"ASS subtitle {line.Id} has a negative external start time of {line.Start + timeOffset}. Adjust the interval or playback origin; it will not be clipped automatically.");
            }
            if (line.Style.FontAssetId.HasValue)
            {
                diagnostics.Add(new("Ass.FontResource", "ASS files do not include project-embedded fonts. Install the corresponding fonts in the playback environment.", SubtitleId: line.Id));
                reportedDiagnostics.Add((line.Id, "Ass.FontResource"));
            }
            if (line.Style.TextAlign is { } textAlign && (int)textAlign != (int)line.Style.Alignment % 3)
            {
                diagnostics.Add(new("Ass.TextAlign", "ASS alignment anchors cannot independently preserve project text alignment. Text alignment corresponding to the placement anchor was used during export.", SubtitleId: line.Id));
            }
            if (!line.Style.LineHeight.Equals(1.2))
            {
                diagnostics.Add(new("Ass.LineHeight", "ASS does not support custom project line heights.", SubtitleId: line.Id));
            }
            AssExportPrecision.AddStyle(line.Style, line.Id, diagnostics, includeBlur: false);
            AssExportPrecision.AddTime(line.Start + timeOffset, line.End + timeOffset, line.Id, diagnostics);
            if (layer.Blur > 0)
            {
                diagnostics.Add(new("Ass.LayerBlur", "ASS text-edge blur cannot represent project whole-layer blur. Whole-layer blur was omitted during export.", SubtitleId: line.Id));
            }
            var conversion = new AssEventConversionContext(sourceDocument, layer, line, placementMeasurer, diagnostics);
            foreach (var sample in AssMaskSampling.Samples(document, layer, line, diagnostics, timeOffset))
            {
                if (exportedCount++ >= 100000)
                {
                    throw new InvalidDataException("The total ASS dialogue count after mask expansion exceeds the 100,000-entry budget.");
                }
                var sampleLine = line with { Start = sample.Start, End = sample.End };
                var body = AssTextWriter.Write(sampleLine, sample.ContentTime, conversion: conversion,
                    eventOrigin: conversion.EventOrigin(sample, timeOffset), canvasUnits: true);
                foreach (var diagnostic in body.Diagnostics)
                {
                    if (reportedDiagnostics.Add((diagnostic.SubtitleId, diagnostic.Code)))
                    {
                        diagnostics.Add(diagnostic);
                    }
                }
                var placement = conversion.PlacementTags(sample, timeOffset);
                var opacity = conversion.OpacityTags(sample, timeOffset);
                var maskTags = sample.Tags.Length == 0 ? string.Empty : "{" + sample.Tags + "}";
                result.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"Dialogue: {order},{AssFormatValues.Time(sample.Start + timeOffset, MediaTimeRounding.FLOOR)},{AssFormatValues.Time(sample.End + timeOffset, MediaTimeRounding.CEILING)},{styles[line.Id]},,0,0,0,,{placement}{opacity}{maskTags}{body.Text}"));
            }
            order++;
        }
        var text = result.ToString();
        AssFormatValues.CheckText(text);
        return new(text, diagnostics.DistinctBy(diagnostic => (diagnostic.SubtitleId, diagnostic.LayerId, diagnostic.Code)).ToImmutableArray());
    }

    private static ProjectDocument CanvasProjection(ProjectDocument document)
    {
        var scales = document.Subtitles.ToDictionary(line => line.Id,
            line => SubtitleStyleScaling.GetScaleFactor(line.Style.Scaling, document.Width, document.Height));
        if (scales.Values.All(scale => scale.Equals(1d)))
        {
            return document;
        }
        return document with
        {
            Subtitles = document.Subtitles.Select(line => SubtitleStyleScaling.ToCanvasUnits(line,
                document.Width, document.Height)).ToImmutableArray(),
            Layers = document.Layers.Select(layer => layer.SubtitleId is { } id && !scales[id].Equals(1d)
                ? layer with { Tracks = SubtitleStyleScaling.ScaleDimensions(layer.Tracks, scales[id]) } : layer).ToImmutableArray()
        };
    }

    private static AssStyleDefinition ParseStyle(Dictionary<string, string> row, double sx, double sy, int wrapStyle,
        AssResolutionContext resolution, ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics,
        SubtitleScalingSettings scaling)
    {
        var spacing = Math.Max(AssFormatValues.Number(Get(row, "Spacing", "0")), 0) * sx;
        if (!double.IsFinite(spacing) || spacing is < -4096 or > 4096)
        {
            diagnostics.Add(new("Ass.LetterSpacingRange", $"Resampled letter spacing for ASS style {Required(row, "Name")} exceeds the native range. The letter spacing was ignored and other style properties were preserved."));
            spacing = 0;
        }
        var style = new SubtitleStyle
        {
            Scaling = scaling,
            FontFamily = Required(row, "Fontname"), FontSize = AssFormatValues.Number(Required(row, "Fontsize")) * sy,
            LetterSpacing = spacing, WrapMode = wrapStyle == 2 ? SubtitleWrapMode.NO_WRAP : SubtitleWrapMode.NATURAL,
            Fill = AssFormatValues.Color(Required(row, "PrimaryColour")), Stroke = AssFormatValues.Color(Get(row, "OutlineColour", "&H00000000")),
            ShadowColor = AssFormatValues.Color(Get(row, "BackColour", "&H00000000")),
            Bold = AssFormatValues.Integer(Get(row, "Bold", "0")) != 0, Italic = AssFormatValues.Integer(Get(row, "Italic", "0")) != 0,
            Underline = AssFormatValues.Integer(Get(row, "Underline", "0")) != 0, Strikethrough = AssFormatValues.Integer(Get(row, "StrikeOut", "0")) != 0,
            StrokeWidth = resolution.Stroke(Math.Max(AssFormatValues.Number(Get(row, "Outline", "0")), 0)),
            ShadowOffset = resolution.Shadow(Math.Max(AssFormatValues.Number(Get(row, "Shadow", "0")), 0)),
            ShadowBlur = 0, Alignment = AssFormatValues.Alignment(AssFormatValues.Integer(Get(row, "Alignment", "2"))),
            Margins = new(
                AssFormatValues.Number(Get(row, "MarginL", Get(row, "MarginV", "0"))) * sx,
                AssFormatValues.Number(Get(row, "MarginR", Get(row, "MarginV", "0"))) * sx,
                AssFormatValues.Number(Get(row, "MarginV", "0")) * sy)
        };
        if (resolution.ApproximatesStroke && style.StrokeWidth > 0)
        {
            diagnostics.Add(new("Ass.BorderResampling", "ASS stroke has different horizontal and vertical resampling ratios. It was approximated as a single native stroke width using their geometric mean."));
        }
        ProjectValidator.ValidateSubtitleStyle(style);
        return new(Required(row, "Name"), style, AssFormatValues.Color(Get(row, "SecondaryColour", "&H000000FF")))
        {
            Scale = new(Math.Max(AssFormatValues.Number(Get(row, "ScaleX", "100")), 0) / 100,
                Math.Max(AssFormatValues.Number(Get(row, "ScaleY", "100")), 0) / 100),
            Rotation = AssFormatValues.Number(Get(row, "Angle", "0"))
        };
    }

    private static string[] ParseFormat(string value)
    {
        var format = value.Split(',', StringSplitOptions.TrimEntries);
        if (format.Length == 0 || format.Any(string.IsNullOrEmpty) || format.Distinct(StringComparer.OrdinalIgnoreCase).Count() != format.Length)
        {
            throw new InvalidDataException("ASS Format contains duplicate or empty fields.");
        }
        return format;
    }

    private static Dictionary<string, string> ParseFields(string value, string[] format)
    {
        var values = value.Split(',', format.Length);
        if (values.Length != format.Length)
        {
            throw new InvalidDataException("ASS field counts do not match.");
        }
        return format.Select((field, index) => (field, Value: field.Equals("Text", StringComparison.OrdinalIgnoreCase) ? values[index] : values[index].Trim()))
            .ToDictionary(pair => pair.field, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static string Required(Dictionary<string, string> fields, string key) => fields.TryGetValue(key, out var value) ? value : throw new InvalidDataException("ASS is missing the " + key + " field.");
    private static string Get(Dictionary<string, string> fields, string key, string fallback) => fields.TryGetValue(key, out var value) ? value : fallback;
}
