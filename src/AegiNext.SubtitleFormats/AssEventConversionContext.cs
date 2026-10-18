using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal sealed class AssEventConversionContext
{
    private readonly ProjectLayer layer;
    private readonly AssTextAnimationExport textAnimation;
    private readonly SubtitleLine line;
    private readonly ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics;
    private readonly HashSet<AnimationProperty> consumed = [];
    private readonly HashSet<string> reported = [];
    private readonly ScenePoint scale;
    private readonly double rotation;
    private readonly double rotationX;
    private readonly double rotationY;
    private readonly (double Sine, double Cosine) rotationParts;
    private readonly ScenePoint placementOffset;
    private readonly ScenePoint position;
    private readonly AssLinearMove? move;
    private readonly AssRotationOriginExport? rotationOrigin;
    private readonly AssOpacityEnvelope? opacity;
    private readonly bool hasPlacement;
    private readonly bool convertedPath;
    private readonly double? letterSpacing;
    private readonly double? fillBlur;
    private readonly double? strokeBlur;
    private readonly double? strokeWidth;
    private readonly Dictionary<(AnimationProperty Property, int Component), AssNumericAnimation> numeric = [];

    internal AssEventConversionContext(ProjectDocument document, ProjectLayer layer, SubtitleLine line,
        ISubtitlePlacementMeasurer? measurer, ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        layer = AssRotationTracks.Expand(layer, line);
        var originalLayer = layer;
        textAnimation = new(line, layer, diagnostics);
        layer = layer with { Tracks = layer.Tracks.Where(track => !textAnimation.HandlesForExport(track)).ToImmutableArray() };
        this.layer = layer;
        this.line = line;
        this.diagnostics = diagnostics;
        letterSpacing = Constant(AnimationProperty.LETTER_SPACING)?.Scalar;
        fillBlur = Constant(AnimationProperty.FILL_BLUR)?.Scalar;
        strokeBlur = Constant(AnimationProperty.STROKE_BLUR)?.Scalar;
        strokeWidth = Constant(AnimationProperty.STROKE_WIDTH)?.Scalar;
        var alignmentPivot = AssTextParser.Pivot(line.Style.Alignment);
        var stylePosition = line.Style.Position ?? SubtitlePosition.FromAlignment(line.Style.Alignment, line.Style.Margins);
        var automaticPlacement = line.Style.Position is null && layer.Transform == new LayerTransform() &&
            layer.MotionPath is null && !layer.Tracks.Any(track => track.Property is AnimationProperty.POSITION or AnimationProperty.SCALE or
                AnimationProperty.ROTATION or AnimationProperty.ROTATION_X or AnimationProperty.ROTATION_Y);
        var spacingAnimation = Dynamic(AnimationProperty.LETTER_SPACING);
        if (spacingAnimation is not null)
        {
            if (stylePosition.Pivot == alignmentPivot && (automaticPlacement || line.Style.Position is not null || line.Style.WrapMode == SubtitleWrapMode.NO_WRAP))
            {
                AddNumeric(spacingAnimation);
                letterSpacing = spacingAnimation.Initial;
            }
            else
            {
                Report("Ass.TransformPivotAnimation", "Animated letter spacing changes the compensation position for automatic placement or a custom pivot. ASS cannot preserve this translation, so only the letter-spacing animation was omitted.");
            }
        }
        foreach (var property in new[] { AnimationProperty.STROKE_WIDTH, AnimationProperty.FILL_BLUR, AnimationProperty.STROKE_BLUR })
        {
            if (Dynamic(property) is { } animation)
            {
                if (KaraokeOverrides(property))
                {
                    Report("Ass.KaraokeAnimation", "Independent karaoke appearance overrides coexist with whole-line stroke or blur animation. ASS cannot preserve their override order, so only the conflicting whole-line appearance animation was omitted.");
                }
                else
                {
                    AddNumeric(animation);
                }
            }
        }
        var opacityTrack = layer.Tracks.FirstOrDefault(track => track.Property == AnimationProperty.OPACITY);
        opacity = opacityTrack is null ? new(layer.Opacity, []) : AssOpacityConversion.FromTrack(opacityTrack);
        if (opacityTrack is not null)
        {
            if (opacity is null)
            {
                Report("Ass.OpacityAnimation", "ASS fades cannot preserve this multisegment or overlapping opacity animation. The animation was omitted during export.");
            }
            else
            {
                consumed.Add(AnimationProperty.OPACITY);
            }
        }
        if (opacity is { Approximate: true })
        {
            Report("Ass.OpacityApproximation", "ASS fades support only linear changes. Opacity endpoints, delays, and start/end times were preserved, with easing approximated as linear.");
        }
        var transform = layer.Transform;
        scale = Constant(AnimationProperty.SCALE)?.Vector ?? transform.Scale;
        rotation = Constant(AnimationProperty.ROTATION)?.Scalar ?? transform.Rotation;
        rotationX = Constant(AnimationProperty.ROTATION_X)?.Scalar ?? transform.RotationX;
        rotationY = Constant(AnimationProperty.ROTATION_Y)?.Scalar ?? transform.RotationY;
        var distance = Constant(AnimationProperty.PERSPECTIVE_DISTANCE)?.Scalar ?? transform.PerspectiveDistance;
        if (distance != SceneProjection.DEFAULT_DISTANCE || Dynamic(AnimationProperty.PERSPECTIVE_DISTANCE) is not null)
        {
            Report("Ass.PerspectiveDistance", "ASS uses a fixed perspective distance and cannot preserve native distance values or their animation. Export used the ASS distance of 312.5 project pixels; native parameters remain in the project.");
        }
        if (layer.Tracks.Any(track => track.Property == AnimationProperty.PERSPECTIVE_DISTANCE))
        {
            consumed.Add(AnimationProperty.PERSPECTIVE_DISTANCE);
        }
        if (transform.ProjectionTranslation != default)
        {
            Report("Ass.ProjectionTranslation", "ASS cannot preserve native translation in three-dimensional projection space. Projection translation was omitted; the native pivot compensation remains in the project.");
        }
        var scaleXAnimation = Dynamic(AnimationProperty.SCALE, 0);
        var scaleYAnimation = Dynamic(AnimationProperty.SCALE, 1);
        var rotationAnimation = Dynamic(AnimationProperty.ROTATION);
        var rotationXAnimation = Dynamic(AnimationProperty.ROTATION_X);
        var rotationYAnimation = Dynamic(AnimationProperty.ROTATION_Y);
        if (scale.X < 0 || scale.Y < 0)
        {
            Report("Ass.TransformScale", "Negative scaling components were omitted from ASS export; mirrored transforms in the project are unchanged.");
            scale = new(scale.X >= 0 ? scale.X : 1, scale.Y >= 0 ? scale.Y : 1);
        }
        position = transform.Position;
        var positionTrack = layer.Tracks.FirstOrDefault(track => track.Property == AnimationProperty.POSITION);
        if (positionTrack is not null && AssMoveConversion.FromTrack(positionTrack) is { } converted)
        {
            consumed.Add(AnimationProperty.POSITION);
            move = converted;
            position = converted.First;
        }
        if (layer.MotionPath is { } path && (move is null || move.First == move.Last) &&
            !layer.Tracks.Any(track => track.Property == AnimationProperty.PATH_PROGRESS) &&
            AssMoveConversion.FromPath(path, position) is { } pathMove)
        {
            move = pathMove;
            position = pathMove.First;
            convertedPath = true;
        }
        if (move is { Approximate: true })
        {
            Report("Ass.MoveApproximation", "ASS move supports only straight-line movement at constant speed. The path and start/end times were preserved, with speed changes approximated as constant speed.");
        }
        hasPlacement = line.Style.Position is not null || position != default || transform.Pivot != default ||
            scale != new ScenePoint(1, 1) || rotation != 0 || rotationX != 0 || rotationY != 0 || move is not null && move.First != move.Last ||
            scaleXAnimation is not null || scaleYAnimation is not null || rotationAnimation is not null ||
            rotationXAnimation is not null || rotationYAnimation is not null;
        var basis = new ScenePoint(stylePosition.Anchor.X * document.Width + stylePosition.Offset.X,
            stylePosition.Anchor.Y * document.Height + stylePosition.Offset.Y);
        var delta = new ScenePoint(-transform.Pivot.X, -transform.Pivot.Y);
        var needsMeasurement = line.Style.Position is null || stylePosition.Pivot != alignmentPivot;
        if (hasPlacement && needsMeasurement && measurer is not null)
        {
            var measuredLine = letterSpacing is { } spacing ? line with
            {
                Style = line.Style with { LetterSpacing = spacing },
                InlineSpans = line.InlineSpans.Select(span => span with
                {
                    Style = span.Style with { LetterSpacing = spacing }
                }).ToImmutableArray()
            } : line;
            var metrics = measurer.Measure(document, measuredLine);
            basis = metrics.BasePosition;
            delta = new(metrics.BoundsOrigin.X + alignmentPivot.X * metrics.BoundsSize.X - metrics.Pivot.X - transform.Pivot.X,
                metrics.BoundsOrigin.Y + alignmentPivot.Y * metrics.BoundsSize.Y - metrics.Pivot.Y - transform.Pivot.Y);
        }
        else if (hasPlacement && needsMeasurement)
        {
            Report("Ass.PlacementMeasurement", "Font layout measurements were not provided. Placement uses an approximation based on alignment margins and cannot compensate for actual glyph bounds or custom text pivots.");
        }
        if (hasPlacement)
        {
            rotationOrigin = AssRotationOriginExport.Create(originalLayer, line, basis, delta, scale, rotation, rotationX, rotationY,
                position, move, convertedPath, needsMeasurement, measurer is not null, out var originReason);
            if (rotationOrigin is not null)
            {
                AssExportPrecision.AddNumbers(line.Id, diagnostics, rotationOrigin.Origin.X, rotationOrigin.Origin.Y);
            }
            else if (transform.Pivot != default)
            {
                Report("Ass.RotationOrigin", $"This geometry combination cannot produce reversible ASS org compensation ({originReason}). Existing position, pivot, and animation conversion was used.");
                if (rotationX != 0 || rotationY != 0 || rotationXAnimation is not null || rotationYAnimation is not null)
                {
                    Report("Ass.PerspectiveComposition", $"ASS cannot preserve the native perspective pivot together with this placement or scale animation ({originReason}). Convertible axes and placement were exported using an approximate pivot.");
                }
            }
        }
        if (AcceptScale(scaleXAnimation, delta.X, needsMeasurement && measurer is null && stylePosition.Pivot != alignmentPivot))
        {
            scale = scale with { X = scaleXAnimation!.Initial };
        }
        if (AcceptScale(scaleYAnimation, delta.Y, needsMeasurement && measurer is null && stylePosition.Pivot != alignmentPivot))
        {
            scale = scale with { Y = scaleYAnimation!.Initial };
        }
        if (scaleXAnimation is not null && (!numeric.ContainsKey((AnimationProperty.SCALE, 0)) || !numeric.ContainsKey((AnimationProperty.SCALE, 1))))
        {
            consumed.Remove(AnimationProperty.SCALE);
        }
        if (rotationAnimation is not null)
        {
            if (rotationOrigin is not null || rotationAnimation.Operations.IsEmpty || Math.Abs(delta.X * scale.X) < 1e-9 && Math.Abs(delta.Y * scale.Y) < 1e-9 &&
                !(needsMeasurement && measurer is null && stylePosition.Pivot != alignmentPivot))
            {
                AddNumeric(rotationAnimation);
                rotation = rotationAnimation.Initial;
            }
            else
            {
                Report("Ass.TransformPivotAnimation", "Rotation animation requires the compensation position of a custom pivot to change over time. ASS cannot preserve this translation, so only the rotation animation was omitted.");
            }
        }
        var unknownPivot = needsMeasurement && measurer is null && stylePosition.Pivot != alignmentPivot;
        if (AcceptProjectionRotation(rotationXAnimation, delta, unknownPivot))
        {
            rotationX = rotationXAnimation!.Initial;
        }
        if (AcceptProjectionRotation(rotationYAnimation, delta, unknownPivot))
        {
            rotationY = rotationYAnimation!.Initial;
        }
        rotationParts = AssTransformMath.SinCos(rotation);
        if (scale != new ScenePoint(1, 1) || numeric.ContainsKey((AnimationProperty.SCALE, 0)) || numeric.ContainsKey((AnimationProperty.SCALE, 1)))
        {
            Report("Ass.TransformLayout", "ASS applies scaling before automatic wrapping, while the project scales after layout. Wrapping and text bounds may differ for long lines.");
        }
        if (!ExactScale(scale.X) || !ExactScale(scale.Y))
        {
            Report("Ass.NumberPrecision", "Exported ASS scaling percentages use up to 9 decimal places. Some scaling values were approximated.");
        }
        var compensation = TransformVector(delta);
        placementOffset = new(basis.X + compensation.X, basis.Y + compensation.Y);
        if (layer.Tracks.Any(track => !AnimationPropertyMetadata.IsMaskProperty(track.Property) && !consumed.Contains(track.Property)) ||
            layer.MotionPath is not null && !convertedPath || layer.Blend != BlendMode.NORMAL)
        {
            Report("Subtitle.Composition", "Subtitle formats cannot preserve some project compositing or animation. Convertible position, scaling, rotation, and opacity were preserved.");
        }
    }

    internal string GeometryTags => scale == new ScenePoint(1, 1) && rotation == 0 && rotationX == 0 && rotationY == 0 ? string.Empty :
        "\\fscx" + AssFormatValues.Number(scale.X * 100) + "\\fscy" + AssFormatValues.Number(scale.Y * 100) +
        "\\frz" + AssFormatValues.Number(rotation == 0 ? 0 : -rotation) +
        "\\frx" + AssFormatValues.Number(rotationX == 0 ? 0 : -rotationX) +
        "\\fry" + AssFormatValues.Number(rotationY == 0 ? 0 : -rotationY);

    internal string TextAnimationTags(int offset, SubtitleStyle style, bool karaoke, MediaTime origin, SubtitleStyle? sharedStyle = null)
    {
        return textAnimation.Write(offset, style, karaoke, origin, scale, rotation, sharedStyle, rotationX, rotationY);
    }

    internal ImmutableHashSet<AnimationProperty> SharedVisualProperties(int offset, bool karaoke)
    {
        return textAnimation.SharedVisualProperties(offset, karaoke);
    }

    internal SubtitleStyle ApplyTypographyAnimations(SubtitleStyle style)
    {
        return style with
        {
            LetterSpacing = Initial(AnimationProperty.LETTER_SPACING, letterSpacing ?? style.LetterSpacing),
            FillBlur = Initial(AnimationProperty.FILL_BLUR, fillBlur ?? style.FillBlur),
            StrokeBlur = Initial(AnimationProperty.STROKE_BLUR, strokeBlur ?? style.StrokeBlur),
            StrokeWidth = Initial(AnimationProperty.STROKE_WIDTH, strokeWidth ?? style.StrokeWidth)
        };
    }

    internal MediaTime EventOrigin(AssMaskSample sample, MediaTime timeOffset)
    {
        return AssEventClock.Origin(line.Start, sample.Start, layer.AnimationOffset, timeOffset);
    }

    internal string AnimationTags(SubtitleStyle style, MediaTime origin, ImmutableHashSet<AnimationProperty>? sharedVisual = null)
    {
        var result = new System.Text.StringBuilder();
        var appearanceScale = Math.Sqrt(scale.X * scale.Y);
        var hasScale = numeric.ContainsKey((AnimationProperty.SCALE, 0)) || numeric.ContainsKey((AnimationProperty.SCALE, 1));
        var hasRotation = numeric.Keys.Any(key => key.Property is AnimationProperty.ROTATION or AnimationProperty.ROTATION_X or AnimationProperty.ROTATION_Y);
        foreach (var animation in numeric.Values)
        {
            if (sharedVisual?.Contains(animation.Property) == true)
            {
                continue;
            }
            var tag = animation.Property switch
            {
                AnimationProperty.LETTER_SPACING => "\\fsp",
                AnimationProperty.STROKE_WIDTH => "\\bord",
                AnimationProperty.FILL_BLUR when style.StrokeWidth <= 0 => "\\blur",
                AnimationProperty.STROKE_BLUR when style.StrokeWidth > 0 => "\\blur",
                AnimationProperty.SCALE => animation.Component == 0 ? "\\fscx" : "\\fscy",
                AnimationProperty.ROTATION => "\\frz",
                AnimationProperty.ROTATION_X => "\\frx",
                AnimationProperty.ROTATION_Y => "\\fry",
                _ => null
            };
            if (tag is null)
            {
                Report(animation.Property == AnimationProperty.FILL_BLUR ? "Ass.FillBlurAnimation" : "Ass.StrokeBlurAnimation",
                    "The single ASS blur channel cannot preserve independent fill and stroke blur animations for this text at the same time. Only the currently unselected blur animation was omitted.");
                continue;
            }
            var factor = animation.Property switch
            {
                AnimationProperty.SCALE => 100,
                AnimationProperty.ROTATION or AnimationProperty.ROTATION_X or AnimationProperty.ROTATION_Y => -1,
                AnimationProperty.STROKE_WIDTH => appearanceScale,
                AnimationProperty.FILL_BLUR or AnimationProperty.STROKE_BLUR => appearanceScale / AssBlurConversion.SigmaPerUnit,
                _ => 1
            };
            result.Append(animation.Write(tag, factor, origin, line.Id, diagnostics));
        }
        var uniform = UniformScaleAnimation();
        if (uniform is not null && style.StrokeWidth > 0 && sharedVisual?.Contains(AnimationProperty.STROKE_WIDTH) != true &&
            !KaraokeOverrides(AnimationProperty.STROKE_WIDTH) && !numeric.ContainsKey((AnimationProperty.STROKE_WIDTH, 0)))
        {
            result.Append(uniform.Write("\\bord", style.StrokeWidth, origin, line.Id, diagnostics));
        }
        var blurProperty = style.StrokeWidth > 0 ? AnimationProperty.STROKE_BLUR : AnimationProperty.FILL_BLUR;
        if (uniform is not null && AssBlurConversion.Sigma(style) > 0 && sharedVisual?.Contains(blurProperty) != true &&
            !KaraokeOverrides(blurProperty) && !numeric.ContainsKey((blurProperty, 0)))
        {
            result.Append(uniform.Write("\\blur", AssBlurConversion.Sigma(style) / AssBlurConversion.SigmaPerUnit, origin, line.Id, diagnostics));
        }
        if ((hasScale || hasRotation) && (style.ShadowColor.Alpha > 0 && style.ShadowOffset != default ||
            hasScale && (uniform is null || numeric.ContainsKey((AnimationProperty.STROKE_WIDTH, 0)) ||
                numeric.ContainsKey((blurProperty, 0)) || sharedVisual?.Contains(AnimationProperty.STROKE_WIDTH) == true ||
                sharedVisual?.Contains(blurProperty) == true || KaraokeOverrides(AnimationProperty.STROKE_WIDTH) || KaraokeOverrides(blurProperty)) &&
            (style.StrokeWidth > 0 || style.FillBlur > 0 || style.StrokeBlur > 0 || style.ShadowBlur > 0)))
        {
            Report("Ass.TransformAppearanceAnimation", "Scaling or rotation animation was preserved, but ASS cannot retain all transform compensation for independent stroke, blur, or shadow simultaneously. Some appearance values were approximated using the initial transform.");
        }
        if (style.ShadowColor.Alpha > 0 && (numeric.ContainsKey((blurProperty, 0)) && !textAnimation.HasMatchingShadowBlur(blurProperty) ||
            uniform is not null && !style.ShadowBlur.Equals(AssBlurConversion.Sigma(style))))
        {
            Report("Ass.ShadowBlur", "ASS blur animation also changes shadow blur and cannot independently preserve the project shadow blur appearance.");
        }
        if (numeric.TryGetValue((AnimationProperty.STROKE_WIDTH, 0), out var border) &&
            (border.Initial == 0 || border.Operations.Any(operation => operation.Value == 0)) &&
            (border.Initial > 0 || border.Operations.Any(operation => operation.Value > 0)) &&
            (style.FillBlur > 0 || style.StrokeBlur > 0))
        {
            Report("Ass.BlurAnimation", "Stroke animation may switch the channel affected by ASS blur. Independent fill and stroke blur were selected using the initial stroke state.");
        }
        return result.ToString();
    }

    private AssNumericAnimation? UniformScaleAnimation()
    {
        if (numeric.TryGetValue((AnimationProperty.SCALE, 0), out var x) &&
            numeric.TryGetValue((AnimationProperty.SCALE, 1), out var y) && x.Initial.Equals(y.Initial) &&
            x.Operations.SequenceEqual(y.Operations))
        {
            return x;
        }
        return null;
    }

    private double Initial(AnimationProperty property, double fallback)
    {
        return numeric.TryGetValue((property, 0), out var animation) ? animation.Initial : fallback;
    }

    private AssNumericAnimation? Dynamic(AnimationProperty property, int component = 0)
    {
        var track = layer.Tracks.FirstOrDefault(candidate => candidate.Property == property);
        return track is null || consumed.Contains(property) ? null : AssNumericAnimation.FromTrack(track, component);
    }

    private void AddNumeric(AssNumericAnimation animation)
    {
        numeric.Add((animation.Property, animation.Component), animation);
        consumed.Add(animation.Property);
        if (animation.Approximate)
        {
            Report("Ass.TransformCurveApproximation", "ASS numeric transforms cannot directly preserve some easing or trimmed curve phases. Endpoints and times were preserved, with the curve approximated as linear.");
        }
    }

    private bool AcceptScale(AssNumericAnimation? animation, double delta, bool unknownPivot)
    {
        if (animation is null)
        {
            return false;
        }
        if (animation.HasNegative)
        {
            Report("Ass.TransformScale", "ASS does not support negative values in this scaling component. Only this component animation was omitted; zero scaling remains convertible.");
            return false;
        }
        if (!animation.Operations.IsEmpty && (Math.Abs(delta) >= 1e-9 || unknownPivot))
        {
            Report("Ass.TransformPivotAnimation", "Scaling animation requires the compensation position of a custom pivot to change over time. ASS cannot preserve this translation, so only the conflicting scaling component animation was omitted.");
            return false;
        }
        AddNumeric(animation);
        return true;
    }

    private bool AcceptProjectionRotation(AssNumericAnimation? animation, ScenePoint delta, bool unknownPivot)
    {
        if (animation is null)
        {
            return false;
        }
        if (rotationOrigin is null && !animation.Operations.IsEmpty &&
            (Math.Abs(delta.X * scale.X) >= 1e-9 || Math.Abs(delta.Y * scale.Y) >= 1e-9 || unknownPivot))
        {
            Report("Ass.PerspectiveComposition", "Perspective rotation animation requires changing depth and placement compensation for this pivot. The conflicting axis animation was omitted; other convertible properties were preserved.");
            consumed.Add(animation.Property);
            return false;
        }
        AddNumeric(animation);
        return true;
    }

    private bool KaraokeOverrides(AnimationProperty property)
    {
        if (line.KaraokeStyle is not null && !line.Karaoke.IsEmpty)
        {
            return true;
        }
        return line.Karaoke.Any(segment => segment.HighlightKind == KaraokeHighlightKind.OUTLINE_STEP) ||
            line.KaraokeStyleSpans.Any(span => line.Karaoke.Any(segment => span.Utf16Start < segment.Utf16Start + segment.Utf16Length &&
                segment.Utf16Start < span.Utf16Start + span.Utf16Length) && (Overrides(span.ActiveStyle) || Overrides(span.InactiveStyle)));

        bool Overrides(KaraokeVisualStyleOverride? visual)
        {
            return property switch
            {
                AnimationProperty.STROKE_WIDTH => visual?.StrokeWidth.HasValue == true,
                AnimationProperty.FILL_BLUR => visual?.FillBlur.HasValue == true || visual?.StrokeWidth.HasValue == true,
                AnimationProperty.STROKE_BLUR => visual?.StrokeBlur.HasValue == true || visual?.StrokeWidth.HasValue == true,
                _ => false
            };
        }
    }

    internal SubtitleStyle ConvertStyle(SubtitleStyle style)
    {
        if ((Math.IEEERemainder(rotationX, 360) != 0 || Math.IEEERemainder(rotationY, 360) != 0 ||
            numeric.Keys.Any(key => key.Property is AnimationProperty.ROTATION_X or AnimationProperty.ROTATION_Y)) &&
            (style.StrokeWidth > 0 || style.FillBlur > 0 || style.StrokeBlur > 0 ||
                style.ShadowColor.Alpha > 0 && style.ShadowOffset != default))
        {
            Report("Ass.PerspectiveAppearance", "ASS perspective and native layer projection apply edges, blur, and shadow at different stages. Rotation geometry was preserved; projected appearance may differ.");
        }
        if (scale == new ScenePoint(1, 1) && rotation.Equals(0d))
        {
            return style;
        }
        if (!scale.X.Equals(scale.Y) && style.StrokeWidth > 0 && style.Stroke.Alpha > 0)
        {
            Report("Ass.TransformAppearance", "Stroke under nonuniform scaling was approximated using the geometric mean of the two axis scales. Text scaling and shadow direction were preserved.");
        }
        if (!scale.X.Equals(scale.Y) && (style.FillBlur > 0 || style.StrokeBlur > 0 || style.ShadowBlur > 0))
        {
            Report("Ass.TransformAppearance", "Blur under nonuniform scaling was approximated using the geometric mean of the two axis scales. Horizontal and vertical spread cannot both be preserved.");
        }
        AssExportPrecision.AddNumbers(line.Id, diagnostics, -rotation);
        return style with
        {
            StrokeWidth = style.StrokeWidth * Math.Sqrt(scale.X * scale.Y),
            FillBlur = style.FillBlur * Math.Sqrt(scale.X * scale.Y),
            StrokeBlur = style.StrokeBlur * Math.Sqrt(scale.X * scale.Y),
            ShadowBlur = style.ShadowBlur * Math.Sqrt(scale.X * scale.Y),
            ShadowOffset = TransformVector(style.ShadowOffset)
        };
    }

    internal string PlacementTags(AssMaskSample sample, MediaTime timeOffset, AssExportStyle exportStyle)
    {
        var encodedAlignment = AssFormatValues.Alignment(line.Style.Alignment).ToString(CultureInfo.InvariantCulture);
        var alignment = exportStyle.Baseline["an"] == encodedAlignment ? string.Empty : "\\an" + encodedAlignment;
        if (!hasPlacement)
        {
            return alignment.Length == 0 ? string.Empty : "{" + alignment + "}";
        }
        alignment = "{" + alignment;
        if (rotationOrigin is not null)
        {
            alignment += "\\org(" + Point(rotationOrigin.Origin) + ")";
        }
        var origin = new MediaTime((sample.Start + timeOffset).ToTimestamp(new(1, 100), MediaTimeRounding.FLOOR).Value, 100) -
            timeOffset - line.Start + layer.AnimationOffset;
        var end = new MediaTime((sample.End + timeOffset).ToTimestamp(new(1, 100), MediaTimeRounding.CEILING).Value, 100) -
            timeOffset - line.Start + layer.AnimationOffset;
        var startTime = move is null || move.Start < origin ? origin : move.Start;
        var endTime = move is null || move.End > end ? end : move.End;
        if (move is null || move.First == move.Last || startTime >= endTime)
        {
            var point = Place(move?.Evaluate(origin) ?? position);
            AssExportPrecision.AddNumbers(line.Id, diagnostics, point.X, point.Y);
            return alignment + "\\pos(" + Point(point) + ")}";
        }
        var first = Place(move.Evaluate(startTime));
        var last = Place(move.Evaluate(endTime));
        var startMs = Milliseconds(startTime - origin);
        var endMs = Math.Max(startMs + 1, Milliseconds(endTime - origin));
        if (new MediaTime(startMs, 1000) != startTime - origin || new MediaTime(endMs, 1000) != endTime - origin)
        {
            Report("Ass.MoveTimeQuantization", "ASS movement times were rounded to milliseconds; very short movements retain at least 1 millisecond.");
        }
        AssExportPrecision.AddNumbers(line.Id, diagnostics, first.X, first.Y, last.X, last.Y);
        return alignment + "\\move(" + Point(first) + "," + Point(last) + "," +
            startMs.ToString(CultureInfo.InvariantCulture) + "," + endMs.ToString(CultureInfo.InvariantCulture) + ")}";
    }

    internal string OpacityTags(AssMaskSample sample, MediaTime timeOffset)
    {
        if (opacity is null)
        {
            return string.Empty;
        }
        var origin = new MediaTime((sample.Start + timeOffset).ToTimestamp(new(1, 100), MediaTimeRounding.FLOOR).Value, 100) -
            timeOffset - line.Start + layer.AnimationOffset;
        var end = new MediaTime((sample.End + timeOffset).ToTimestamp(new(1, 100), MediaTimeRounding.CEILING).Value, 100) -
            timeOffset - line.Start + layer.AnimationOffset;
        var tags = AssOpacityConversion.WriteTags(opacity, origin, end, line.Id, diagnostics);
        if (tags.Length > 0 && opacity.Clip(origin, end).HasPartialOpacity)
        {
            Report("Ass.OpacityComposition", "ASS fades apply opacity while drawing glyph components, while the project applies it after drawing the whole layer. Areas where fill, stroke, or shadow overlap may differ.");
        }
        return tags.Length == 0 ? string.Empty : "{" + tags + "}";
    }

    private AnimationValue? Constant(AnimationProperty property)
    {
        var track = layer.Tracks.FirstOrDefault(candidate => candidate.Property == property);
        if (track is null || !AssMoveConversion.TryConstant(track, out var value))
        {
            return null;
        }
        consumed.Add(property);
        return value;
    }

    private ScenePoint Place(ScenePoint point)
    {
        if (rotationOrigin is not null)
        {
            return rotationOrigin.MapPosition(point);
        }
        return new(placementOffset.X + point.X, placementOffset.Y + point.Y);
    }

    private ScenePoint TransformVector(ScenePoint point)
    {
        var x = point.X * scale.X;
        var y = point.Y * scale.Y;
        return new(rotationParts.Cosine * x - rotationParts.Sine * y, rotationParts.Sine * x + rotationParts.Cosine * y);
    }

    private void Report(string code, string message)
    {
        if (reported.Add(code))
        {
            diagnostics.Add(new(code, message, SubtitleId: line.Id));
        }
    }

    private static long Milliseconds(MediaTime time) => time.ToTimestamp(new(1, 1000), MediaTimeRounding.TO_EVEN).Value;
    private static string Point(ScenePoint point) => AssFormatValues.Number(point.X) + "," + AssFormatValues.Number(point.Y);

    private static bool ExactScale(double value)
    {
        var serialized = AssFormatValues.Number(AssFormatValues.Number(value * 100));
        return serialized.Equals(value * 100) || (serialized / 100).Equals(value);
    }
}
