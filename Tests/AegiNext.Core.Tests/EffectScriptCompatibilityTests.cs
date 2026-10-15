using AegiNext.Core.Effects;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests;

public sealed class EffectScriptCompatibilityTests
{
    [Theory]
    [InlineData("fade-in-out", false)]
    [InlineData("fade-in", false)]
    [InlineData("fade-out", false)]
    [InlineData("pop-in", false)]
    [InlineData("pop-out", false)]
    [InlineData("slide-in", false)]
    [InlineData("slide-out", false)]
    [InlineData("letter-bounce", true)]
    [InlineData("letter-pulse", true)]
    public void BuiltinRequirementsSeparateTextGroupingFromWholeClipEffects(string id, bool requiresSubtitle)
    {
        Assert.Equal(requiresSubtitle, EffectScriptCompatibility.RequiresSubtitle(BuiltinEffectScripts.Get(id).Script));
    }

    [Theory]
    [InlineData("current", "group", "normal", "opacity", false)]
    [InlineData("current", "group", "normal", "font-size", true)]
    [InlineData("current", "word", "normal", "opacity", true)]
    [InlineData("subtitle", "group", "normal", "opacity", true)]
    [InlineData("range(1, 2)", "group", "normal", "opacity", true)]
    [InlineData("current", "group", "active", "opacity", true)]
    public void ScopedRequirementsMatchCompilerTextRestrictions(string target, string unit, string state,
        string property, bool requiresSubtitle)
    {
        var source = $$"""
            effect "compatibility" version 3
            short-clip compress
            scope target {{target}}
                unit {{unit}}
                state {{state}}
                segment hold flex 1
                    at 0 {{property}} base
                    at 1 {{property}} base
                end
            end
            """;
        var script = EffectScriptParser.Parse(source);
        var instance = new EffectInstance { Source = source };
        Assert.Equal(requiresSubtitle, EffectScriptCompatibility.RequiresSubtitle(script));
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 60) };
        if (requiresSubtitle)
        {
            Assert.Throws<EffectScriptException>(() => EffectScriptCompiler.CompileInstance(script, instance, layer, null));
        }
        else
        {
            Assert.Single(EffectScriptCompiler.CompileInstance(script, instance, layer, null).Channels);
        }
    }
}
