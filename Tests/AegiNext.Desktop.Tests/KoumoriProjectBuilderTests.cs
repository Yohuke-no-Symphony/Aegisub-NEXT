using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Koumori;

namespace AegiNext.Desktop.Tests;

public sealed class KoumoriProjectBuilderTests
{
    [Fact]
    public void OverlappingEmptySubtitlesUseNewTracksAndPreserveExistingContentWithOneUndo()
    {
        var original = ProjectEditingOperations.CreateSubtitleClips(new(), [new() { Start = new(0), End = new(4), Text = "Keep original" }], ProjectTrack.DEFAULT_TRACK_ID);
        original = ProjectEditingOperations.AddTrack(original, new() { Name = "Automatic 1" });
        var editor = new ProjectEditor(original);
        var result = KoumoriProjectBuilder.Add(original,
            [new(new(1), new(3)), new(new(2), new(4)), new(new(3), new(5))], "Automatic");
        ProjectValidator.Validate(result);

        Assert.Equal(original.Tracks.Length + 2, result.Tracks.Length);
        Assert.Equal(original.Subtitles.Length + 3, result.Subtitles.Length);
        Assert.Same(original.Subtitles[0], result.Subtitles.First(value => value.Id == original.Subtitles[0].Id));
        Assert.Same(original.Layers[0], result.Layers.First(value => value.Id == original.Layers[0].Id));
        var addedLines = result.Subtitles.Where(value => value.Id != original.Subtitles[0].Id).ToArray();
        Assert.All(addedLines, value => Assert.Equal(string.Empty, value.Text));
        var addedTracks = result.Tracks.Where(value => original.Tracks.All(existing => existing.Id != value.Id)).ToArray();
        Assert.Equal(new[] { "Automatic 2", "Automatic 3" }, addedTracks.Select(value => value.Name).Order());
        foreach (var track in addedTracks)
        {
            var layers = result.Layers.Where(value => value.TrackId == track.Id).OrderBy(value => value.Start).ToArray();
            for (var index = 1; index < layers.Length; index++)
            {
                Assert.True(layers[index - 1].End <= layers[index].Start);
            }
        }
        editor.Apply("Automatic subtitle timing", _ => result);
        Assert.True(editor.CanUndo);
        editor.Undo();
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void EmptyPreviewDoesNotCreateATrack()
    {
        var original = new ProjectDocument();
        Assert.Same(original, KoumoriProjectBuilder.Add(original, [], "Automatic"));
    }
}
