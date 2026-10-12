using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class AssKaraokeFillAnimationExportTests
{
    [Fact]
    public void NormalFillFeedsInactiveKaraokeWithoutChangingActiveHighlight()
    {
        var document = WithTracks(Document(), Fill(SceneColor.Black, new(1, 0, 0)));

        var imported = RoundTrip(document);

        AssertSameFill(document, imported, 0, new(1, 2));
        AssertSameFill(document, imported, 0, new(3, 2));
    }

    [Fact]
    public void StaticInactiveFillOverridesNormalFillDuringKaraoke()
    {
        var document = Document();
        var line = document.Subtitles[0] with
        {
            KaraokeStyleSpans = [new(0, 1, InactiveStyle: new() { Fill = new(0, 0, 1) })]
        };
        document = WithTracks(document with { Subtitles = [line] }, Fill(SceneColor.Black, new(1, 0, 0)));

        var imported = RoundTrip(document);

        AssertSameFill(document, imported, 0, new(1, 2));
        AssertSameFill(document, imported, 0, new(3, 2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InactiveFillTrackOverridesNormalAndStaticInactiveFillRegardlessOfTrackOrder(bool normalFirst)
    {
        var document = Document();
        var line = document.Subtitles[0] with
        {
            KaraokeStyleSpans = [new(0, 1, InactiveStyle: new() { Fill = new(1, 1, 0) })]
        };
        var normal = Fill(SceneColor.Black, new(1, 0, 0));
        var inactive = Fill(new(0, 0, 1), new(0, 1, 0), SubtitleAnimationState.INACTIVE);
        document = WithTracks(document with { Subtitles = [line] }, normalFirst ? [normal, inactive] : [inactive, normal]);

        var imported = RoundTrip(document);

        AssertSameFill(document, imported, 0, new(1, 2));
        AssertSameFill(document, imported, 0, new(3, 2));
    }

    [Fact]
    public void ScopedNormalFillOverridesGlobalNormalForInactiveKaraokeOnly()
    {
        var document = Document("ab", 2);
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var line = document.Subtitles[0] with { AnimationRanges = [range] };
        document = WithTracks(document with { Subtitles = [line] },
            Fill(SceneColor.Black, new(1, 0, 0)),
            Fill(new(0, 0, 1), new(0, 1, 0), rangeId: range.Id));

        var imported = RoundTrip(document);

        for (var offset = 0; offset < 2; offset++)
        {
            AssertSameFill(document, imported, offset, new(1, 2));
            AssertSameFill(document, imported, offset, new(3, 2));
        }
    }

    [Fact]
    public void MixedRunsRouteNormalFillByTheirEffectiveKaraokeState()
    {
        var document = WithTracks(Document("ab"), Fill(SceneColor.Black, new(1, 0, 0)));

        var imported = RoundTrip(document);

        for (var offset = 0; offset < 2; offset++)
        {
            AssertSameFill(document, imported, offset, new(1, 2));
            AssertSameFill(document, imported, offset, new(3, 2));
        }
    }

    [Fact]
    public void ScopedInactiveFillOverridesGlobalNormalAndStaticInactiveFill()
    {
        var document = Document("ab", 2);
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var line = document.Subtitles[0] with
        {
            AnimationRanges = [range],
            KaraokeStyleSpans = [new(0, 2, InactiveStyle: new() { Fill = new(1, 1, 0) })]
        };
        document = WithTracks(document with { Subtitles = [line] },
            Fill(SceneColor.Black, new(1, 0, 0)),
            Fill(new(0, 0, 1), new(0, 1, 0), SubtitleAnimationState.INACTIVE, range.Id));

        var imported = RoundTrip(document);

        for (var offset = 0; offset < 2; offset++)
        {
            AssertSameFill(document, imported, offset, new(1, 2));
            AssertSameFill(document, imported, offset, new(3, 2));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void UnrelatedFontEditPreservesNativeNormalFillStateAndIdentity(bool mixedRuns, bool explicitInactive)
    {
        var document = Document(mixedRuns ? "ab" : "a");
        if (explicitInactive)
        {
            var line = document.Subtitles[0] with
            {
                KaraokeStyleSpans = [new(0, 1, InactiveStyle: new() { Fill = new(0, 0, 1) })]
            };
            document = document with { Subtitles = [line] };
        }
        var normal = Fill(SceneColor.Black, new(1, 0, 0));
        document = WithTracks(document, normal, FontSize());

        var changed = EditFont(document);

        Assert.Same(normal, Assert.Single(changed.Layers[0].Tracks, track => track.Property == AnimationProperty.FILL));
        Assert.Equal(SubtitleAnimationState.NORMAL, normal.Target.State);
        Assert.Equal(35, SceneEvaluator.EvaluateScalarTrack(
            Assert.Single(changed.Layers[0].Tracks, track => track.Property == AnimationProperty.FONT_SIZE), new(1)), 9);
        for (var offset = 0; offset < document.Subtitles[0].Text.Length; offset++)
        {
            AssertSameFill(document, changed, offset, new(1, 2));
            AssertSameFill(document, changed, offset, new(3, 2));
        }
    }

    [Fact]
    public void ScopedNormalFillAndItsRangeIdentitySurviveUnrelatedFontEdit()
    {
        var document = Document("ab", 2);
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var line = document.Subtitles[0] with { AnimationRanges = [range] };
        var normal = Fill(SceneColor.Black, new(1, 0, 0));
        var scoped = Fill(new(0, 0, 1), new(0, 1, 0), rangeId: range.Id);
        document = WithTracks(document with { Subtitles = [line] }, normal, scoped, FontSize());

        var changed = EditFont(document);

        var fills = changed.Layers[0].Tracks.Where(track => track.Property == AnimationProperty.FILL).ToArray();
        Assert.Equal(2, fills.Length);
        Assert.Same(normal, Assert.Single(fills, track => track.Target.TextRangeId is null));
        Assert.Same(scoped, Assert.Single(fills, track => track.Target.TextRangeId == range.Id));
        Assert.Contains(changed.Subtitles[0].AnimationRanges, candidate => candidate.Id == range.Id &&
            candidate.Utf16Start == range.Utf16Start && candidate.Utf16Length == range.Utf16Length);
        for (var offset = 0; offset < 2; offset++)
        {
            AssertSameFill(document, changed, offset, new(1, 2));
            AssertSameFill(document, changed, offset, new(3, 2));
        }
    }

    [Fact]
    public void AddingScopedGeometryPreservesNativeNormalFillAcrossNewKaraokeRunBoundaries()
    {
        var normal = Fill(SceneColor.Black, new(1, 0, 0));
        var document = WithTracks(Document("abc", 2), normal, FontSize());

        var changed = Edit(document, "ab", @"a{\fscx200}b");

        Assert.Same(normal, Assert.Single(changed.Layers[0].Tracks, track => track.Property == AnimationProperty.FILL));
        Assert.Equal(SubtitleAnimationState.NORMAL, normal.Target.State);
        Assert.Contains(changed.Subtitles[0].AnimationRanges,
            range => range.Utf16Start == 1 && range.Utf16Length == 1 && range.Scale == new ScenePoint(2, 1));
        for (var offset = 0; offset < 3; offset++)
        {
            AssertSameFill(document, changed, offset, new(1, 2));
            AssertSameFill(document, changed, offset, new(3, 2));
        }
    }

    [Fact]
    public void SplittingNativeFillGeometryRangeDoesNotApplyThePreservedGeometryTwice()
    {
        var document = Document("abc", 2);
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2)
        {
            Scale = new(2, 1),
            Pivot = SubtitleAnimationPivot.SUBTITLE_ANCHOR
        };
        var line = document.Subtitles[0] with { AnimationRanges = [range] };
        var normal = Fill(SceneColor.Black, new(1, 0, 0), rangeId: range.Id);
        document = WithTracks(document with { Subtitles = [line] }, normal, FontSize());

        var changed = Edit(document, "ab", @"a{\fscx300}b");

        Assert.Equal(new ScenePoint(2, 1), ScaleAt(changed, 0, new(1)));
        Assert.Equal(new ScenePoint(3, 1), ScaleAt(changed, 1, new(1)));
        Assert.Equal(new ScenePoint(1, 1), ScaleAt(changed, 2, new(1)));
        Assert.Same(normal, Assert.Single(changed.Layers[0].Tracks, track => track.Property == AnimationProperty.FILL));
        Assert.Equal(SubtitleAnimationState.NORMAL, normal.Target.State);
        Assert.Contains(changed.Subtitles[0].AnimationRanges,
            candidate => candidate.Id == range.Id && candidate.Utf16Start == range.Utf16Start &&
                candidate.Utf16Length == range.Utf16Length);
        for (var offset = 0; offset < 3; offset++)
        {
            AssertSameFill(document, changed, offset, new(1, 2));
            AssertSameFill(document, changed, offset, new(3, 2));
        }
    }

    [Fact]
    public void SplittingNativeFillRotationRangeKeepsTheRequestedEffectiveRotation()
    {
        var document = Document("abc", 2);
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2)
        {
            Rotation = 20,
            Pivot = SubtitleAnimationPivot.SUBTITLE_ANCHOR
        };
        var line = document.Subtitles[0] with { AnimationRanges = [range] };
        var normal = Fill(SceneColor.Black, new(1, 0, 0), rangeId: range.Id);
        document = WithTracks(document with { Subtitles = [line] }, normal, FontSize());

        var changed = Edit(document, "ab", @"a{\frz-30}b");

        Assert.Equal(20, RotationAt(changed, 0, new(1)), 9);
        Assert.Equal(30, RotationAt(changed, 1, new(1)), 9);
        Assert.Equal(0, RotationAt(changed, 2, new(1)), 9);
        Assert.Same(normal, Assert.Single(changed.Layers[0].Tracks, track => track.Property == AnimationProperty.FILL));
        Assert.Contains(changed.Subtitles[0].AnimationRanges, candidate => candidate.Id == range.Id);
        for (var offset = 0; offset < 3; offset++)
        {
            AssertSameFill(document, changed, offset, new(1, 2));
            AssertSameFill(document, changed, offset, new(3, 2));
        }
    }

    [Fact]
    public void SplittingAnUnprojectedFillRangePreservesItsNativeMirror()
    {
        var document = Document("abc", 2);
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2)
        {
            Scale = new(-2, 1),
            Pivot = SubtitleAnimationPivot.SUBTITLE_ANCHOR
        };
        var line = document.Subtitles[0] with { AnimationRanges = [range] };
        var normal = Fill(SceneColor.Black, new(1, 0, 0), rangeId: range.Id);
        document = WithTracks(document with { Subtitles = [line] }, normal, FontSize());

        var changed = Edit(document, "ab", @"a{\b1}b");

        Assert.Equal(range, Assert.Single(changed.Subtitles[0].AnimationRanges, candidate => candidate.Id == range.Id));
        Assert.Equal(new ScenePoint(-2, 1), ScaleAt(changed, 0, new(1)));
        Assert.Equal(new ScenePoint(-2, 1), ScaleAt(changed, 1, new(1)));
        Assert.Equal(new ScenePoint(1, 1), ScaleAt(changed, 2, new(1)));
        Assert.Same(normal, Assert.Single(changed.Layers[0].Tracks, track => track.Property == AnimationProperty.FILL));
        for (var offset = 0; offset < 3; offset++)
        {
            AssertSameFill(document, changed, offset, new(1, 2));
            AssertSameFill(document, changed, offset, new(3, 2));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitActiveFillRemainsActiveAlongsideNormalAndInactiveChannels(bool normalFirst)
    {
        var normal = Fill(SceneColor.Black, new(1, 0, 0));
        var inactive = Fill(new(0, 0, 1), new(0, 1, 0), SubtitleAnimationState.INACTIVE);
        var active = Fill(SceneColor.White, new(0, 0, 1), SubtitleAnimationState.ACTIVE);
        var document = WithTracks(Document(), normalFirst ? [normal, inactive, active] : [active, inactive, normal]);

        var imported = RoundTrip(document);

        AssertSameFill(document, imported, 0, new(1, 2));
        AssertSameFill(document, imported, 0, new(3, 2));
    }

    [Fact]
    public void UnrelatedFontEditPreservesAllNativeFillStatesAndIdentities()
    {
        var normal = Fill(SceneColor.Black, new(1, 0, 0));
        var inactive = Fill(new(0, 0, 1), new(0, 1, 0), SubtitleAnimationState.INACTIVE);
        var active = Fill(SceneColor.White, new(0, 0, 1), SubtitleAnimationState.ACTIVE);
        var document = WithTracks(Document(), normal, inactive, active, FontSize());

        var changed = EditFont(document);

        var fills = changed.Layers[0].Tracks.Where(track => track.Property == AnimationProperty.FILL).ToArray();
        Assert.Equal(3, fills.Length);
        foreach (var original in new[] { normal, inactive, active })
        {
            Assert.Same(original, Assert.Single(fills, candidate => candidate.Target == original.Target));
        }
        AssertSameFill(document, changed, 0, new(1, 2));
        AssertSameFill(document, changed, 0, new(3, 2));
    }

    [Theory]
    [InlineData(SubtitleAnimationState.INACTIVE, @"\2c&H00FF00&")]
    [InlineData(SubtitleAnimationState.ACTIVE, @"\1c&H00FF00&")]
    public void EditingAnExplicitFillAnimationStillAppliesTheRequestedColor(SubtitleAnimationState state, string tag)
    {
        var explicitFill = Fill(new(0, 0, 1), new(0, 1, 0), state);
        var document = WithTracks(Document(), Fill(SceneColor.Black, new(1, 0, 0)), explicitFill, FontSize());

        var changed = Edit(document, tag, tag.Replace("00FF00", "FF0000", StringComparison.Ordinal));

        var time = state == SubtitleAnimationState.INACTIVE ? new MediaTime(1, 2) : new(3, 2);
        Assert.Equal(new SceneColor(0, 0, 1), FillAt(changed, 0, time));
    }

    [Fact]
    public void OrdinaryTextNormalFillStillUsesTheOrdinaryChannel()
    {
        var document = Document();
        var line = document.Subtitles[0] with { Karaoke = [] };
        document = WithTracks(document with { Subtitles = [line] }, Fill(SceneColor.Black, new(1, 0, 0)));

        var imported = RoundTrip(document);

        AssertSameFill(document, imported, 0, new(1, 2));
        AssertSameFill(document, imported, 0, new(3, 2));
    }

    private static AnimationTrack Fill(SceneColor initial, SceneColor final,
        SubtitleAnimationState state = SubtitleAnimationState.NORMAL, Guid? rangeId = null)
    {
        return new(new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: rangeId, State: state), [])
        {
            ColorSpace = AnimationColorSpace.SRGB,
            InitialValue = initial,
            Transforms = [new(Guid.NewGuid(), MediaTime.Zero, new(2), final)]
        };
    }

    private static AnimationTrack FontSize()
    {
        return new(AnimationProperty.FONT_SIZE, [])
        {
            InitialValue = 20,
            Transforms = [new(Guid.NewGuid(), MediaTime.Zero, new(2), 40)]
        };
    }

    private static ProjectDocument WithTracks(ProjectDocument document, params AnimationTrack[] tracks)
    {
        return document with { Layers = [document.Layers[0] with { Tracks = tracks.ToImmutableArray() }] };
    }

    private static ProjectDocument RoundTrip(ProjectDocument document)
    {
        ProjectValidator.Validate(document);
        var exported = AssSubtitleFormat.Write(document);
        var imported = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 },
            AssSubtitleFormat.Parse(exported.Text, 640, 360), "ASS");
        ProjectValidator.Validate(imported);
        return imported;
    }

    private static ProjectDocument EditFont(ProjectDocument document)
    {
        return Edit(document, @"\fs40", @"\fs50");
    }

    private static ProjectDocument Edit(ProjectDocument document, string before, string after)
    {
        ProjectValidator.Validate(document);
        var line = document.Subtitles[0];
        var layer = document.Layers[0];
        var projection = AssTextProjection.Create(line, layer: layer);
        Assert.Contains(before, projection.Source, StringComparison.Ordinal);
        var edited = AssTextProjection.Apply(line,
            projection.Source.Replace(before, after, StringComparison.Ordinal), layer: layer);
        var changed = ProjectEditingOperations.ApplyAssTextEdit(document, line.Id, edited);
        ProjectValidator.Validate(changed);
        return changed;
    }

    private static void AssertSameFill(ProjectDocument expected, ProjectDocument actual, int offset, MediaTime time)
    {
        var expectedFill = FillAt(expected, offset, time);
        var actualFill = FillAt(actual, offset, time);
        Assert.Equal(expectedFill.Red, actualFill.Red, 6);
        Assert.Equal(expectedFill.Green, actualFill.Green, 6);
        Assert.Equal(expectedFill.Blue, actualFill.Blue, 6);
        Assert.Equal(expectedFill.Alpha, actualFill.Alpha, 6);
    }

    private static SceneColor FillAt(ProjectDocument document, int offset, MediaTime time)
    {
        var line = document.Subtitles[0];
        var evaluated = SceneEvaluator.EvaluateLayer(document.Layers[0], line, time);
        var style = line.Style;
        foreach (var span in line.InlineSpans)
        {
            if (offset >= span.Utf16Start && offset < span.Utf16Start + span.Utf16Length)
            {
                style = span.Style.ApplyTo(style);
                break;
            }
        }
        var ordinary = SubtitleAnimationEvaluation.ApplyStyleAnimations(evaluated, style, offset,
            SubtitleAnimationState.NORMAL);
        var segment = line.Karaoke.FirstOrDefault(candidate => offset >= candidate.Utf16Start &&
            offset < candidate.Utf16Start + candidate.Utf16Length);
        if (segment is null)
        {
            return ordinary.Fill;
        }
        var active = time >= segment.Start;
        var rangeStyle = KaraokeVisualStyleResolver.RangeStyleAt(line, offset,
            active ? KaraokeVisualState.ACTIVE : KaraokeVisualState.INACTIVE);
        var visual = active
            ? KaraokeVisualStyleResolver.ResolveActive(ordinary, line.KaraokeStyle, segment, rangeStyle)
            : KaraokeVisualStyleResolver.ResolveInactive(ordinary, segment, rangeStyle);
        return SubtitleAnimationEvaluation.ApplyStyleAnimations(evaluated, visual, offset,
            active ? SubtitleAnimationState.ACTIVE : SubtitleAnimationState.INACTIVE).Fill;
    }

    private static ScenePoint ScaleAt(ProjectDocument document, int offset, MediaTime time)
    {
        var evaluated = SceneEvaluator.EvaluateLayer(document.Layers[0], document.Subtitles[0], time);
        var scale = evaluated.Transform.Scale;
        Assert.Equal(0, evaluated.Transform.Rotation);
        foreach (var range in evaluated.AnimationRanges)
        {
            if (offset >= range.Utf16Start && offset < range.Utf16Start + range.Utf16Length)
            {
                Assert.Equal(0, range.Rotation);
                scale = new(scale.X * range.Scale.X, scale.Y * range.Scale.Y);
            }
        }
        return scale;
    }

    private static double RotationAt(ProjectDocument document, int offset, MediaTime time)
    {
        var evaluated = SceneEvaluator.EvaluateLayer(document.Layers[0], document.Subtitles[0], time);
        var rotation = evaluated.Transform.Rotation;
        Assert.Equal(new ScenePoint(1, 1), evaluated.Transform.Scale);
        foreach (var range in evaluated.AnimationRanges)
        {
            if (offset >= range.Utf16Start && offset < range.Utf16Start + range.Utf16Length)
            {
                Assert.Equal(new ScenePoint(1, 1), range.Scale);
                rotation += range.Rotation;
            }
        }
        return rotation;
    }

    private static ProjectDocument Document(string text = "a", int karaokeLength = 1)
    {
        var line = new SubtitleLine
        {
            Text = text,
            End = new(2),
            Style = new()
            {
                FontFamily = "Noto Sans",
                FontSize = 20,
                Fill = SceneColor.Black,
                StrokeWidth = 0,
                ShadowColor = SceneColor.Transparent,
                ShadowBlur = 0,
                WrapMode = SubtitleWrapMode.NATURAL
            },
            Karaoke = [new(0, karaokeLength, new(1), new(2), SceneColor.White) { HighlightKind = KaraokeHighlightKind.STEP }]
        };
        return new()
        {
            Width = 640,
            Height = 360,
            Subtitles = [line],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
    }
}
