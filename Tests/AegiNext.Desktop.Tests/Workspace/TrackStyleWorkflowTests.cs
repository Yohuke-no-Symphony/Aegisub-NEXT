using System.Collections.Immutable;
using System.Security.Cryptography;
using AegiNext.Application.Presets;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class TrackStyleWorkflowTests
{
    [Theory]
    [InlineData((int)TrackStyleUpdateDecision.DEFAULT_ONLY)]
    [InlineData((int)TrackStyleUpdateDecision.UPDATE_EXISTING)]
    [InlineData((int)TrackStyleUpdateDecision.CANCEL)]
    public async Task TrackDecisionControlsOnlyItsExistingClipsAndKeepsUndoAtomic(int choice)
    {
        var decision = (TrackStyleUpdateDecision)choice;
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var session = context.Session;
        context.Dialogs.TrackStyleChoice = decision;
        var track = Assert.IsType<Guid>(session.CurrentTrackId);
        var cue = context.Editor.AddSubtitle(new(0), new(2), "existing", track);
        var otherTrack = context.Editor.AddTrack("Other");
        var otherCue = context.Editor.AddSubtitle(new(0), new(2), "other", otherTrack);
        context.Editor.SetKeyframe(cue, AnimationProperty.OPACITY, new(new(1), 0.4));
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "New preset", new() { FontSize = 97 });
        await session.Styles.UpsertAsync(preset);
        var before = context.Editor.Snapshot;

        await session.ApplySubtitleTrackStyleAsync(track, preset.Id);

        Assert.Equal(1, context.Dialogs.TrackStyleRequests);
        Assert.False(session.IsProjectBusy);
        Assert.Null(session.LastError);
        if (decision == TrackStyleUpdateDecision.CANCEL)
        {
            Assert.Same(before, context.Editor.Snapshot);
            return;
        }

        Assert.Equal(decision == TrackStyleUpdateDecision.UPDATE_EXISTING ? preset.Style : before.Subtitles[0].Style,
            context.Editor.Snapshot.Subtitles.Single(line => line.Id == cue).Style);
        Assert.Same(before.Subtitles.Single(line => line.Id == otherCue), context.Editor.Snapshot.Subtitles.Single(line => line.Id == otherCue));
        Assert.Equal(before.Layers, context.Editor.Snapshot.Layers);
        Assert.True(context.Editor.Undo());
        Assert.Same(before, context.Editor.Snapshot);
    }

    [Fact]
    public async Task ImportOnDisabledTrackUsesSelectedPortableStyleWithFontAndSelectsFirstClipAfterBusyEnds()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var session = context.Session;
        session.SetProjectLocation(null, context.DirectoryPath);
        var originalTrackId = Assert.IsType<Guid>(session.CurrentTrackId);
        var other = context.Editor.AddTrack("Other");
        var otherCue = context.Editor.AddSubtitle(new(0), new(3), "other", other);
        context.Editor.SetSubtitleTrackStyle(originalTrackId, Guid.NewGuid(), "Saved track", new() { FontSize = 40 });
        context.Editor.SetSubtitleTrackAutoApplyStyle(originalTrackId, false);
        var bytes = (await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"))).ToImmutableArray();
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan()));
        Assert.Equal("bfb7bb691513f12e734dc346c03a03f784912432d7e3fa8e56efcf906fe86b3d", digest);
        var font = new EmbeddedSubtitleFont("NotoSans.ttf", digest, bytes);
        var family = new FontFamilyBundle("Noto Sans", [font]);
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Panel font", new() { FontFamily = family.FamilyName, FontSize = 61 });
        await session.ApplicationContext.RunStyleOperationAsync(() => session.StyleLibrary.UpsertAsync(new CapturedSubtitleStyle(preset, [family])));
        session.Styles.Refresh(preset.Id);
        context.Dialogs.OpenPath = Path.Combine(context.DirectoryPath, "import.srt");
        await File.WriteAllTextAsync(context.Dialogs.OpenPath,
            "1\n00:00:00,125 --> 00:00:01,125\nFirst\n\n2\n00:00:01,125 --> 00:00:02,125\nSecond\n");
        var before = context.Editor.Snapshot;

        await session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_SUBTITLES);

        Assert.Null(session.LastError);
        Assert.False(session.IsProjectBusy);
        Assert.Empty(context.Editor.Snapshot.Assets);
        var imported = context.Editor.Snapshot.Subtitles.Where(line => line.Id != otherCue).ToArray();
        Assert.Equal(2, imported.Length);
        Assert.All(imported, line =>
        {
            Assert.Equal(session.CurrentTrackId, session.ClipIndex.GetSubtitleTrackId(line.Id));
            Assert.Equal(preset.Style, line.Style);
            Assert.Null(line.Style.FontAssetId);
        });
        Assert.Equal(new MediaTime(1, 8), imported[0].Start);
        Assert.Equal(new MediaTime(17, 8), imported[1].End);
        Assert.Equal("First", imported[0].Text);
        Assert.Equal(imported[0].Id, session.SelectedCue?.Id);
        Assert.Same(before.Subtitles.Single(line => line.Id == otherCue), context.Editor.Snapshot.Subtitles.Single(line => line.Id == otherCue));
        Assert.Equal(40, context.Editor.Snapshot.Tracks.Single(track => track.Id == originalTrackId).DefaultStyle!.FontSize);
        Assert.True(context.Editor.Undo());
        Assert.Same(before, context.Editor.Snapshot);
    }
}
