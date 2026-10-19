using AegiNext.Application;
using AegiNext.Application.Presets;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Rendering.Projects;
using AegiNext.Rendering.Fonts;
using AegiNext.SubtitleFormats;

namespace AegiNext.Rendering.Tests;

public sealed class SubtitleScalingWorkflowTests
{
    [Theory]
    [InlineData(1280, 720, 0.5, 48)]
    [InlineData(3840, 2160, 0.5, 144)]
    [InlineData(1080, 1920, 0, 40.5)]
    [InlineData(1080, 1920, 0.5, 72)]
    [InlineData(1080, 1920, 1, 128)]
    public async Task StoredPresetAndProjectAdaptToVideoCanvasAndAssExportsTheRenderedSizes(int width, int height,
        double match, double expectedFontSize)
    {
        var directory = Directory.CreateTempSubdirectory("aegi-scaling-workflow-");
        try
        {
            var fonts = ApplicationFontFixture.Latin();
            var preset = new SubtitleStylePreset(Guid.NewGuid(), "Adaptive", new()
            {
                FontFamily = fonts.FamilyName, FontSize = 72, StrokeWidth = 3, Margins = new(30, 30, 30), WrapMode = SubtitleWrapMode.NO_WRAP,
                Scaling = new() { MatchWidthOrHeight = match }
            });
            var library = SubtitleStylePresetStore.Deserialize(SubtitleStylePresetStore.Serialize(new()
            {
                Styles = new() { Presets = [preset] }, FontFamilies = [fonts]
            }));
            var line = new SubtitleLine { Text = "Adaptive subtitle", End = new(2) };
            var initial = new ProjectDocument { Subtitles = [line], Layers = [new() { SubtitleId = line.Id, End = line.End }] };
            var applied = await SubtitleStylePresetService.ApplyAsync(library.Styles.Presets[0], initial, directory.FullName, [line.Id]);
            var reloaded = ProjectStore.Deserialize(ProjectStore.Serialize(applied));
            var resized = reloaded with { Width = width, Height = height };
            using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(directory.FullName),
                new SystemFontCatalog([fonts], SystemFontCatalog.Empty));

            var layout = renderer.MeasureSubtitleTextLayout(resized, resized.Subtitles[0]);
            var written = AssSubtitleFormat.Write(resized);
            var external = Assert.Single(AssSubtitleFormat.Parse(written.Text, width, height).Lines);

            Assert.Equal(expectedFontSize, layout.Runs[0].Style.FontSize, 10);
            Assert.Equal(expectedFontSize, external.Style.FontSize, 8);
            Assert.Equal(expectedFontSize / 24, external.Style.StrokeWidth, 8);
            Assert.Equal(72, resized.Subtitles[0].Style.FontSize);
            Assert.Equal(preset.Style.Scaling, resized.Subtitles[0].Style.Scaling);
            Assert.Equal(preset.Style, library.Styles.Presets[0].Style);
            Assert.Equal(1920, reloaded.Width);
            Assert.Equal(1080, reloaded.Height);
        }
        finally
        {
            directory.Delete(true);
        }
    }
}
