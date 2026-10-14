using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.EffectLibrary.Styles;

namespace AegiNext.Desktop.Tests;

public sealed class SettingsStylePreviewTests
{
    [Fact]
    public void PreviewTextDoesNotDirtySaveApplyOrModifyTheStoredPreset()
    {
        var original = CreatePreset();
        var model = CreateModel(original);
        var saved = 0;
        var applied = 0;
        model.UpsertRequested += (_, _) => saved++;
        model.ApplyRequested += (_, _) => applied++;

        model.PreviewText = "用户预览文字 ABC 123\nSecond line";

        Assert.True(model.HasPreview);
        Assert.True(model.TryCreatePreviewPreset(out var candidate));
        Assert.NotNull(candidate);
        Assert.False(model.IsDirty);
        Assert.Equal(0, saved);
        Assert.Equal(0, applied);
        Assert.Equal(original, model.Draft);
        Assert.Same(original, Assert.Single(model.Styles));
    }

    [Theory]
    [InlineData("Fill")]
    [InlineData("Stroke")]
    [InlineData("Shadow")]
    public void ValidRawColorAndNumberProduceACandidateWithoutCommittingTheColorOrChangingTheDraft(string field)
    {
        var original = CreatePreset();
        var model = CreateModel(original);
        var color = GetColor(model, field);
        var commits = 0;
        color.Committed += (_, _) => commits++;
        model.FontSizeText = "72";
        color.HexText = "#00FF00FF";
        var currentDraft = model.Draft;

        Assert.True(model.TryCreatePreviewPreset(out var candidate));

        Assert.NotNull(candidate);
        Assert.Equal(72, candidate.Style.FontSize);
        Assert.Equal(new SceneColor(0, 1, 0, 1), GetColor(candidate.Style, field));
        Assert.Equal("72", model.FontSizeText);
        Assert.Equal("#00FF00FF", color.HexText);
        Assert.True(color.IsDirty);
        Assert.Equal(0, commits);
        Assert.Same(currentDraft, model.Draft);
        Assert.Equal(GetColor(original.Style, field), GetColor(model.Draft!.Style, field));
        Assert.Same(original, Assert.Single(model.Styles));
        Assert.Equal(64, original.Style.FontSize);
    }

    [Fact]
    public void VerticalMarginEditingKeepsAsymmetricHorizontalMarginsInTheDraftAndPreview()
    {
        var original = CreatePreset() with { Style = new() { Margins = new(13.125, 41.25, 27) } };
        var model = CreateModel(original);

        model.Margins.Vertical.RawText = "38.5";

        Assert.Equal(new SubtitleMargins(13.125, 41.25, 38.5), model.Draft!.Style.Margins);
        Assert.True(model.TryCreatePreviewPreset(out var candidate));
        Assert.Equal(model.Draft.Style.Margins, candidate!.Style.Margins);
        Assert.Equal(new SubtitleMargins(13.125, 41.25, 27), original.Style.Margins);
    }

    [Fact]
    public void AnUneditedCandidatePreservesDoublePrecisionAndHdrColors()
    {
        var original = CreatePreset() with
        {
            Style = new()
            {
                FontSize = 64.00000000000003,
                StrokeWidth = 2.0000000000000004,
                Margins = new(13.000000000000004, 41.00000000000001, 27.000000000000004),
                Fill = new(2.5, 1.25, 0.5, 0.9),
                Stroke = new(1.75, 0.25, 0.125, 0.8),
                ShadowColor = new(1.5, 0.1, 0.2, 0.5)
            }
        };
        var model = CreateModel(original);

        Assert.True(model.TryCreatePreviewPreset(out var candidate));

        Assert.NotNull(candidate);
        Assert.Equal(original.Style, candidate.Style);
        Assert.False(model.FillDraft.IsDirty);
        Assert.False(model.StrokeDraft.IsDirty);
        Assert.False(model.ShadowDraft.IsDirty);
        Assert.False(model.IsDirty);
    }

