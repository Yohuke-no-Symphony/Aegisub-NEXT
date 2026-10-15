using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Effects;

public sealed class EffectScriptV3Tests
{
    [Theory]
    [InlineData("VAR duration = 0.1s")]
    [InlineData("Var duration = 0.1s")]
    [InlineData("var duration = 0.1s LABEL \"Duration\"")]
    [InlineData("var duration = 0.1s Label \"Duration\"")]
    [InlineData("var duration = 0.1s OPTIONS [\"Normal\": 0.1s]")]
    [InlineData("var duration = 0.1s Options [\"Normal\": 0.1s]")]
    [InlineData("var duration = 0.1s SUGGEST [\"Normal\": 0.1s]")]
    [InlineData("var duration = 0.1s Suggest [\"Normal\": 0.1s]")]
    public void ParameterKeywordsFollowTheLowercaseGrammar(string declaration)
    {
        var error = Assert.Throws<EffectScriptException>(() => EffectScriptParser.Parse(
            "effect \"lowercase\" version 3\nshort-clip compress\n" + declaration));

        Assert.Equal(3, error.Line);
    }

    [Fact]
    public void LowercaseKeywordsPreserveParameterNamesAndAuthorText()
    {
        var script = EffectScriptParser.Parse("""
            effect "author-case" version 3
            short-clip compress
            var Profile = "VAR OPTIONS" label "Display LABEL" options ["Choice SUGGEST": "VAR OPTIONS"]
            """);
        var parameter = Assert.Single(script.Parameters);

        Assert.Equal(3, script.Version);
        Assert.Equal("Profile", parameter.Name);
        Assert.Equal("Display LABEL", parameter.Label);
        Assert.Equal("Choice SUGGEST", Assert.Single(parameter.Options).Label);
        Assert.Equal("VAR OPTIONS", EffectScriptParameterBinder.Bind(script)["Profile"].Text);
        Assert.Throws<EffectScriptException>(() => EffectScriptParameterBinder.Bind(script,
            new Dictionary<string, EffectScriptParameterValue> { ["profile"] = parameter.DefaultValue }));
    }

    [Fact]
    public void ParametersExposeStrictOptionsAndBindDurationsWithoutRounding()
    {
        var script = EffectScriptParser.Parse("""
            effect "parameters" version 3
            short-clip compress
            var duration = 0.1s label "时长" options ["快":100ms, "慢":200ms]
            scope whole current
                segment enter fixed duration
                    at 0 opacity 0
                    at 1 opacity base
                end
                segment rest flex 1
                end
            end
            """);
        var parameter = Assert.Single(script.Parameters);
        Assert.Equal("时长", parameter.Label);
        Assert.Equal(new MediaTime(1, 10), EffectScriptParameterBinder.Bind(script)["duration"].Duration);
        Assert.Throws<EffectScriptException>(() => EffectScriptParameterBinder.Bind(script,
            new Dictionary<string, EffectScriptParameterValue> { ["duration"] = EffectScriptParameterValue.FromDuration(new(3, 10)) }));
    }

    [Fact]
    public void ExplicitLoopPositionsRetainExactThirdsAndSymbolicBase()
    {
        var script = EffectScriptParser.Parse("""
            effect "thirds" version 3
            short-clip compress
            scope whole current
                segment all flex 1
                    for i in range(0, 4)
                        at i / 3 rotation offset(i * 30)
                    end
                end
            end
            """);
        var layer = new ProjectLayer { End = new(1) };
        var instance = new EffectInstance { Source = "", Start = new(0), End = new(1) };
        var result = EffectScriptCompiler.CompileInstance(script, instance, layer, null);
        var channel = Assert.Single(result.Channels);
        Assert.Equal(new MediaTime[] { new(0), new(1, 3), new(2, 3), new(1) }, channel.Offset.Keyframes.Select(key => key.Time));
        Assert.All(channel.Multiplier.Keyframes, key => Assert.Equal(1, key.Value.Scalar));
    }
}
