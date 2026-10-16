using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SubtitleStyleBatchTests
{
    [Theory]
    [InlineData("FontSize")]
    [InlineData("ShadowX")]
    [InlineData("Margins.Left")]
    [InlineData("Scaling.ReferenceWidth")]
    [InlineData("Bold")]
    [InlineData("Fill.Red")]
    public async Task EditingAFieldUpdatesEverySelectedSubtitleAndPreservesOtherFields(string field)
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectLayer(document.Layers[0].Id, [document.Layers[0].Id, document.Layers[1].Id]);
        var original = context.Editor.Snapshot;
        var styles = context.Session.ViewModel.Styles.Subtitle;
        var firstStyle = original.Subtitles[0].Style;
        var secondStyle = original.Subtitles[1].Style;
        switch (field)
        {
            case "FontSize":
                styles.FontSizeText = "80";
                firstStyle = firstStyle with { FontSize = 80 };
                secondStyle = secondStyle with { FontSize = 80 };
                break;
            case "ShadowX":
                styles.ShadowXText = "17";
                firstStyle = firstStyle with { ShadowOffset = firstStyle.ShadowOffset with { X = 17 } };
                secondStyle = secondStyle with { ShadowOffset = secondStyle.ShadowOffset with { X = 17 } };
                break;
            case "Margins.Left":
                styles.Margins.Left.RawText = "75";
                firstStyle = firstStyle with { Margins = firstStyle.Margins with { Left = 75 } };
                secondStyle = secondStyle with { Margins = secondStyle.Margins with { Left = 75 } };
                break;
            case "Scaling.ReferenceWidth":
                styles.Scaling.ReferenceWidth.RawText = "1280";
                firstStyle = firstStyle with { Scaling = firstStyle.Scaling with { ReferenceWidth = 1280 } };
                secondStyle = secondStyle with { Scaling = secondStyle.Scaling with { ReferenceWidth = 1280 } };
                break;
            case "Bold":
                styles.Bold = true;
                firstStyle = firstStyle with { Bold = true };
                secondStyle = secondStyle with { Bold = true };
                break;
            case "Fill.Red":
                styles.FillDraft.Red.RawText = "0.25";
                firstStyle = firstStyle with { Fill = firstStyle.Fill with { Red = 0.25 } };
                secondStyle = secondStyle with { Fill = secondStyle.Fill with { Red = 0.25 } };
                break;
        }

        Assert.True(context.Session.TryCommitDrafts(false));

        var changed = context.Editor.Snapshot;
        Assert.Equal(original.Subtitles[0] with { Style = firstStyle, InlineSpans = [] }, changed.Subtitles[0]);
        Assert.Equal(original.Subtitles[1] with { Style = secondStyle, InlineSpans = [] }, changed.Subtitles[1]);
        Assert.Equal(original.Subtitles[2], changed.Subtitles[2]);
        Assert.Equal(original.Layers, changed.Layers);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.Redo());
        Assert.Same(changed, context.Editor.Snapshot);
    }

    [Fact]
    public async Task ExplicitEquivalentInputCanUnifyDifferentValuesWithoutOverwritingOtherFields()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectLayer(document.Layers[0].Id, [document.Layers[0].Id, document.Layers[1].Id]);
        context.Session.ViewModel.Styles.Subtitle.FontSizeText = "40.0";

        Assert.True(context.Session.TryCommitDrafts(false));

        Assert.All(context.Editor.Snapshot.Subtitles.Take(2), line => Assert.Equal(40, line.Style.FontSize));
        Assert.All(context.Editor.Snapshot.Subtitles.Take(2), line => Assert.Empty(line.InlineSpans));
        Assert.Equal(document.Subtitles[1].Style.Fill, context.Editor.Snapshot.Subtitles[1].Style.Fill);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task RestoringOneFieldExcludesItFromTheBatchWhileOtherEditsCommit()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectLayer(document.Layers[0].Id, [document.Layers[0].Id, document.Layers[1].Id]);
        var styles = context.Session.ViewModel.Styles.Subtitle;
        styles.FontSizeText = "invalid";
        styles.ShadowXText = "17";
        Assert.True(styles.RestoreField("FontSizeInput"));

        Assert.True(context.Session.TryCommitDrafts(false));

        Assert.Equal(40, context.Editor.Snapshot.Subtitles[0].Style.FontSize);
        Assert.Equal(52, context.Editor.Snapshot.Subtitles[1].Style.FontSize);
        Assert.All(context.Editor.Snapshot.Subtitles.Take(2), line => Assert.Equal(17, line.Style.ShadowOffset.X));
    }

    [Fact]
    public async Task EachAnimatedTargetUsesItsOwnContentTimeAndKeepsUnrelatedTracks()
    {
        var document = Document();
        var secondTrack = new ProjectTrack { Name = "Second" };
        document = document with
        {
            Tracks = document.Tracks.Add(secondTrack),
            Subtitles = document.Subtitles.SetItem(1, document.Subtitles[1] with { Start = new(1), End = new(5) }),
            Layers = document.Layers
                .SetItem(0, document.Layers[0] with
                {
                    Tracks = [new(AnimationProperty.STROKE_WIDTH, [new(new(0), 2), new(new(2), 4)])]
                })
                .SetItem(1, document.Layers[1] with
                {
                    TrackId = secondTrack.Id, Start = new(1), End = new(5), AnimationOffset = new(3),
                    Tracks =
                    [
                        new(AnimationProperty.STROKE_WIDTH, [new(new(3), 3), new(new(7), 7)]),
                        new(AnimationProperty.OPACITY, [new(new(3), 0.5)])
                    ]
                })
        };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectLayer(document.Layers[0].Id, [document.Layers[0].Id, document.Layers[1].Id]);
        context.Session.SelectedKeyTime = new(2);
        context.Session.ViewModel.Styles.Subtitle.StrokeWidthText = "8";

        Assert.True(context.Session.TryCommitDrafts(false));

        var changed = context.Editor.Snapshot;
        Assert.Equal(8, changed.Layers[0].Tracks[0].Keyframes.Single(frame => frame.Time == new MediaTime(2)).Value.Scalar);
        Assert.Equal(8, changed.Layers[1].Tracks[0].Keyframes.Single(frame => frame.Time == new MediaTime(4)).Value.Scalar);
        Assert.Equal(document.Layers[1].Tracks[1], changed.Layers[1].Tracks[1]);
        Assert.Equal(document.Subtitles[1].Style.StrokeWidth, changed.Subtitles[1].Style.StrokeWidth);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task OrderedAnimationOnAnyTargetBlocksTheEntireBatch()
    {
        var document = Document();
        document = document with
        {
            Layers = document.Layers.SetItem(1, document.Layers[1] with
            {
                Tracks = [new(AnimationProperty.STROKE_WIDTH, [])
                {
                    InitialValue = 2,
                    Transforms = [new(Guid.NewGuid(), new(0), new(2), 4)]
                }]
            })
        };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectLayer(document.Layers[0].Id, [document.Layers[0].Id, document.Layers[1].Id]);
        Assert.False(context.Session.ViewModel.Styles.Subtitle.CanEditStrokeWidth);
        context.Session.ViewModel.Styles.Subtitle.StrokeWidthText = "8";

        Assert.False(context.Session.TryCommitDrafts(false));

        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    private static ProjectDocument Document()
    {
        var first = new SubtitleLine
        {
            Text = "First", End = new(4),
            Style = new() { FontFamily = "sans-serif", FontSize = 40 },
            InlineSpans = [new(0, 2, new() { Italic = true })]
        };
        var second = new SubtitleLine
        {
            Text = "Second", Start = new(5), End = new(9),
            Style = new()
            {
                FontFamily = "sans-serif", FontSize = 52, Fill = new(0.2, 0.4, 0.6),
                ShadowOffset = new(8, 9), Margins = new(100, 110, 120),
                Scaling = new() { ReferenceWidth = 800, ReferenceHeight = 600, MatchWidthOrHeight = 1 }
            },
            InlineSpans = [new(0, 2, new() { Bold = true })]
        };
        var third = new SubtitleLine { Text = "Untouched", Start = new(10), End = new(14) };
        return new()
        {
            Subtitles = [first, second, third],
            Layers = [.. new[] { first, second, third }.Select(line => new ProjectLayer
            {
                Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End
            })]
        };
    }
}