    [Theory]
    [InlineData("FontSize", "7e-")]
    [InlineData("FontSize", "0")]
    [InlineData("FontSize", "4097")]
    [InlineData("StrokeWidth", "-1")]
    [InlineData("StrokeWidth", "4097")]
    [InlineData("MarginLeft", "7e-")]
    [InlineData("MarginLeft", "-1")]
    [InlineData("MarginLeft", "32769")]
    [InlineData("MarginRight", "7e-")]
    [InlineData("MarginRight", "-1")]
    [InlineData("MarginRight", "32769")]
    [InlineData("MarginVertical", "7e-")]
    [InlineData("MarginVertical", "-1")]
    [InlineData("MarginVertical", "32769")]
    [InlineData("LineHeight", "0")]
    [InlineData("LineHeight", "11")]
    [InlineData("ShadowBlur", "-1")]
    [InlineData("ShadowBlur", "513")]
    [InlineData("ShadowX", "-1000000001")]
    [InlineData("ShadowY", "1000000001")]
    public void InvalidOrOutOfRangeNumbersBlockTheCandidateAndKeepRawText(string field, string rawText)
    {
        var original = CreatePreset();
        var model = CreateModel(original);
        SetNumber(model, field, rawText);
        var currentDraft = model.Draft;

        Assert.False(model.TryCreatePreviewPreset(out var candidate));

        Assert.Null(candidate);
        Assert.Equal(rawText, GetNumber(model, field));
        Assert.Same(currentDraft, model.Draft);
        Assert.Same(original, Assert.Single(model.Styles));
    }

    [Theory]
    [InlineData("Fill")]
    [InlineData("Stroke")]
    [InlineData("Shadow")]
    public void InvalidColorBlocksTheCandidateWithoutRepairingOrCommittingRawText(string field)
    {
        var model = CreateModel(CreatePreset());
        var color = GetColor(model, field);
        var commits = 0;
        color.Committed += (_, _) => commits++;
        color.HexText = "#12";
        var currentDraft = model.Draft;

        Assert.False(model.TryCreatePreviewPreset(out var candidate));

        Assert.Null(candidate);
        Assert.Equal("#12", color.HexText);
        Assert.True(color.HasError);
        Assert.True(color.IsDirty);
        Assert.Equal(0, commits);
        Assert.Same(currentDraft, model.Draft);
    }

