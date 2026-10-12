using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class SubtitleAnimationRangeEditingTests
{
    [Fact]
    public void RangeUpdatesPreserveIdentityTracksAndExplicitOrder()
    {
        var first = Range(0, 2);
        var second = Range(1, 2);
        var editor = new ProjectEditor(Document("abcd", [first, second], [Track(first.Id)]));
        var subtitleId = editor.Snapshot.Subtitles[0].Id;
        editor.SetSubtitleAnimationRange(subtitleId, first with { Rotation = 15, Scale = new(2, 1) }, 1);

        var ranges = editor.Snapshot.Subtitles[0].AnimationRanges;
        Assert.Equal(new[] { second.Id, first.Id }, ranges.Select(range => range.Id));
        Assert.Equal(15, ranges[1].Rotation);
        Assert.Equal(new ScenePoint(2, 1), ranges[1].Scale);
        Assert.Equal(first.Id, Assert.Single(editor.Snapshot.Layers[0].Tracks).Target.TextRangeId);
        editor.MoveSubtitleAnimationRange(subtitleId, first.Id, 0);
        Assert.Equal(first.Id, editor.Snapshot.Subtitles[0].AnimationRanges[0].Id);
    }

    [Fact]
    public void InvalidRangeCannotSplitAGraphemeOrChangeHistory()
    {
        var editor = new ProjectEditor(Document("a😀b"));
        var before = editor.Snapshot;

        Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetSubtitleAnimationRange(before.Subtitles[0].Id, Range(2, 1)));
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void RemovingARangeOnlyRemovesItsTracksInOneUndoableTransaction()
    {
        var first = Range(0, 2);
        var second = Range(1, 2);
        var original = Document("abcd", [first, second], [Track(first.Id), Track(second.Id), Track(null)]);
        var editor = new ProjectEditor(original);
        editor.RemoveSubtitleAnimationRange(original.Subtitles[0].Id, first.Id);

        Assert.Equal(second.Id, Assert.Single(editor.Snapshot.Subtitles[0].AnimationRanges).Id);
        Assert.Equal(new Guid?[] { second.Id, null }, editor.Snapshot.Layers[0].Tracks.Select(track => track.Target.TextRangeId));
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
        Assert.True(editor.Redo());
        Assert.Single(editor.Snapshot.Subtitles[0].AnimationRanges);
    }

    [Fact]
    public void ClearingACompleteTargetPreservesOtherRangesAndTheWholeSubtitle()
    {
        var first = Range(0, 2);
        var second = Range(1, 2);
        var original = Document("abcd", [first, second], [Track(first.Id), Track(second.Id), Track(null)]);
        var editor = new ProjectEditor(original);
        editor.ClearAnimationTracks([original.Layers[0].Id], new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: first.Id));

        Assert.Equal(new Guid?[] { second.Id, null }, editor.Snapshot.Layers[0].Tracks.Select(track => track.Target.TextRangeId));
        Assert.Equal(original.Subtitles[0].AnimationRanges, editor.Snapshot.Subtitles[0].AnimationRanges);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
    }

    [Fact]
    public void CompleteTargetClearingKeepsOtherVisualStatesAndRejectsUnknownRangesAtomically()
    {
        var range = Range(0, 2);
        var normal = Track(range.Id);
        var active = normal with { Target = normal.Target with { State = SubtitleAnimationState.ACTIVE } };
        var original = Document("abcd", [range], [normal, active]);
        var editor = new ProjectEditor(original);
        editor.ClearAnimationTracks([original.Layers[0].Id], active.Target);
        Assert.Equal(normal.Target, Assert.Single(editor.Snapshot.Layers[0].Tracks).Target);
        var before = editor.Snapshot;
        Assert.Throws<InvalidDataException>(() => editor.ClearAnimationTracks([original.Layers[0].Id],
            normal.Target with { TextRangeId = Guid.NewGuid() }));
        Assert.Same(before, editor.Snapshot);
    }

    [Fact]
    public void EqualRangeAndMissingTrackClearPreserveSavedIdentityAndRedo()
    {
        var range = Range(0, 2);
        var editor = new ProjectEditor(Document("abcd", [range]));
        var before = editor.Snapshot;
        editor.SetSubtitleAnimationRange(before.Subtitles[0].Id, range with { Rotation = 20 });
        Assert.True(editor.Undo());
        editor.SetSubtitleAnimationRange(before.Subtitles[0].Id, range);
        editor.MoveSubtitleAnimationRange(before.Subtitles[0].Id, range.Id, 0);
        editor.ClearAnimationTracks([before.Layers[0].Id], new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: range.Id));

        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.CanRedo);
    }

    [Fact]
    public void EqualGraphemeReplacementKeepsRangeIdentityGeometryAndAnimationClock()
    {
        var range = Range(1, 3) with { Rotation = 25, Scale = new(2, 3) };
        var editor = new ProjectEditor(Document("a😀bc", [range], [Track(range.Id)]));
        var before = editor.Snapshot;
        editor.ReplaceSubtitleTextRange(before.Subtitles[0].Id, 1, 2, "字");

        Assert.Equal(range with { Utf16Length = 2 }, Assert.Single(editor.Snapshot.Subtitles[0].AnimationRanges));
        Assert.Equal(before.Layers, editor.Snapshot.Layers);
    }

    [Theory]
    [InlineData(1, 1, 4)]
    [InlineData(2, 1, 4)]
    [InlineData(3, 1, 2)]
    public void InsertionsFollowExistingStyleInheritance(int insertion, int expectedStart, int expectedLength)
    {
        var range = Range(1, 2);
        var editor = new ProjectEditor(Document("abcd", [range], [Track(range.Id)]));
        editor.ReplaceSubtitleTextRange(editor.Snapshot.Subtitles[0].Id, insertion, 0, "XY");

        Assert.Equal((expectedStart, expectedLength), (editor.Snapshot.Subtitles[0].AnimationRanges[0].Utf16Start,
            editor.Snapshot.Subtitles[0].AnimationRanges[0].Utf16Length));
    }

    [Fact]
    public void AppendingAtTextEndInheritsTheFinalRange()
    {
        var range = Range(1, 2);
        var editor = new ProjectEditor(Document("abc", [range]));
        editor.ReplaceSubtitleTextRange(editor.Snapshot.Subtitles[0].Id, 3, 0, "XY");

        Assert.Equal(range with { Utf16Length = 4 }, Assert.Single(editor.Snapshot.Subtitles[0].AnimationRanges));
    }

    [Fact]
    public void RemovingAllRangeTextDropsOnlyDanglingTargetsAndKeepsOverlapOrder()
    {
        var first = Range(1, 1);
        var second = Range(0, 3);
        var third = Range(2, 1);
        var editor = new ProjectEditor(Document("abcd", [first, second, third], [Track(first.Id), Track(second.Id), Track(third.Id), Track(null)]));
        editor.ReplaceSubtitleTextRange(editor.Snapshot.Subtitles[0].Id, 1, 1, "");

        var ranges = editor.Snapshot.Subtitles[0].AnimationRanges;
        Assert.Equal(new[] { second.Id, third.Id }, ranges.Select(range => range.Id));
        Assert.Equal(second with { Utf16Length = 2 }, ranges[0]);
        Assert.Equal(third with { Utf16Start = 1 }, ranges[1]);
        Assert.Equal(new Guid?[] { second.Id, third.Id, null }, editor.Snapshot.Layers[0].Tracks.Select(track => track.Target.TextRangeId));
    }

    [Fact]
    public void CombiningInsertionKeepsTheCompleteGraphemeInItsOriginalRange()
    {
        var range = Range(0, 1);
        var editor = new ProjectEditor(Document("eb", [range], [Track(range.Id)]));
        editor.ReplaceSubtitleTextRange(editor.Snapshot.Subtitles[0].Id, 1, 0, "\u0301");

        Assert.Equal(range with { Utf16Length = 2 }, Assert.Single(editor.Snapshot.Subtitles[0].AnimationRanges));
        ProjectValidator.Validate(editor.Snapshot);
    }

    [Fact]
    public void DirectTextUpdateAndAssProjectionUseTheSameRangeLifecycle()
    {
        var range = Range(1, 2);
        var initial = Document("abcd", [range], [Track(range.Id)]);
        var editor = new ProjectEditor(initial);
        editor.UpdateSubtitle(initial.Subtitles[0].Id, line => line with { Text = "acd" });
        Assert.Equal(range with { Utf16Length = 1 }, Assert.Single(editor.Snapshot.Subtitles[0].AnimationRanges));

        editor = new(initial);
        var edited = AssTextProjection.Apply(initial.Subtitles[0], "acd");
        editor.ApplyAssTextEdit(initial.Subtitles[0].Id, edited);
        Assert.Equal(range with { Utf16Length = 1 }, Assert.Single(editor.Snapshot.Subtitles[0].AnimationRanges));
    }

    [Fact]
    public void SplitRemapsRightRangeIdentitiesFiltersTargetsAndPreservesContentPhase()
    {
        var leftOnly = Range(0, 1);
        var crossing = Range(1, 2) with { Rotation = 15 };
        var rightOnly = Range(3, 1);
        var original = Document("abcd", [leftOnly, crossing, rightOnly], [Track(leftOnly.Id), Track(crossing.Id), Track(rightOnly.Id), Track(null)]);
        var result = ProjectEditingOperations.SplitSubtitle(original, original.Subtitles[0].Id, new(2), 2);

        Assert.Equal(new[] { leftOnly.Id, crossing.Id }, result.Subtitles[0].AnimationRanges.Select(range => range.Id));
        Assert.Equal(crossing with { Utf16Length = 1 }, result.Subtitles[0].AnimationRanges[1]);
        var rightRanges = result.Subtitles[1].AnimationRanges;
        Assert.All(rightRanges, range => Assert.DoesNotContain(range.Id, original.Subtitles[0].AnimationRanges.Select(value => value.Id)));
        Assert.Equal(new[] { (0, 1), (1, 1) }, rightRanges.Select(range => (range.Utf16Start, range.Utf16Length)));
        Assert.Equal(new Guid?[] { rightRanges[0].Id, rightRanges[1].Id, null }, result.Layers[1].Tracks.Select(track => track.Target.TextRangeId));
        Assert.Equal(new MediaTime(2), result.Layers[1].AnimationOffset);
        Assert.Equal(SceneEvaluator.EvaluateColorTrack(original.Layers[0].Tracks[1], new(3)),
            SceneEvaluator.EvaluateColorTrack(result.Layers[1].Tracks[0], new(3)));
        ProjectValidator.Validate(result);
    }

    [Fact]
    public void ClipboardCopiesGenerateIndependentRangeAndOperationIds()
    {
        var range = Range(0, 2);
        var track = Track(range.Id) with
        {
            Keyframes = [], InitialValue = SceneColor.Black,
            Transforms = [new(Guid.NewGuid(), new(0), new(4), SceneColor.White)]
        };
        var source = Document("abcd", [range], [track]);
        var captured = ProjectEditingOperations.CaptureClips(source, [source.Layers[0].Id], source.Layers[0].Id);
        var result = ProjectEditingOperations.PasteClips(source, captured, new(4)).Document;

        var copiedRange = Assert.Single(result.Subtitles[1].AnimationRanges);
        Assert.NotEqual(range.Id, copiedRange.Id);
        var copiedTrack = Assert.Single(result.Layers[1].Tracks);
        Assert.Equal(copiedRange.Id, copiedTrack.Target.TextRangeId);
        Assert.NotEqual(track.Transforms[0].Id, copiedTrack.Transforms[0].Id);
    }

    [Fact]
    public void ProjectMergeRemapsRangesAndTheirTrackReferences()
    {
        var range = Range(0, 2);
        var source = Document("abcd", [range], [Track(range.Id)]);
        var result = ProjectEditingOperations.MergeProjects(new(), [new(source, "Source", "/tmp")]).Document;
        var copiedRange = Assert.Single(Assert.Single(result.Subtitles).AnimationRanges);

        Assert.NotEqual(range.Id, copiedRange.Id);
        Assert.Equal(copiedRange.Id, Assert.Single(Assert.Single(result.Layers).Tracks).Target.TextRangeId);
        ProjectValidator.Validate(result);
    }

    [Fact]
    public void NeutralRangesSurviveMergeWhileAnimatedOrTransformedRangesRejectIt()
    {
        var range = Range(0, 2);
        var first = new SubtitleLine { Text = "ab", End = new(2), AnimationRanges = [range] };
        var second = new SubtitleLine { Text = "cd", Start = new(2), End = new(4), AnimationRanges = [Range(0, 2)] };
        var document = new ProjectDocument
        {
            Subtitles = [first, second],
            Layers = [Layer(first), Layer(second)]
        };
        var result = ProjectEditingOperations.MergeSubtitles(document, first.Id, second.Id);
        Assert.Equal(new[] { (0, 2), (3, 2) }, result.Subtitles[0].AnimationRanges.Select(value => (value.Utf16Start, value.Utf16Length)));

        var transformed = document with { Subtitles = document.Subtitles.SetItem(0, first with { AnimationRanges = [range with { Rotation = 10 }] }) };
        Assert.Throws<InvalidOperationException>(() => ProjectEditingOperations.MergeSubtitles(transformed, first.Id, second.Id));
        var translated = document with { Subtitles = document.Subtitles.SetItem(0, first with { AnimationRanges = [range with { Offset = new(0, -12) }] }) };
        Assert.Throws<InvalidOperationException>(() => ProjectEditingOperations.MergeSubtitles(translated, first.Id, second.Id));
        var animation = Track(range.Id) with
        {
            Keyframes = [new(new(0), SceneColor.Black), new(new(2), SceneColor.White)]
        };
        var animated = document with { Layers = document.Layers.SetItem(0, document.Layers[0] with { Tracks = [animation] }) };
        Assert.Throws<InvalidOperationException>(() => ProjectEditingOperations.MergeSubtitles(animated, first.Id, second.Id));
    }

    private static SubtitleAnimationRange Range(int start, int length) => new(Guid.NewGuid(), start, length);

    private static AnimationTrack Track(Guid? rangeId)
    {
        return new(new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: rangeId),
            [new(new(0), SceneColor.Black), new(new(4), SceneColor.White)]);
    }

    private static ProjectDocument Document(string text, ImmutableArray<SubtitleAnimationRange> ranges = default,
        ImmutableArray<AnimationTrack> tracks = default)
    {
        var line = new SubtitleLine { Text = text, End = new(4), AnimationRanges = ranges.IsDefault ? [] : ranges };
        return new() { Subtitles = [line], Layers = [Layer(line) with { Tracks = tracks.IsDefault ? [] : tracks }] };
    }

    private static ProjectLayer Layer(SubtitleLine line)
    {
        return new() { Id = line.Id, SubtitleId = line.Id, Start = line.Start, End = line.End };
    }
}
