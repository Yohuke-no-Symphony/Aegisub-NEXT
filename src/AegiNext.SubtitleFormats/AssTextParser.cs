using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal sealed class AssTextParser(SubtitleLine original, IReadOnlyDictionary<string, AssStyleDefinition> styles,
    SceneColor secondary, double scaleX = 1, double scaleY = 1, bool projectSource = false, int canvasWidth = 1920, int canvasHeight = 1080,
    int wrapStyle = 0, double? blurScaleX = null, double? blurScaleY = null, bool blurUsesPlayRes = true,
    AssResolutionContext? resolution = null, bool projectAnimations = false, IAssFontWeightResolver? fontWeightResolver = null,
    SubtitleMargins? dialogueMargins = null, ISubtitlePlacementMeasurer? placementMeasurer = null,
    ISubtitleMaskGeometryCombiner? maskGeometryCombiner = null)
{
    private readonly AssResolutionContext borderResolution = resolution ?? new(scaleX, scaleY);
    private readonly AssMaskParser maskParser = new(original.End - original.Start, scaleX, scaleY, canvasWidth, canvasHeight, maskGeometryCombiner);
    private readonly AssGeometryParser geometryParser = new(original, styles, scaleX, scaleY, !projectSource);
    private readonly AssRotationOriginParser rotationOriginParser = new(original.Id, scaleX, scaleY);
    private readonly AssOpacityParser opacityParser = new(original.End - original.Start, original.Id);
    private readonly AssBorderSourceState borderSource = new(original.Style.StrokeWidth);
    private int borderSourceStart;
    private int borderSourceLength;
    private AssNumericTransformParser numericParser = null!;
    private AssTextAnimationImport textAnimation = null!;
    private readonly StringBuilder text = new();
    private readonly ImmutableArray<AssDrawingRun>.Builder drawings = ImmutableArray.CreateBuilder<AssDrawingRun>();
    private AssDrawingRun? drawingRun;
    private int drawingScale;
    private double drawingBaselineOffset;
    private readonly ImmutableArray<SubtitleInlineSpan>.Builder spans = ImmutableArray.CreateBuilder<SubtitleInlineSpan>();
    private readonly ImmutableArray<SubtitleKaraokeStyleSpan>.Builder karaokeStyles = ImmutableArray.CreateBuilder<SubtitleKaraokeStyleSpan>();
    private readonly ImmutableArray<KaraokeSegment>.Builder karaoke = ImmutableArray.CreateBuilder<KaraokeSegment>();
    private readonly ImmutableArray<AssSourceMapEntry>.Builder map = ImmutableArray.CreateBuilder<AssSourceMapEntry>();
    private readonly ImmutableArray<AssKaraokeSourceMapEntry>.Builder karaokeMap = ImmutableArray.CreateBuilder<AssKaraokeSourceMapEntry>();
    private readonly ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();
    private SubtitleStyle current = original.Style;
    private SubtitleStyle lineStyle = original.Style;
    private SubtitleStyle resetStyle = original.Style;
    private int? requestedFontWeight = original.Style.FontAssetId.HasValue ? null : original.Style.FontVariant?.Weight;
    private int requestedFontWidth = original.Style.FontVariant?.Width ?? 5;
    private bool fontWeightDirty;
    private int fontWeightSourceStart;
    private int fontWeightSourceLength;
    private readonly HashSet<(string Family, int Weight, int Width, bool Italic)> reportedFontWeights = [];
    private readonly HashSet<string> typographyDiagnostics = [];
    private readonly int defaultWrapStyle = wrapStyle;
    private int currentWrapStyle = wrapStyle;
    private double edgeBlur;
    private double? instantEdgeBlur;
    private readonly SceneColor secondaryColor = secondary;
    private SceneColor resetSecondaryColor = secondary;
    private SceneColor? inactive;
    private SubtitleStyle? segmentRunStyle;
    private SceneColor? segmentRunInactive;
    private KaraokeVisualStyleOverride? segmentRunActive;
    private SceneColor segmentHighlightColor;
    private bool segmentRunsReported;
    private KaraokeVisualStyleOverride? instantVisual;
    private MediaTime? instantVisualTime;
    private bool instantNumericOnly;
    private int instantCandidate;
    private int nextCandidate;
    private int instantSourceStart;
    private int instantSourceLength;
    private MediaTime karaokeTime;
    private MediaTime? segmentDuration;
    private KaraokeHighlightKind kind;
    private int segmentStart;
    private int segmentSourceStart;
    private int segmentSourceLength;
    private bool drawing;
    private bool explicitAlignment;
    private bool incompleteGeometry;
    private static readonly string[] knownTags = AssOverrideTags.KnownNames;

    internal AssTextEditResult Parse(string source)
    {
        AssFormatValues.CheckText(source);
        numericParser = new(original, geometryParser.CurrentScale, geometryParser.CurrentRotation, ExternalBlurMaximum);
        textAnimation = new(original, secondaryColor, geometryParser.CurrentScale, geometryParser.CurrentRotation, !projectSource);
        for (var index = 0; index < source.Length;)
        {
            if (source[index] == '{')
            {
                FlushDrawing();
                var end = source.IndexOf('}', index + 1);
                if (end < 0)
                {
                    throw new InvalidDataException("An ASS override block is missing its closing brace.");
                }
                ParseTags(source.AsSpan(index + 1, end - index - 1), index + 1);
                map.Add(new(index, end - index + 1, text.Length, 0));
                index = end + 1;
                continue;
            }
            var count = 1;
            var content = source[index].ToString();
            if (source[index] == '\\' && index + 1 < source.Length && source[index + 1] is 'N' or 'n' or 'h' or '{' or '}')
            {
                count = 2;
                content = source[index + 1] switch
                {
                    'N' => "\n", 'n' => !projectSource && currentWrapStyle == 2 ? "\n" : " ",
                    'h' => "\u00a0", '{' => "{", '}' => "}",
                    _ => source.Substring(index, 2)
                };
                if (source[index + 1] is '{' or '}')
                {
                    Report("Ass.LiteralBraces", "Escaping literal braces is a libass extension; other ASS players may alter the text.", index, count);
                }
            }
            var offset = text.Length;
            if (!drawing)
            {
                if (!projectSource && content.Any(character => character is not ('\r' or '\n')))
                {
                    ResolveFontWeight();
                }
                if ((!projectSource || projectAnimations) && content.Any(character => character is not ('\r' or '\n')))
                {
                    if (!projectSource)
                    {
                        ObserveBorder();
                    }
                    geometryParser.Observe();
                    numericParser.Observe(current, segmentDuration > MediaTime.Zero && instantVisualTime == karaokeTime &&
                        kind != KaraokeHighlightKind.SWEEP ? instantCandidate : 0);
                    textAnimation.Observe(offset, content.Length, segmentDuration > MediaTime.Zero,
                        segmentDuration > MediaTime.Zero && instantVisualTime == karaokeTime &&
                        kind != KaraokeHighlightKind.SWEEP ? instantCandidate : 0);
                }
                if (segmentDuration > MediaTime.Zero)
                {
                    var inactiveColor = inactive ?? secondaryColor;
                    var activeVisual = ResolveInstantVisual();
                    if (text.Length == segmentStart)
                    {
                        segmentHighlightColor = current.Fill;
                    }
                    else if (!projectSource && !segmentRunsReported &&
                        (segmentRunStyle != current || segmentRunInactive != inactiveColor || segmentRunActive != activeVisual))
                    {
                        Report("Ass.KaraokeStyleRuns", "Style changes within the same ASS karaoke group may create separate rendering segments. Full group timing and the appearance of each range were preserved; activation or sweep timing in the source player may differ.", index, count);
                        segmentRunsReported = true;
                    }
                    segmentRunStyle = current;
                    segmentRunInactive = inactiveColor;
                    segmentRunActive = activeVisual;
                    AppendKaraokeStyle(offset, content.Length, (activeVisual ?? new()) with { Fill = current.Fill },
                        new() { Fill = inactiveColor });
                }
                else if (instantVisualTime is not null)
                {
                    if (projectSource && !projectAnimations || !instantNumericOnly)
                    {
                        Report("Ass.UnsupportedTag", "Instantaneous ASS edge and shadow transforms require per-word segment timing. Ordinary text cannot store this animation.", instantSourceStart, instantSourceLength);
                    }
                    instantVisualTime = null;
                    instantVisual = null;
                    instantEdgeBlur = null;
                }
                text.Append(content);
                AppendStyle(offset, content.Length);
            }
            else if (!projectSource)
            {
                if (drawingRun is null)
                {
                    ObserveBorder();
                    var drawingStyle = ResolveExternalBlur(current, edgeBlur);
                    drawingRun = new(index, 0, drawingScale, drawingBaselineOffset, drawingStyle,
                        geometryParser.CurrentScale, geometryParser.CurrentRotation);
                }
                drawingRun = drawingRun with { SourceLength = drawingRun.SourceLength + count };
            }
            map.Add(new(index, count, offset, drawing ? 0 : content.Length));
            index += count;
        }
        FlushDrawing();
        FlushKaraoke();
        if (!projectSource)
        {
            var margins = resetStyle.Margins;
            if (dialogueMargins is { } overrides)
            {
                margins = new(overrides.Left == 0 ? margins.Left : overrides.Left,
                    overrides.Right == 0 ? margins.Right : overrides.Right,
                    overrides.Vertical == 0 ? margins.Vertical : overrides.Vertical);
            }
            lineStyle = lineStyle with
            {
                WrapMode = currentWrapStyle == 2 ? SubtitleWrapMode.NO_WRAP : SubtitleWrapMode.NATURAL,
                Margins = margins
            };
            if (currentWrapStyle is 0 or 3)
            {
                Report("Ass.WrapModeApproximation", "ASS balanced smart wrapping was converted to native natural wrapping. Line-width distribution may differ.", 0, 0);
            }
        }
        var line = original with
        {
            Text = text.ToString(), Style = lineStyle, InlineSpans = spans.ToImmutable(), KaraokeStyleSpans = karaokeStyles.ToImmutable(),
            Karaoke = karaoke.ToImmutable(), InactiveKaraoke = []
        };
        var firstContentTime = line.Karaoke.IsEmpty ? MediaTime.Zero : line.Karaoke.Min(clip => clip.Start);
        var contentOffset = !projectSource && firstContentTime < MediaTime.Zero ? -firstContentTime : MediaTime.Zero;
        if (contentOffset > MediaTime.Zero)
        {
            line = line with { Karaoke = line.Karaoke.Select(clip => clip with { Start = clip.Start + contentOffset, End = clip.End + contentOffset }).ToImmutableArray() };
        }
        var transform = new LayerTransform();
        var placementTracks = projectSource ? [] : geometryParser.Tracks(contentOffset);
        ImmutableArray<AnimationTrack> numericTracks = [];
        if (!projectSource)
        {
            var sourceGeometry = rotationOriginParser.Origin.HasValue
                ? textAnimation.RotationOriginGeometry() with { IsComplete = !incompleteGeometry } : null;
            transform = textAnimation.NormalizeTransform(geometryParser.Transform());
            var appearance = new AssTransformAppearance(transform, original.Id);
            line = appearance.Import(line);
            numericTracks = numericParser.Tracks(transform, contentOffset);
            var textAnimations = textAnimation.Convert(line, numericTracks, transform, contentOffset);
            line = textAnimations.Line;
            numericTracks = textAnimations.Tracks;
            if (rotationOriginParser.Origin is { } origin && sourceGeometry is not null)
            {
                var basis = geometryParser.Placement;
                var reason = string.Empty;
                if (basis is null)
                {
                    if (!textAnimation.TryFixedLayout(line, out var measuredLine))
                    {
                        reason = "automatic placement depends on changing or unpreserved font-size or letter-spacing layout";
                    }
                    else if (placementMeasurer is null)
                    {
                        reason = "automatic placement requires actual font layout measurements";
                    }
                    else
                    {
                        var measurementDocument = new ProjectDocument
                        {
                            Width = canvasWidth, Height = canvasHeight, Subtitles = [measuredLine],
                            Layers = [new()
                            {
                                Kind = LayerKind.SUBTITLE, SubtitleId = measuredLine.Id,
                                Start = measuredLine.Start, End = measuredLine.End
                            }]
                        };
                        basis = placementMeasurer.Measure(measurementDocument, measuredLine).BasePosition;
                    }
                }
                if (reason.Length == 0 && AssRotationOriginConversion.TryImport(origin, basis, line, transform, placementTracks,
                    numericTracks, sourceGeometry, scaleX, scaleY, contentOffset, original.End - original.Start,
                    out var convertedTransform, out var convertedPlacementTracks, out var convertedNumericTracks, out reason))
                {
                    transform = convertedTransform;
                    placementTracks = convertedPlacementTracks;
                    numericTracks = convertedNumericTracks;
                }
                else
                {
                    Report("Ass.RotationOrigin", "The ASS rotation origin was not preserved: " + reason + "; other placement and animation properties were preserved.",
                        rotationOriginParser.SourceStart, rotationOriginParser.SourceLength);
                }
            }
            diagnostics.AddRange(rotationOriginParser.Diagnostics);
            diagnostics.AddRange(geometryParser.Diagnostics.Where(diagnostic => diagnostic.Code != "Ass.InlineTransform"));
            diagnostics.AddRange(appearance.Diagnostics);
            diagnostics.AddRange(numericParser.Diagnostics.Where(diagnostic => diagnostic.Code != "Ass.InlineTransform" &&
                (diagnostic.Code != "Ass.TransformScaleAxes" || !numericTracks.Any(track => track.Property == AnimationProperty.SCALE))));
            diagnostics.AddRange(textAnimation.Diagnostics);
        }
        else if (projectAnimations)
        {
            var textAnimations = textAnimation.Convert(line, [], transform, MediaTime.Zero);
            line = textAnimations.Line;
            numericTracks = textAnimations.Tracks;
            diagnostics.AddRange(textAnimation.Diagnostics);
        }
        ValidateLine(line);
        if (!projectSource)
        {
            AssShadowComposition.AddDiagnostics(line, contentOffset, diagnostics);
        }
        var mask = maskParser.Mask;
        diagnostics.AddRange(maskParser.Diagnostics);
        var opacityTracks = projectSource ? [] : opacityParser.Tracks(contentOffset);
        diagnostics.AddRange(opacityParser.Diagnostics);
        return new(line, diagnostics.ToImmutable(), map.ToImmutable())
        {
            KaraokeSourceMap = karaokeMap.ToImmutable(), Drawings = drawings.ToImmutable(), Mask = mask, ContentOffset = contentOffset,
            Transform = transform, PlacementTracks = placementTracks,
            OpacityTracks = opacityTracks, NumericTracks = numericTracks,
            MaskTracks = projectSource ? maskParser.Tracks() : LayerAnimationTiming.Clip(new ProjectLayer
            {
                Start = original.Start, End = original.End, AnimationOffset = contentOffset,
                Tracks = maskParser.Tracks().Select(track => contentOffset == MediaTime.Zero ? track : track with
                {
                    Keyframes = track.Keyframes.Select(key => key with { Time = key.Time + contentOffset }).ToImmutableArray(),
                    Transforms = track.Transforms.Select(operation => operation with { Start = operation.Start + contentOffset, End = operation.End + contentOffset }).ToImmutableArray()
                }).ToImmutableArray()
            }).Tracks
        };
    }

    private void ParseTags(ReadOnlySpan<char> block, int sourceOffset)
    {
        var cursor = 0;
        while (cursor < block.Length)
        {
            if (block[cursor] != '\\')
            {
                cursor++;
                continue;
            }
            var start = cursor++;
            var valueStart = cursor;
            var depth = 0;
            while (cursor < block.Length)
            {
                if (block[cursor] == '\\' && depth == 0)
                {
                    break;
                }
                depth += block[cursor] == '(' ? 1 : block[cursor] == ')' ? -1 : 0;
                if (depth < 0)
                {
                    throw new InvalidDataException("ASS tag parentheses do not match.");
                }
                cursor++;
            }
            if (depth != 0)
            {
                throw new InvalidDataException("ASS tag parentheses do not match.");
            }
            var token = block[valueStart..cursor].ToString();
            var name = knownTags.FirstOrDefault(tag => token.StartsWith(tag, StringComparison.Ordinal));
            if (name is null)
            {
                Report("Ass.UnsupportedTag", $"Unsupported ASS tag: {token}.", sourceOffset + start, cursor - start);
                continue;
            }
            var value = token[name.Length..].Trim();
            ApplyTag(name, value, sourceOffset + start, cursor - start);
        }
    }

    private void ApplyTag(string name, string value, int sourceStart, int sourceLength)
    {
        var baseline = resetStyle;
        switch (name)
        {
            case "fn":
                var resetFont = value.Length == 0 || !projectSource && value == "0";
                var family = resetFont ? baseline.FontFamily : value;
                if (!projectSource)
                {
                    if (family != current.FontFamily)
                    {
                        requestedFontWidth = resetFont ? baseline.FontVariant?.Width ?? 5 : 5;
                    }
                    current = current with { FontFamily = family, FontAssetId = null, FontVariant = null };
                    RefreshFontWeight(sourceStart, sourceLength);
                    break;
                }
                current = current with
                {
                    FontFamily = family, FontAssetId = null,
                    FontVariant = family == current.FontFamily ? current.FontVariant : null
                };
                break;
            case "fs":
                var fontSize = value.Length == 0 ? baseline.FontSize : value[0] is '+' or '-'
                    ? current.FontSize * (1 + AssFormatValues.Number(value) / 10) : AssFormatValues.Number(value) * scaleY;
                current = current with { FontSize = fontSize <= 0 ? baseline.FontSize : fontSize };
                break;
            case "fsp":
                var spacing = value.Length == 0 ? resetStyle.LetterSpacing : AssFormatValues.Number(value) * scaleX;
                if (!double.IsFinite(spacing) || spacing is < -4096 or > 4096)
                {
                    Report("Ass.LetterSpacingRange", "Resampled ASS letter spacing exceeds the native range. The letter-spacing tag was ignored and other style properties were preserved.", sourceStart, sourceLength);
                }
                else
                {
                    current = current with { LetterSpacing = spacing };
                    if (!projectSource)
                    {
                        numericParser.Set(name, spacing);
                    }
                }
                break;
            case "b":
                if (!projectSource)
                {
                    var resetWeight = !baseline.FontAssetId.HasValue ? baseline.FontVariant?.Weight : null;
                    var requested = value.Length == 0 ? resetWeight ?? (baseline.Bold ? 1 : 0) : AssFormatValues.Integer(value);
                    var resetBold = value.Length == 0 || requested is not (0 or 1) && requested < 100;
                    if (resetBold)
                    {
                        requested = resetWeight ?? (baseline.Bold ? 1 : 0);
                    }
                    var explicitWeight = resetBold ? resetWeight.HasValue : requested >= 100;
                    requestedFontWeight = explicitWeight ? requested : null;
                    fontWeightSourceStart = sourceStart;
                    fontWeightSourceLength = sourceLength;
                    fontWeightDirty = explicitWeight;
                    current = current with
                    {
                        Bold = explicitWeight ? requested >= 600 : requested == 1,
                        FontVariant = null
                    };
                    break;
                }
                var weight = value.Length == 0 ? (baseline.Bold ? 1 : 0) : AssFormatValues.Integer(value);
                var bold = weight is -1 or 1 || weight >= 600;
                current = current with { Bold = bold, FontVariant = bold == current.Bold ? current.FontVariant : null };
                if (weight is not (-1 or 0 or 1))
                {
                    Report("Ass.FontWeight", "Explicit ASS font weight was converted to regular or bold.", sourceStart, sourceLength);
                }
                break;
            case "i":
                var italic = EmphasisFlag(value, baseline.Italic);
                if (!projectSource)
                {
                    current = current with { Italic = italic, FontVariant = null };
                    RefreshFontWeight(sourceStart, sourceLength);
                    break;
                }
                current = current with { Italic = italic, FontVariant = italic == current.Italic ? current.FontVariant : null };
                break;
            case "u":
                current = current with { Underline = EmphasisFlag(value, baseline.Underline) };
                break;
            case "s":
                current = current with { Strikethrough = EmphasisFlag(value, baseline.Strikethrough) };
                break;
            case "c":
            case "1c": current = current with { Fill = value.Length == 0 ? baseline.Fill with { Alpha = current.Fill.Alpha } : AssFormatValues.Color(value, current.Fill) }; break;
            case "2c": inactive = value.Length == 0 ? resetSecondaryColor with { Alpha = (inactive ?? secondaryColor).Alpha } : AssFormatValues.Color(value, inactive ?? secondaryColor); break;
            case "3c": current = current with { Stroke = value.Length == 0 ? baseline.Stroke with { Alpha = current.Stroke.Alpha } : AssFormatValues.Color(value, current.Stroke) }; break;
            case "4c": current = current with { ShadowColor = value.Length == 0 ? baseline.ShadowColor with { Alpha = current.ShadowColor.Alpha } : AssFormatValues.Color(value, current.ShadowColor) }; break;
            case "alpha":
                var alpha = value.Length == 0 ? baseline.Fill.Alpha : AssFormatValues.Alpha(value);
                current = current with
                {
                    Fill = current.Fill with { Alpha = alpha },
                    Stroke = current.Stroke with { Alpha = value.Length == 0 ? baseline.Stroke.Alpha : alpha },
                    ShadowColor = current.ShadowColor with { Alpha = value.Length == 0 ? baseline.ShadowColor.Alpha : alpha }
                };
                inactive = (inactive ?? secondaryColor) with { Alpha = value.Length == 0 ? resetSecondaryColor.Alpha : alpha };
                break;
            case "1a": current = current with { Fill = current.Fill with { Alpha = value.Length == 0 ? baseline.Fill.Alpha : AssFormatValues.Alpha(value) } }; break;
            case "2a": inactive = (inactive ?? secondaryColor) with { Alpha = value.Length == 0 ? resetSecondaryColor.Alpha : AssFormatValues.Alpha(value) }; break;
            case "3a": current = current with { Stroke = current.Stroke with { Alpha = value.Length == 0 ? baseline.Stroke.Alpha : AssFormatValues.Alpha(value) } }; break;
            case "4a": current = current with { ShadowColor = current.ShadowColor with { Alpha = value.Length == 0 ? baseline.ShadowColor.Alpha : AssFormatValues.Alpha(value) } }; break;
            case "bord":
                current = current with { StrokeWidth = value.Length == 0 ? baseline.StrokeWidth : BorderWidth(AssFormatValues.Number(value)) };
                if (!projectSource)
                {
                    borderSource.Set(name, current.StrokeWidth);
                    numericParser.Set(name, current.StrokeWidth);
                }
                if (!projectSource && instantVisual is not null)
                {
                    instantVisual = instantVisual with { StrokeWidth = null };
                    DiscardEmptyInstantVisual();
                }
                break;
            case "xbord":
            case "ybord":
                if (projectSource)
                {
                    Report("Ass.UnsupportedTag", $"Project ASS code does not support {name}. Adjust the native stroke width instead.", sourceStart, sourceLength);
                }
                else
                {
                    var border = value.Length == 0 ? baseline.StrokeWidth : BorderWidth(AssFormatValues.Number(value));
                    borderSource.Set(name, border);
                    borderSourceStart = sourceStart;
                    borderSourceLength = sourceLength;
                    if (instantVisual is not null)
                    {
                        instantVisual = instantVisual with { StrokeWidth = null };
                        DiscardEmptyInstantVisual();
                    }
                }
                break;
            case "shad":
                current = current with { ShadowOffset = value.Length == 0 ? baseline.ShadowOffset : borderResolution.Shadow(Math.Max(AssFormatValues.Number(value), 0)) }; break;
            case "xshad": current = current with { ShadowOffset = current.ShadowOffset with { X = value.Length == 0 ? baseline.ShadowOffset.X : AssFormatValues.Number(value) * borderResolution.BorderScaleX } }; break;
            case "yshad": current = current with { ShadowOffset = current.ShadowOffset with { Y = value.Length == 0 ? baseline.ShadowOffset.Y : AssFormatValues.Number(value) * borderResolution.BorderScaleY } }; break;
            case "blur":
                if (!projectSource)
                {
                    if (TryExternalBlur(value, sourceStart, sourceLength, out var blur))
                    {
                        edgeBlur = blur;
                        numericParser.Set(name, blur);
                        instantEdgeBlur = null;
                        DiscardEmptyInstantVisual();
                    }
                    break;
                }
                current = current with { ShadowBlur = value.Length == 0 ? baseline.ShadowBlur : AssFormatValues.Number(value) * scaleY };
                if (current.ShadowBlur > 0 && (current.Fill.Alpha > 0 || current.StrokeWidth > 0 && current.Stroke.Alpha > 0 || current.ShadowColor.Alpha > 0))
                {
                    Report("Ass.ShadowBlur", "ASS blur affects text or stroke edges. Converting it to project shadow blur changes the edge appearance.", sourceStart, sourceLength);
                }
                break;
            case "r":
                current = value.Length == 0 ? original.Style : styles.TryGetValue(value, out var style) ? style.Style : original.Style;
                resetStyle = current;
                if (!projectSource)
                {
                    borderSource.Set("bord", current.StrokeWidth);
                    requestedFontWeight = current.FontAssetId.HasValue ? null : current.FontVariant?.Weight;
                    requestedFontWidth = current.FontVariant?.Width ?? 5;
                    fontWeightDirty = false;
                    fontWeightSourceStart = 0;
                    fontWeightSourceLength = 0;
                }
                inactive = value.Length == 0 ? secondaryColor : styles.TryGetValue(value, out var reset) ? reset.Secondary : secondaryColor;
                resetSecondaryColor = inactive.Value;
                instantVisual = null;
                instantVisualTime = null;
                instantEdgeBlur = null;
                edgeBlur = 0;
                if (!projectSource || projectAnimations)
                {
                    geometryParser.Reset(value);
                    numericParser.Reset(current, geometryParser.CurrentScale, geometryParser.CurrentRotation);
                    instantCandidate = 0;
                }
                if (value.Length > 0 && !styles.ContainsKey(value))
                {
                    Report("Ass.UnknownStyle", $"Reset style '{value}' was not found. The current line style was used.", sourceStart, sourceLength);
                }
                break;
            case "q":
                if (projectSource)
                {
                    Report("Ass.UnsupportedTag", "Project ASS code does not support q. Adjust whole-line layout using the native wrapping mode.", sourceStart, sourceLength);
                    break;
                }
                var wrapping = value.Length == 0 ? defaultWrapStyle : AssFormatValues.Integer(value);
                if (wrapping is < 0 or > 3)
                {
                    Report("Ass.WrapStyle", "ASS wrapping mode must be between 0 and 3. The file WrapStyle was restored.", sourceStart, sourceLength);
                    wrapping = defaultWrapStyle;
                }
                currentWrapStyle = wrapping;
                break;
            case "an":
            case "a":
                var alignment = Alignment(name, value, baseline.Alignment);
                if (explicitAlignment)
                {
                    Report("Ass.DuplicatePlacement", "Duplicate ASS alignment tags on the same line were ignored; the first value was used.", sourceStart, sourceLength);
                    break;
                }
                explicitAlignment = true;
                lineStyle = lineStyle with
                {
                    Alignment = alignment,
                    Position = projectSource ? original.Style.Position : lineStyle.Position is { } position
                        ? position with { Pivot = Pivot(alignment) }
                        : null
                };
                break;
            case "fsc":
            case "fscx":
            case "fscy":
            case "frz":
            case "fr":
                if (projectSource && !projectAnimations)
                {
                    Report("Ass.UnsupportedTag", $"Project ASS code does not support {name}. Adjust it using native project transform properties.", sourceStart, sourceLength);
                }
                else
                {
                    geometryParser.Apply(name, value);
                    if (name == "fsc")
                    {
                        numericParser.ResetScale(geometryParser.CurrentScale);
                    }
                    else
                    {
                        numericParser.Set(name, name == "fscx" ? geometryParser.CurrentScale.X :
                            name == "fscy" ? geometryParser.CurrentScale.Y : geometryParser.CurrentRotation);
                    }
                }
                break;
            case "pos":
            case "move":
                if (projectSource)
                {
                    Report(name == "pos" ? "Ass.ProjectPositionUnsupported" : "Ass.UnsupportedTag",
                        $"Project ASS code does not support placement tags. Remove \\{name} and adjust placement using native project position properties.", sourceStart, sourceLength);
                    break;
                }
                if (geometryParser.TryPlacement(name, value, sourceStart, sourceLength, out var offset))
                {
                    lineStyle = lineStyle with { Position = new() { Anchor = new(0, 0), Pivot = Pivot(lineStyle.Alignment), Offset = offset } };
                }
                break;
            case "org":
                if (projectSource)
                {
                    Report("Ass.UnsupportedTag", "Project ASS code does not support org. Adjust it using native project pivot properties.", sourceStart, sourceLength);
                }
                else
                {
                    rotationOriginParser.Apply(value, sourceStart, sourceLength);
                }
                break;
            case "kt":
                FlushKaraoke();
                var karaokeStart = value.Length == 0 && !projectSource ? 0 : AssFormatValues.Integer(value);
                AssKaraokeTiming.ValidateCount(karaokeStart);
                karaokeTime = new(karaokeStart, 100);
                if (projectSource && karaokeTime < MediaTime.Zero)
                {
                    throw new InvalidDataException("Karaoke times in project advanced code cannot be negative.");
                }
                break;
            case "k":
            case "K":
            case "kf":
            case "ko":
                FlushKaraoke();
                var centiseconds = value.Length == 0 && !projectSource ? 100 : AssFormatValues.Integer(value);
                if (centiseconds < 0)
                {
                    throw new InvalidDataException("ASS karaoke durations cannot be negative.");
                }
                AssKaraokeTiming.ValidateCount(centiseconds);
                segmentDuration = new(centiseconds, 100);
                segmentStart = text.Length;
                segmentSourceStart = sourceStart;
                segmentSourceLength = sourceLength;
                kind = name == "k" ? KaraokeHighlightKind.STEP : name == "ko" ? KaraokeHighlightKind.OUTLINE_STEP : KaraokeHighlightKind.SWEEP;
                break;
            case "clip":
            case "iclip":
                maskParser.Apply(name, value, original.Id, sourceStart, sourceLength);
                break;
            case "fad":
            case "fade":
                if (projectSource)
                {
                    Report("Ass.UnsupportedTag", $"Project ASS code does not support {name}. Adjust fades using native opacity properties or the timeline.", sourceStart, sourceLength);
                }
                else
                {
                    opacityParser.Apply(name, value, sourceStart, sourceLength);
                }
                break;
            case "t":
                if (!projectSource)
                {
                    rotationOriginParser.CollectTransform(value, sourceStart, sourceLength);
                }
                if (!projectSource || projectAnimations)
                {
                    ParseNumericTransform(value, sourceStart, sourceLength);
                }
                else if (!maskParser.TryTransform(value, original.Id, sourceStart, sourceLength))
                {
                    ParseInstantTransform(value, sourceStart, sourceLength);
                }
                break;
            case "p":
                drawingScale = AssFormatValues.Integer(value);
                drawing = projectSource ? drawingScale != 0 : drawingScale > 0;
                if (drawing && projectSource)
                {
                    Report("Ass.Drawing", "ASS drawing was not imported. Use project shape layers.", sourceStart, sourceLength);
                }
                break;
            case "pbo":
                if (projectSource)
                {
                    Report("Ass.UnsupportedTag", "Project ASS code does not support drawing baseline offsets. Adjust native shape placement instead.", sourceStart, sourceLength);
                }
                else
                {
                    drawingBaselineOffset = value.Length == 0 ? 0 : AssFormatValues.Integer(value);
                }
                break;
            default:
                Report("Ass.UnsupportedTag", name == "move" ? "ASS move is unsupported. Use project position effects." : $"ASS tag {name} was not imported.", sourceStart, sourceLength);
                break;
        }
        if (!projectSource || projectAnimations)
        {
            textAnimation.Set(name, current, inactive ?? secondaryColor, geometryParser.CurrentScale,
                geometryParser.CurrentRotation, edgeBlur);
        }
    }

    private void FlushDrawing()
    {
        if (drawingRun is not null)
        {
            drawings.Add(drawingRun);
            drawingRun = null;
        }
    }

    private void ObserveBorder()
    {
        if (!borderSource.Dirty)
        {
            return;
        }
        if (borderSource.IsShared && borderSource.IsStatic)
        {
            SynchronizeStaticBorder();
        }
        else if (!borderSource.IsShared)
        {
            numericParser.Set("bord", current.StrokeWidth);
            textAnimation.Set("bord", current, inactive ?? secondaryColor, geometryParser.CurrentScale,
                geometryParser.CurrentRotation, edgeBlur);
            Report("Ass.BorderAxes", "Independent ASS horizontal and vertical stroke values or animations cannot share one native stroke width. The previous native width was preserved and conflicting stroke animation was omitted.",
                borderSourceStart, borderSourceLength);
        }
        borderSource.Observe();
    }

    private void SynchronizeStaticBorder()
    {
        if (!borderSource.Dirty || !borderSource.IsShared || !borderSource.IsStatic)
        {
            return;
        }
        current = current with { StrokeWidth = borderSource.Width };
        numericParser.Set("bord", current.StrokeWidth);
        textAnimation.Set("bord", current, inactive ?? secondaryColor, geometryParser.CurrentScale,
            geometryParser.CurrentRotation, edgeBlur);
    }

    private bool ApplyImmediateScaleResets(IEnumerable<AssOverrideTag> tags, int sourceStart, int sourceLength)
    {
        if (projectSource)
        {
            return false;
        }
        var onlyResets = true;
        var pending = new Stack<AssOverrideTag>(tags.Reverse());
        while (pending.TryPop(out var tag))
        {
            if (tag.Name == "fsc")
            {
                ApplyTag(tag.Name, tag.Value, sourceStart, sourceLength);
            }
            else if (tag.Name == "t")
            {
                var arguments = AssOverrideTags.Arguments(tag.Value);
                if (arguments.Length is >= 1 and <= 4 && arguments[^1].StartsWith('\\'))
                {
                    foreach (var child in AssOverrideTags.Parse(arguments[^1]).Reverse())
                    {
                        pending.Push(child);
                    }
                }
                else
                {
                    onlyResets = false;
                }
            }
            else
            {
                onlyResets = false;
            }
        }
        return onlyResets;
    }

    private void FlushKaraoke()
    {
        if (segmentDuration is not { } duration)
        {
            return;
        }
        AssKaraokeTiming.ValidateClock(karaokeTime);
        AssKaraokeTiming.ValidateClock(karaokeTime + duration);
        if (text.Length > segmentStart && duration > MediaTime.Zero)
        {
            karaokeMap.Add(new(segmentSourceStart, segmentSourceLength, karaoke.Count));
            karaoke.Add(new(segmentStart, text.Length - segmentStart, karaokeTime, karaokeTime + duration, segmentHighlightColor)
            { HighlightKind = kind });
        }
        else if (text.Length > segmentStart && !projectSource)
        {
            Report("Ass.ZeroKaraoke", "Zero-duration karaoke text was imported as ordinary text.", 0, 0);
        }
        karaokeTime += duration;
        segmentDuration = null;
        segmentRunStyle = null;
        segmentRunInactive = null;
        segmentRunActive = null;
        segmentRunsReported = false;
    }

    private KaraokeVisualStyleOverride? ResolveInstantVisual()
    {
        if (instantVisualTime is null)
        {
            return null;
        }
        if (instantVisualTime != karaokeTime || kind == KaraokeHighlightKind.SWEEP)
        {
            if (projectSource && !projectAnimations || !instantNumericOnly)
            {
                Report("Ass.UnsupportedTag", "Instantaneous ASS edge and shadow transforms must align with the start of a per-word or outline-per-word segment.", instantSourceStart, instantSourceLength);
            }
            return null;
        }
        if (projectSource)
        {
            return instantVisual;
        }
        var resolved = ResolveExternalBlur(instantVisual!.ApplyTo(current), instantEdgeBlur ?? edgeBlur);
        return instantVisual with { FillBlur = resolved.FillBlur, StrokeBlur = resolved.StrokeBlur, ShadowBlur = resolved.ShadowBlur };
    }

    private void ParseNumericTransform(string value, int sourceStart, int sourceLength)
    {
        if (!projectSource)
        {
            SynchronizeStaticBorder();
        }
        var arguments = AssOverrideTags.Arguments(value);
        var tags = AssOverrideTags.Parse(arguments[^1]).ToArray();
        if (!projectSource)
        {
            tags = tags.Where(tag => tag.Name != "org" &&
                !(tag.Name == "t" && AssRotationOriginParser.ContainsOnlyOrigins(tag.Value))).ToArray();
            if (tags.Length == 0)
            {
                return;
            }
        }
        if (tags.Length > 0 && tags.All(tag => tag.Name is "clip" or "iclip"))
        {
            maskParser.TryTransform(value, original.Id, sourceStart, sourceLength);
            return;
        }
        AssTransformTiming timing;
        try
        {
            timing = AssTransformTiming.Parse(arguments, original.End - original.Start);
        }
        catch (InvalidDataException)
        {
            ApplyImmediateScaleResets(tags, sourceStart, sourceLength);
            incompleteGeometry |= !projectSource && AssRotationOriginGeometry.ContainsGeometry(tags);
            Report("Ass.TransformTiming", "ASS numeric transform times or arguments are invalid. The transform was discarded and other content was preserved.", sourceStart, sourceLength);
            return;
        }
        if (timing.End < timing.Start || !double.IsFinite(timing.Acceleration) || timing.Acceleration < 0)
        {
            ApplyImmediateScaleResets(tags, sourceStart, sourceLength);
            incompleteGeometry |= !projectSource && AssRotationOriginGeometry.ContainsGeometry(tags);
            Report("Ass.TransformTiming", "ASS reversed times or negative acceleration cannot be converted exactly to finite native animation. The transform was discarded.", sourceStart, sourceLength);
            return;
        }
        var clips = tags.Where(tag => tag.Name is "clip" or "iclip").ToArray();
        if (clips.Length > 0)
        {
            var maskArguments = arguments[..^1].Append(string.Concat(clips.Select(tag => "\\" + tag.Name + tag.Value)));
            maskParser.TryTransform("(" + string.Join(',', maskArguments) + ")", original.Id, sourceStart, sourceLength);
        }
        var visualTags = tags.Where(tag => tag.Name is "bord" or "blur" or "3c" or "3a" or "4c" or "4a" or "shad" or "xshad" or "yshad").ToArray();
        var candidate = 0;
        var needsShadOperation = visualTags.Any(tag => tag.Name == "shad") &&
            (current.ShadowOffset.X < 0 || current.ShadowOffset.Y < 0 || textAnimation.HasShadowAnimation);
        if (timing.Start != MediaTime.Zero && timing.Start == timing.End && instantVisual is null &&
            visualTags.Length > 0 && visualTags.All(tag => tag.Value.Length > 0) && !needsShadOperation)
        {
            var milliseconds = checked(timing.Start.Numerator * 1000 / timing.Start.Denominator);
            ParseInstantTransform("(" + milliseconds.ToString(CultureInfo.InvariantCulture) + "," +
                milliseconds.ToString(CultureInfo.InvariantCulture) + "," +
                string.Concat(visualTags.Select(tag => "\\" + tag.Name + tag.Value)) + ")", sourceStart, sourceLength);
            if (instantVisual is not null)
            {
                instantCandidate = candidate = ++nextCandidate;
                instantNumericOnly = true;
            }
        }
        foreach (var tag in tags)
        {
            if (tag.Name is "clip" or "iclip")
            {
                continue;
            }
            if (tag.Name == "fsc")
            {
                ApplyTag(tag.Name, tag.Value, sourceStart, sourceLength);
                continue;
            }
            if (tag.Name == "t" && ApplyImmediateScaleResets([tag], sourceStart, sourceLength))
            {
                continue;
            }
            if (!projectSource && tag.Name is "xbord" or "ybord")
            {
                if (tag.Value.Length == 0)
                {
                    ApplyTag(tag.Name, tag.Value, sourceStart, sourceLength);
                }
                else
                {
                    borderSource.Animate(tag.Name);
                    borderSourceStart = sourceStart;
                    borderSourceLength = sourceLength;
                    Report("Ass.UnsupportedTag", $"Independent ASS transform tag {tag.Name} cannot yet be converted to native stroke animation.", sourceStart, sourceLength);
                }
                continue;
            }
            if (tag.Value.Length == 0 && tag.Name is "fs" or "shad" or "xshad" or "yshad" or "c" or
                "1c" or "2c" or "3c" or "4c" or "alpha" or "1a" or "2a" or "3a" or "4a")
            {
                ApplyTag(tag.Name, string.Empty, sourceStart, sourceLength);
                continue;
            }
            if (tag.Value.Length > 0 && textAnimation.AddStyle(tag.Name, tag.Value, timing, scaleY,
                borderResolution, resetStyle.FontSize, visualTags.Contains(tag) ? candidate : 0))
            {
                continue;
            }
            if (tag.Name is not ("fsp" or "bord" or "blur" or "fscx" or "fscy" or "frz" or "fr"))
            {
                incompleteGeometry |= !projectSource && AssRotationOriginGeometry.ContainsGeometry([tag]);
                if (candidate == 0 || !visualTags.Contains(tag))
                {
                    Report("Ass.UnsupportedTag", $"ASS transform tag {tag.Name} cannot yet be converted to native animation. Other convertible properties were preserved.", sourceStart, sourceLength);
                }
                continue;
            }
            if (tag.Value.Length == 0)
            {
                ApplyTag(tag.Name, string.Empty, sourceStart, sourceLength);
                continue;
            }
            double target;
            if (tag.Name == "blur")
            {
                if (projectSource)
                {
                    target = AssFormatValues.Number(tag.Value) * ExternalBlurScale;
                }
                else if (!TryExternalBlur(tag.Value, sourceStart, sourceLength, out target, true))
                {
                    continue;
                }
                if (!projectSource)
                {
                    ResolveExternalBlur(current, target);
                }
            }
            else
            {
                target = AssFormatValues.Number(tag.Value) * (tag.Name switch
                {
                    "fsp" => scaleX,
                    "bord" => borderResolution.StrokeScale,
                    "fscx" or "fscy" => 0.01,
                    _ => 1
                });
                if (tag.Name == "bord")
                {
                    if (!projectSource)
                    {
                        borderSource.Animate(tag.Name);
                    }
                    ReportBorderApproximation(target);
                }
            }
            numericParser.Add(tag.Name, timing, target, tag.Name is "bord" or "blur" ? candidate : 0);
            textAnimation.AddNumeric(tag.Name, timing, target, tag.Name is "bord" or "blur" ? candidate : 0,
                tag.Name == "blur" ? ExternalBlurMaximum : null);
        }
    }

    private void ParseInstantTransform(string value, int sourceStart, int sourceLength)
    {
        if (!value.StartsWith('(') || !value.EndsWith(')'))
        {
            throw new InvalidDataException("ASS t requires parenthesized arguments.");
        }
        var parts = value[1..^1].Split(',', 3);
        if (parts.Length != 3 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var start) ||
            !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var end) || start == 0 || start != end ||
            !parts[2].TrimStart().StartsWith('\\') || instantVisual is not null)
        {
            Report("Ass.UnsupportedTag", "Only one instantaneous ASS edge and shadow transform at a nonzero time aligned with the karaoke group start is supported.", sourceStart, sourceLength);
            return;
        }
        var visual = new KaraokeVisualStyleOverride();
        double? targetBlur = null;
        foreach (var token in parts[2].Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = knownTags.FirstOrDefault(tag => token.StartsWith(tag, StringComparison.Ordinal));
            var argument = name is null ? string.Empty : token[name.Length..].Trim();
            if (name == "blur" && !projectSource)
            {
                if (TryExternalBlur(argument, sourceStart, sourceLength, out var blur))
                {
                    targetBlur = blur;
                }
                continue;
            }
            var target = visual.ApplyTo(current);
            visual = name switch
            {
                "3c" when argument.Length > 0 => visual with { Stroke = AssFormatValues.Color(argument, target.Stroke) },
                "3a" when argument.Length > 0 => visual with { Stroke = target.Stroke with { Alpha = AssFormatValues.Alpha(argument) } },
                "4c" when argument.Length > 0 => visual with { ShadowColor = AssFormatValues.Color(argument, target.ShadowColor) },
                "4a" when argument.Length > 0 => visual with { ShadowColor = target.ShadowColor with { Alpha = AssFormatValues.Alpha(argument) } },
                "bord" when argument.Length > 0 => visual with { StrokeWidth = BorderWidth(AssFormatValues.Number(argument)) },
                "shad" when argument.Length > 0 => visual with { ShadowOffset = borderResolution.Shadow(Math.Max(AssFormatValues.Number(argument), 0)) },
                "xshad" when argument.Length > 0 => visual with { ShadowOffset = target.ShadowOffset with { X = AssFormatValues.Number(argument) * borderResolution.BorderScaleX } },
                "yshad" when argument.Length > 0 => visual with { ShadowOffset = target.ShadowOffset with { Y = AssFormatValues.Number(argument) * borderResolution.BorderScaleY } },
                "blur" when argument.Length > 0 => visual with { ShadowBlur = AssFormatValues.Number(argument) * scaleY },
                _ => visual
            };
            if (name is not ("3c" or "3a" or "4c" or "4a" or "bord" or "shad" or "xshad" or "yshad" or "blur") || argument.Length == 0)
            {
                Report("Ass.UnsupportedTag", "Instantaneous ASS transforms can only contain supported edge and shadow tags.", sourceStart, sourceLength);
                return;
            }
        }
        if (!visual.HasOverrides && !targetBlur.HasValue)
        {
            return;
        }
        instantVisual = visual;
        instantEdgeBlur = targetBlur;
        instantVisualTime = new(start, 1000);
        instantSourceStart = sourceStart;
        instantSourceLength = sourceLength;
    }

    private double BorderWidth(double value)
    {
        var width = borderResolution.Stroke(projectSource ? value : Math.Max(value, 0));
        ReportBorderApproximation(width);
        return width;
    }

    private void ReportBorderApproximation(double width)
    {
        if (!projectSource && borderResolution.ApproximatesStroke && width > 0)
        {
            ReportTypographyOnce("Ass.BorderResampling", "ASS stroke has different horizontal and vertical resampling ratios. It was approximated as a single native stroke width using their geometric mean.");
        }
    }

    private double ExternalBlurScale => AssBlurConversion.SigmaPerUnit * Math.Sqrt((blurScaleX ?? scaleX) * (blurScaleY ?? scaleY));

    private double ExternalBlurMaximum => AssSourceAnimationEvaluator.MAX_BLUR * ExternalBlurScale;

    private bool TryExternalBlur(string value, int sourceStart, int sourceLength, out double blur, bool interpolate = false)
    {
        var amount = value.Length == 0 ? 0 : AssFormatValues.Number(value);
        blur = (interpolate ? amount : Math.Clamp(amount, 0, AssSourceAnimationEvaluator.MAX_BLUR)) * ExternalBlurScale;
        if (!double.IsFinite(blur))
        {
            Report("Ass.BlurRange", "Converted ASS edge blur exceeds the native range. The blur tag was ignored and other style properties were preserved.", sourceStart, sourceLength);
            return false;
        }
        return true;
    }

    private SubtitleStyle ResolveExternalBlur(SubtitleStyle style, double blur)
    {
        if (blur > 0)
        {
            if (blurUsesPlayRes)
            {
                ReportTypographyOnce("Ass.BlurLayoutResolution", "ASS does not provide complete, valid LayoutRes values. Edge blur was resampled using PlayRes; its appearance may differ if the original video resolution differs.");
            }
            if (!(blurScaleX ?? scaleX).Equals(blurScaleY ?? scaleY))
            {
                ReportTypographyOnce("Ass.BlurResampling", "ASS blur has different horizontal and vertical resampling ratios. It was approximated as native circular blur using their geometric mean.");
            }
            ReportTypographyOnce("Ass.BlurAppearance", "ASS edge blur was converted to native per-channel Gaussian blur. Rasterization, edge compositing, and outlined shadow contours may differ.");
        }
        return style with
        {
            FillBlur = style.StrokeWidth > 0 ? 0 : blur,
            StrokeBlur = style.StrokeWidth > 0 ? blur : 0,
            ShadowBlur = blur
        };
    }

    private void DiscardEmptyInstantVisual()
    {
        if (instantVisual is { HasOverrides: false } && !instantEdgeBlur.HasValue)
        {
            instantVisual = null;
            instantVisualTime = null;
        }
    }

    private void ReportTypographyOnce(string code, string message)
    {
        if (typographyDiagnostics.Add(code))
        {
            Report(code, message, 0, 0);
        }
    }

    private bool EmphasisFlag(string value, bool baseline)
    {
        if (value.Length == 0)
        {
            return baseline;
        }
        var flag = AssFormatValues.Integer(value);
        return projectSource ? flag != 0 : flag is 0 or 1 ? flag == 1 : baseline;
    }

    private TextAlignment Alignment(string name, string value, TextAlignment baseline)
    {
        if (value.Length == 0)
        {
            return baseline;
        }
        var alignment = AssFormatValues.Integer(value);
        if (projectSource)
        {
            return name == "an" ? AssFormatValues.Alignment(alignment) : AssFormatValues.LegacyAlignment(alignment);
        }
        if (name == "an")
        {
            return alignment is >= 1 and <= 9 ? AssFormatValues.Alignment(alignment) : baseline;
        }
        if (alignment is 4 or 8)
        {
            alignment = 5;
        }
        return alignment is 1 or 2 or 3 or 5 or 6 or 7 or 9 or 10 or 11
            ? AssFormatValues.LegacyAlignment(alignment) : baseline;
    }

    private void RefreshFontWeight(int sourceStart, int sourceLength)
    {
        fontWeightDirty = requestedFontWeight.HasValue;
        if (fontWeightDirty && fontWeightSourceLength == 0)
        {
            fontWeightSourceStart = sourceStart;
            fontWeightSourceLength = sourceLength;
        }
    }

    private void ResolveFontWeight()
    {
        if (!fontWeightDirty || requestedFontWeight is not { } weight)
        {
            return;
        }
        fontWeightDirty = false;
        var variant = weight is >= 100 and <= 1000 && !current.FontAssetId.HasValue
            ? fontWeightResolver?.ResolveVariant(current.FontFamily, weight, requestedFontWidth, current.Italic) : null;
        if (variant is { } matched && matched.Weight == weight && matched.Width == requestedFontWidth && matched.Italic == current.Italic)
        {
            var resolved = current with { FontVariant = matched, Bold = weight >= 700 };
            ProjectValidator.ValidateSubtitleStyle(resolved);
            current = resolved;
            return;
        }
        current = current with { FontVariant = null, Bold = weight >= 600 };
        if (reportedFontWeights.Add((current.FontFamily, weight, requestedFontWidth, current.Italic)))
        {
            Report("Ass.FontWeight", "Explicit ASS font weight did not match an actual named font variant and was converted to regular or bold.", fontWeightSourceStart, fontWeightSourceLength);
        }
    }

    private void AppendStyle(int start, int length)
    {
        var visibleStyle = segmentDuration > MediaTime.Zero ? current with { Fill = inactive ?? secondaryColor } : current;
        if (!projectSource)
        {
            visibleStyle = ResolveExternalBlur(visibleStyle, edgeBlur);
        }
        if (visibleStyle == original.Style)
        {
            return;
        }
        var style = SubtitleInlineStyleOverride.FromStyle(visibleStyle);
        if (spans.Count > 0 && spans[^1].Utf16Start + spans[^1].Utf16Length == start && spans[^1].Style == style)
        {
            spans[^1] = spans[^1] with { Utf16Length = spans[^1].Utf16Length + length };
        }
        else
        {
            spans.Add(new(start, length, style));
        }
    }

    private void AppendKaraokeStyle(int start, int length, KaraokeVisualStyleOverride active, KaraokeVisualStyleOverride inactiveStyle)
    {
        if (karaokeStyles.Count > 0 && karaokeStyles[^1].Utf16Start + karaokeStyles[^1].Utf16Length == start &&
            karaokeStyles[^1].ActiveStyle == active && karaokeStyles[^1].InactiveStyle == inactiveStyle)
        {
            karaokeStyles[^1] = karaokeStyles[^1] with { Utf16Length = karaokeStyles[^1].Utf16Length + length };
        }
        else
        {
            karaokeStyles.Add(new(start, length, active, inactiveStyle));
        }
    }

    private void Report(string code, string message, int start, int length) => diagnostics.Add(new(code, message, start, length, original.Id));

    internal static ScenePoint Pivot(TextAlignment alignment)
    {
        var row = Math.DivRem((int)alignment, 3, out var column);
        return new(column / 2.0, row / 2.0);
    }

    internal static void ValidateLine(SubtitleLine line, bool canvasUnits = false)
    {
        var boundaries = line.InlineSpans.IsEmpty && line.Karaoke.IsEmpty && line.KaraokeStyleSpans.IsEmpty && line.AnimationRanges.IsEmpty
            ? null : new SubtitleTextBoundaries(line.Text);
        foreach (var span in line.InlineSpans)
        {
            if (!boundaries!.Contains(span.Utf16Start) || !boundaries.Contains(span.Utf16Start + span.Utf16Length))
            {
                throw new InvalidDataException("ASS local styles cannot split graphemes.");
            }
            ValidateStyle(span.Style.ApplyTo(line.Style), canvasUnits);
        }
        foreach (var segment in line.Karaoke)
        {
            if (!boundaries!.Contains(segment.Utf16Start) || !boundaries.Contains(segment.Utf16Start + segment.Utf16Length))
            {
                throw new InvalidDataException("ASS karaoke tags cannot split graphemes.");
            }
        }
        foreach (var span in line.KaraokeStyleSpans)
        {
            if (!boundaries!.Contains(span.Utf16Start) || !boundaries.Contains(span.Utf16Start + span.Utf16Length))
            {
                throw new InvalidDataException("ASS karaoke appearance cannot split graphemes.");
            }
            if (span.ActiveStyle is { } active)
            {
                ValidateStyle(active.ApplyTo(line.Style), canvasUnits);
            }
            if (span.InactiveStyle is { } inactiveStyle)
            {
                ValidateStyle(inactiveStyle.ApplyTo(line.Style), canvasUnits);
            }
        }
        foreach (var range in line.AnimationRanges)
        {
            if (!boundaries!.Contains(range.Utf16Start) || !boundaries.Contains(range.Utf16Start + range.Utf16Length))
            {
                throw new InvalidDataException("ASS animation ranges cannot split graphemes.");
            }
        }
        ValidateStyle(line.Style, canvasUnits);
    }

    private static void ValidateStyle(SubtitleStyle style, bool canvasUnits)
    {
        if (canvasUnits)
        {
            AssSubtitleOutputValidation.ValidateStyle(style);
        }
        else
        {
            ProjectValidator.ValidateSubtitleStyle(style);
        }
    }
}
