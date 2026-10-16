using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Startup;
using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SubtitleStyleBatchFontTests
{
    private static readonly string[] fontFamilies = ["Primary Fixture", "Secondary Fixture"];
    private static readonly (bool Bold, bool Italic)[] fontStyles = [(false, false), (true, false), (false, true), (true, true)];

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task FormattingUsesEachSelectedFontsOwnFaceAndPreservesItsOtherFormatting(bool italic,
        bool primaryNamed, bool otherFormatting)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var application = CreateApplication(directory.Path);
        await application.Initialization;
        var document = Document(primaryNamed, italic, otherFormatting);
        await using var context = new WorkspaceSessionTestContext(document, applicationContext: application);
        await context.InitializeAsync();
        context.Session.SelectLayer(document.Layers[0].Id, [document.Layers[0].Id, document.Layers[1].Id]);
        var original = context.Editor.Snapshot;
        var styles = context.Session.ViewModel.Styles.Subtitle;

        if (italic)
        {
            styles.CommitItalic(true);
        }
        else
        {
            styles.CommitBold(true);
        }

        var changed = context.Editor.Snapshot;
        Assert.Equal(document.Subtitles[0] with
        {
            Style = document.Subtitles[0].Style with
            {
                FontVariant = Variant("Primary Fixture", !italic, italic), Bold = !italic, Italic = italic
            },
            InlineSpans = []
        }, changed.Subtitles[0]);
        var secondaryBold = !italic || otherFormatting;
        var secondaryItalic = italic || otherFormatting;
        Assert.Equal(document.Subtitles[1] with
        {
            Style = document.Subtitles[1].Style with
            {
                FontVariant = Variant("Secondary Fixture", secondaryBold, secondaryItalic),
                Bold = secondaryBold, Italic = secondaryItalic
            },
            InlineSpans = []
        }, changed.Subtitles[1]);
        Assert.Same(document.Subtitles[2], changed.Subtitles[2]);
        Assert.Equal(document.Layers, changed.Layers);
        Assert.False(styles.HasDrafts);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.Redo());
        Assert.Same(changed, context.Editor.Snapshot);
    }

    [Fact]
    public async Task ExplicitFontSelectionUnifiesTheFamilyAndNamedFaceOnlyOnSelectedSubtitles()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var application = CreateApplication(directory.Path);
        await application.Initialization;
        var document = Document(true, false, false);
        await using var context = new WorkspaceSessionTestContext(document, applicationContext: application);
        await context.InitializeAsync();
        context.Session.SelectLayer(document.Layers[0].Id, [document.Layers[0].Id, document.Layers[1].Id]);
        var original = context.Editor.Snapshot;
        var variant = Variant("Primary Fixture", true, true);

        context.Session.ViewModel.Styles.Subtitle.CommitFont(new FontSelection("Primary Fixture", variant, true));

        var changed = context.Editor.Snapshot;
        for (var index = 0; index < 2; index++)
        {
            Assert.Equal(document.Subtitles[index] with
            {
                Style = document.Subtitles[index].Style with
                {
                    FontFamily = "Primary Fixture", FontVariant = variant, Bold = true, Italic = true
                },
                InlineSpans = []
            }, changed.Subtitles[index]);
        }
        Assert.Same(document.Subtitles[2], changed.Subtitles[2]);
        Assert.Equal(document.Layers, changed.Layers);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    private static DesktopApplicationContext CreateApplication(string directory)
    {
        var faces = fontFamilies.SelectMany(family => fontStyles.Select(style => new SystemFontFace
                {
                    FamilyName = family, Aliases = [family], Variant = Variant(family, style.Bold, style.Italic)
                }));
        return new(new WorkbenchPreferencesStore(directory),
            new() { Projects = new() { WorkspaceRoot = Path.Combine(directory, "workspace") } },
            new SubtitleFontSelectionService(faces));
    }

    private static SubtitleFontVariant Variant(string family, bool bold, bool italic)
    {
        var name = (bold, italic) switch
        {
            (true, true) => "Bold Italic",
            (true, false) => "Bold",
            (false, true) => "Italic",
            _ => "Regular"
        };
        return new()
        {
            Name = name, PostScriptName = family.Replace(" ", string.Empty, StringComparison.Ordinal) + "-" +
                name.Replace(" ", string.Empty, StringComparison.Ordinal),
            Weight = bold ? 700 : 400, Italic = italic
        };
    }

    private static ProjectDocument Document(bool primaryNamed, bool italic, bool otherFormatting)
    {
        var first = new SubtitleLine
        {
            Text = "Primary", End = new(4),
            Style = new()
            {
                FontFamily = "Primary Fixture", FontSize = 40,
                FontVariant = primaryNamed ? Variant("Primary Fixture", false, false) : null
            },
            InlineSpans = [new(0, 2, new() { Underline = true })]
        };
        var secondaryBold = italic && otherFormatting;
        var secondaryItalic = !italic && otherFormatting;
        var second = new SubtitleLine
        {
            Text = "Secondary", Start = new(5), End = new(9),
            Style = new()
            {
                FontFamily = "Secondary Fixture", FontSize = 52,
                FontVariant = Variant("Secondary Fixture", secondaryBold, secondaryItalic),
                Bold = secondaryBold, Italic = secondaryItalic
            },
            InlineSpans = [new(0, 2, new() { Strikethrough = true })]
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