    [Theory]
    [InlineData("7e-")]
    [InlineData("1000000001")]
    public void InvalidExplicitPositionBlocksTheCandidateAndKeepsThePositionDraft(string rawText)
    {
        var original = CreatePreset() with { Style = new() { Position = new() } };
        var model = CreateModel(original);
        model.Position.OffsetX.RawText = rawText;
        var currentDraft = model.Draft;

        Assert.False(model.TryCreatePreviewPreset(out var candidate));

        Assert.Null(candidate);
        Assert.Equal(rawText, model.Position.OffsetX.RawText);
        Assert.Equal("OffsetXInput", model.Position.Validate());
        Assert.Same(currentDraft, model.Draft);
        Assert.Same(original, Assert.Single(model.Styles));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Other")]
    public void EmptyOrDuplicateNamesDoNotBlockTheStyleSample(string name)
    {
        var first = CreatePreset();
        var second = CreatePreset() with { Id = Guid.NewGuid(), Name = "Other" };
        var model = new StyleLibraryViewModel();
        model.UpdateStyles([first, second], first.Id);
        model.Name = name;

        Assert.True(model.TryCreatePreviewPreset(out var candidate));

        Assert.NotNull(candidate);
        Assert.Equal(first.Style, candidate.Style);
        Assert.Equal(name, model.Name);
        Assert.Null(model.Error);
        Assert.Null(model.InvalidFieldKey);
    }

    [Fact]
    public void MultipleSelectionHidesTheSampleAndSingleSelectionRestoresIt()
    {
        var first = CreatePreset();
        var second = CreatePreset() with { Id = Guid.NewGuid(), Name = "Second" };
        var model = new StyleLibraryViewModel();
        model.UpdateStyles([first, second], first.Id);
        Assert.True(model.HasPreview);

        model.SelectStyles(first.Id, [first.Id, second.Id]);

        Assert.False(model.HasPreview);
        Assert.False(model.TryCreatePreviewPreset(out var multipleCandidate));
        Assert.Null(multipleCandidate);

        model.SelectStyles(second.Id, [second.Id]);

        Assert.True(model.HasPreview);
        Assert.True(model.TryCreatePreviewPreset(out var singleCandidate));
        Assert.Equal(second.Id, singleCandidate!.Id);
    }

    [Fact]
    public void RevisionAdvancesForStyleColorTextSelectionAndDraftReload()
    {
        var first = CreatePreset();
        var second = CreatePreset() with { Id = Guid.NewGuid(), Name = "Second" };
        var model = new StyleLibraryViewModel();
        AssertRevisionAdvances(model, () => model.UpdateStyles([first, second], first.Id));
        AssertRevisionAdvances(model, () => model.FontSizeText = "72");
        AssertRevisionAdvances(model, () => model.Italic = true);
        AssertRevisionAdvances(model, () => model.Margins.Left.RawText = "27");
        AssertRevisionAdvances(model, () => model.Margins.Right.RawText = "7e-");
        AssertRevisionAdvances(model, () => model.FillDraft.HexText = "#00FF00FF");
        AssertRevisionAdvances(model, () => model.FillDraft.HexText = "#12");
        AssertRevisionAdvances(model, () => model.PreviewText = "New sample 中文");
        AssertRevisionAdvances(model, () => model.SelectStyles(first.Id, [first.Id, second.Id]));
        AssertRevisionAdvances(model, () => model.SelectStyles(second.Id, [second.Id]));
        AssertRevisionAdvances(model, model.DiscardDraft);
    }

    [Fact]
    public void LanguageRefreshPreservesUserSampleAndIncompleteRawDrafts()
    {
        var model = CreateModel(CreatePreset() with { Style = new() { Position = new() } });
        model.PreviewText = "我的预览 Sample 123\n第二行";
        model.FontSizeText = "7e-";
        model.FillDraft.HexText = "#12";
        model.Position.OffsetX.RawText = "-";
        model.Margins.Right.RawText = "7e-";
        var currentDraft = model.Draft;

        model.RefreshLanguage();

        Assert.Equal("我的预览 Sample 123\n第二行", model.PreviewText);
        Assert.Equal("7e-", model.FontSizeText);
        Assert.Equal("#12", model.FillDraft.HexText);
        Assert.Equal("-", model.Position.OffsetX.RawText);
        Assert.Equal("7e-", model.Margins.Right.RawText);
        Assert.True(model.FillDraft.IsDirty);
        Assert.False(model.TryCreatePreviewPreset(out var candidate));
        Assert.Null(candidate);
        Assert.Same(currentDraft, model.Draft);
    }

    private static SubtitleStylePreset CreatePreset()
    {
        return new(Guid.NewGuid(), "Original", new() { FontFamily = "sans-serif" });
    }

    [Fact]
    public void SampleChangesRefreshAutomaticPlacementAndPreserveInvalidExplicitPositionText()
    {
        var model = CreateModel(CreatePreset());
        model.SetPositionMeasurement((_, text) =>
        {
            var height = text.Contains('\n') ? 100 : 50;
            return new(new() { Offset = new(0, -height) }, new(new(1920, 1080), new(), new(100, height), new()));
        });
        model.PreviewText = "First\nSecond";
        Assert.Equal(new ScenePoint(0, -100), model.Position.DiagramPosition!.Offset);
        Assert.False(model.IsDirty);
        model.Position.IsExplicit = true;
        Assert.Equal(new ScenePoint(0, -100), model.Draft!.Style.Position!.Offset);
        model.Position.OffsetX.RawText = "7e-";

        model.PreviewText = "One line";

        Assert.Equal("7e-", model.Position.OffsetX.RawText);
        Assert.Equal(50, model.Position.Geometry!.GlyphSize.Y);
    }

    [Fact]
    public void ValidMarginsRefreshBothPlacementAxesWhileInvalidMarginsKeepTheLastMeasurement()
    {
        var original = CreatePreset();
        original = original with { Style = original.Style with { Scaling = new() { ReferenceWidth = 1280, ReferenceHeight = 720 } } };
        var model = CreateModel(original);
        var measurements = 0;
        model.SetPositionMeasurement(preset =>
        {
            measurements++;
            var margins = preset.Style.Margins;
            return new(new() { Offset = new((margins.Left - margins.Right) / 2, -margins.Vertical) },
                new(new(1280, 720), new(), new(1280 - margins.Left - margins.Right, 100), new()));
        });
        var initialMeasurements = measurements;

        model.Margins.Left.RawText = "13";
        model.Margins.Right.RawText = "41";
        model.Margins.Vertical.RawText = "27";

        Assert.Equal(initialMeasurements + 3, measurements);
        Assert.Equal(new ScenePoint(-14, -27), model.Position.DiagramPosition!.Offset);
        Assert.Equal(1226, model.Position.Geometry!.GlyphSize.X);
        Assert.Equal(1280, model.CanvasWidth);
        Assert.Equal(720, model.CanvasHeight);
        var geometry = model.Position.Geometry;
        model.Margins.Left.RawText = "7e-";
        model.Margins.Vertical.RawText = "-1";

        Assert.Equal(initialMeasurements + 3, measurements);
        Assert.Same(geometry, model.Position.Geometry);
        Assert.False(model.TryCreatePreviewPreset(out _));
    }

    [Fact]
    public void MarginEditsPreserveExplicitPositionAndItsIncompleteCoordinateDraft()
    {
        var original = CreatePreset() with { Style = new() { Position = new() { Offset = new(37, 29) } } };
        var model = CreateModel(original);
        model.Position.OffsetX.RawText = "7e-";

        model.Margins.Right.RawText = "81";

        Assert.Equal(original.Style.Position, model.Draft!.Style.Position);
        Assert.Equal("7e-", model.Position.OffsetX.RawText);
        Assert.Equal(81, model.Draft.Style.Margins.Right);
        Assert.False(model.TryCreatePreviewPreset(out _));
    }

    private static StyleLibraryViewModel CreateModel(SubtitleStylePreset preset)
    {
        var model = new StyleLibraryViewModel();
        model.UpdateStyles([preset]);
        return model;
    }

    private static ColorDraft GetColor(StyleLibraryViewModel model, string field)
    {
        return field switch
        {
            "Fill" => model.FillDraft,
            "Stroke" => model.StrokeDraft,
            "Shadow" => model.ShadowDraft,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

    private static SceneColor GetColor(SubtitleStyle style, string field)
    {
        return field switch
        {
            "Fill" => style.Fill,
            "Stroke" => style.Stroke,
            "Shadow" => style.ShadowColor,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

    private static void SetNumber(StyleLibraryViewModel model, string field, string rawText)
    {
        switch (field)
        {
            case "FontSize":
                model.FontSizeText = rawText;
                break;
            case "StrokeWidth":
                model.StrokeWidthText = rawText;
                break;
            case "MarginLeft":
                model.Margins.Left.RawText = rawText;
                break;
            case "MarginRight":
                model.Margins.Right.RawText = rawText;
                break;
            case "MarginVertical":
                model.Margins.Vertical.RawText = rawText;
                break;
            case "LineHeight":
                model.LineHeightText = rawText;
                break;
            case "ShadowBlur":
                model.ShadowBlurText = rawText;
                break;
            case "ShadowX":
                model.ShadowXText = rawText;
                break;
            case "ShadowY":
                model.ShadowYText = rawText;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field));
        }
    }

    private static string GetNumber(StyleLibraryViewModel model, string field)
    {
        return field switch
        {
            "FontSize" => model.FontSizeText,
            "StrokeWidth" => model.StrokeWidthText,
            "MarginLeft" => model.Margins.Left.RawText,
            "MarginRight" => model.Margins.Right.RawText,
            "MarginVertical" => model.Margins.Vertical.RawText,
            "LineHeight" => model.LineHeightText,
            "ShadowBlur" => model.ShadowBlurText,
            "ShadowX" => model.ShadowXText,
            "ShadowY" => model.ShadowYText,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

    private static void AssertRevisionAdvances(StyleLibraryViewModel model, Action change)
    {
        var previous = model.PreviewRevision;

        change();

        Assert.True(model.PreviewRevision > previous);
    }
}
