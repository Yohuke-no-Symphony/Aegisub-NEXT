using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Rendering.Tests.Reference;

public sealed class AssKaraokeSharedAppearanceReferenceTests
{
    [LibassReferenceTheory]
    [InlineData(AnimationProperty.STROKE_WIDTH, @"\bord2\t(0,2000,\bord6)")]
    [InlineData(AnimationProperty.STROKE, @"\3c&HFF0000&\t(0,2000,\3c&H0000FF&)")]
    [InlineData(AnimationProperty.SHADOW_COLOR, @"\4c&HFF0000&\t(0,2000,\4c&H0000FF&)")]
    [InlineData(AnimationProperty.SHADOW_OFFSET, @"\xshad2\yshad3\t(0,2000,\xshad8\yshad9)")]
    [InlineData(AnimationProperty.STROKE_BLUR, @"\blur1\t(0,2000,\blur3)")]
    [InlineData(AnimationProperty.SHADOW_BLUR, @"\blur1\t(0,2000,\blur3)")]
    public void SelectedInactiveAnimationMatchesHandwrittenSharedAssAtInteriorTimes(AnimationProperty property, string tags)
    {
        var source = ReferenceSubtitleProject.Script(@"{\q2\pos(150,100)" + tags + @"}{\kf200}MMMM", outline: "2", shadow: "4");
        var document = ReferenceSubtitleProject.Import(source);
        var layer = Assert.Single(document.Layers);
        var original = Assert.Single(layer.Tracks, track => track.Property == property);
        var inactive = original with { Target = original.Target with { State = SubtitleAnimationState.INACTIVE } };
        var activeValue = property switch
        {
            AnimationProperty.STROKE or AnimationProperty.SHADOW_COLOR => AnimationValue.FromColor(new(0, 1, 0)),
            AnimationProperty.SHADOW_OFFSET => AnimationValue.FromVector(new(30, 40)),
            _ => AnimationValue.FromScalar(20)
        };
        var active = original with
        {
            Target = original.Target with { State = SubtitleAnimationState.ACTIVE },
            InitialValue = original.IsOrdered ? activeValue : null,
            Keyframes = original.Keyframes.Select(frame => frame with { Value = activeValue }).ToImmutableArray(),
            Transforms = original.Transforms.Select(operation => operation with { Id = Guid.NewGuid(), Value = activeValue }).ToImmutableArray()
        };
        document = document with { Layers = [layer with { Tracks = [active, inactive] }] };
        ProjectValidator.Validate(document);
        var written = AssSubtitleFormat.Write(document);
        Assert.Contains(written.Diagnostics, item => item.Code == "Ass.KaraokeAnimation");
        Assert.Contains(@"\kf200", written.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\p1", written.Text, StringComparison.Ordinal);
        using var reference = new LibassReferenceRenderer();
        foreach (var time in new long[] { 250, 500, 750, 1250, 1750, 2250 })
        {
            var expected = reference.Render(source, time);
            var actual = reference.Render(written.Text, time);
            var energy = expected.Energy(3);
            Assert.True(energy > 100 && actual.Energy(3) > 100);
            for (var channel = 0; channel < 4; channel++)
            {
                var difference = 0d;
                for (var index = channel; index < expected.Pixels.Length; index += 4)
                {
                    difference += Math.Abs(actual.Pixels[index] - expected.Pixels[index]);
                }
                Assert.InRange(difference / energy, 0, 0.02);
            }
        }
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Lines);
        Assert.Equal("MMMM", imported.Text);
        Assert.Single(imported.Karaoke);
        Assert.Same(inactive, document.Layers[0].Tracks[1]);
    }
}
