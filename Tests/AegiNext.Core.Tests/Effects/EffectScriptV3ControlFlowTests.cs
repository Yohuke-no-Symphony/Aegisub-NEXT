using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Effects;

public sealed class EffectScriptV3ControlFlowTests
{
    [Theory]
    [InlineData("version 1")]
    [InlineData("version 2")]
    public void PublicSourceEntryAcceptsOnlyVersionThree(string version)
    {
        Assert.Throws<EffectScriptException>(() => EffectScriptParser.Parse("effect \"old\" " + version + "\nshort-clip compress"));
    }

    [Fact]
    public void SuggestionsAllowCustomValuesAndHeaderOrderIsFlexible()
    {
        var script = EffectScriptParser.Parse("""
            effect "suggestions" version 3
            var amount = 0.5 label "Amount" suggest ["Half":0.5, "Full":1]
            short-clip compress
            """);
        var supplied = ImmutableDictionary<string, EffectScriptParameterValue>.Empty.Add("amount", EffectScriptParameterValue.FromNumber(0.75m));
        Assert.Equal(0.75m, EffectScriptParameterBinder.Bind(script, supplied)["amount"].Number);
        Assert.Throws<EffectScriptException>(() => EffectScriptParameterBinder.Bind(script,
            supplied.Add("unknown", EffectScriptParameterValue.FromNumber(1))));
    }

    [Theory]
    [InlineData("var x = 1 options [\"a\":2]")]
    [InlineData("var x = 1 options [\"a\":1, \"b\":1]")]
    [InlineData("var x = 1 options [\"a\":1s]")]
    [InlineData("var x = 1\nVAR x = 2")]
    [InlineData("var group = 1")]
    [InlineData("var x = vec2()")]
    public void InvalidParameterDeclarationsFailBeforeApplication(string declaration)
    {
        Assert.Throws<EffectScriptException>(() => EffectScriptParser.Parse("effect \"invalid\" version 3\nshort-clip compress\n" + declaration));
    }

    [Fact]
    public void EveryBranchChecksSymbolsAndUnitsButUntakenBranchesDoNotEvaluate()
    {
        var valid = """
            effect "branches" version 3
            short-clip compress
            if false and (1 / 0 > 1)
                scope skipped current
                    segment all flex 1
                        at 0 rotation 0
                        at 1 rotation 1 / 0
                    end
                end
            elif true
                scope chosen current
                    segment all flex 1
                        at 0 rotation offset(0)
                        at 1 rotation offset(30)
                    end
                end
            else
                scope fallback current
                    segment all flex 1
                        at 0 rotation 0
                        at 1 rotation 1
                    end
                end
            end
            """;
        var result = Compile(valid);
        Assert.Equal(30, Assert.Single(result.Channels).Offset.Keyframes[^1].Value.Scalar);
        Assert.Throws<EffectScriptException>(() => EffectScriptParser.Parse(valid.Replace("1 / 0", "unknown", StringComparison.Ordinal)));
        Assert.Throws<EffectScriptException>(() => EffectScriptParser.Parse(valid.Replace("rotation 1 / 0", "rotation 1s", StringComparison.Ordinal)));
    }

    [Fact]
    public void NegativeLoopsGenerateStableDistinctScopeIdentitiesAndShrinkCleanly()
    {
        var source = """
            effect "scope-loop" version 3
            short-clip compress
            var count = 3
            for i in range(count, 0, -1)
                scope letter range(i, 1)
                    segment all flex 1
                        at 0 rotation offset(i)
                        at 1 rotation offset(i)
                    end
                end
            end
            """;
        var line = new SubtitleLine { Text = "ABC", End = new(1) };
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End };
        var script = EffectScriptParser.Parse(source);
        var instance = new EffectInstance { Start = new(0), End = new(1), Source = source };
        var first = EffectScriptCompiler.CompileInstance(script, instance, layer, line);
        var again = EffectScriptCompiler.CompileInstance(script, instance, layer, line);
        Assert.Equal(first.Ranges.Select(range => range.Id), again.Ranges.Select(range => range.Id));
        Assert.Equal(3, first.Ranges.Select(range => range.Id).Distinct().Count());
        var smaller = EffectScriptCompiler.CompileInstance(script, instance with
        {
            Parameters = instance.Parameters.Add("count", EffectScriptParameterValue.FromNumber(1))
        }, layer, line);
        Assert.Single(smaller.Ranges);
        Assert.Contains(smaller.Ranges[0].Id, first.Ranges.Select(range => range.Id));
        Assert.Empty(line.AnimationRanges);
        Assert.Empty(layer.Tracks);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void EmptyGroupsKeepTheirOriginalStaggerSlots(int omitted, int firstIndex)
    {
        var source = $$"""
            effect "empty-slots" version 3
            short-clip compress
            scope letters current
                unit grapheme
                stagger 100ms
                if group.index != {{omitted}}
                    segment enter fixed 100ms
                        at 0 rotation offset(0)
                        at 1 rotation offset(20)
                    end
                    segment rest flex 1
                    end
                end
            end
            """;
        var line = new SubtitleLine { Text = "ABC", End = new(1) };
        var result = Compile(source, line);
        Assert.Equal(2, result.Ranges.Length);
        Assert.Equal(new MediaTime(firstIndex, 10), result.Channels[0].Start);
        Assert.Equal(new MediaTime(1, 5), result.Channels[1].Start);
    }

    [Fact]
    public void GroupSpecificSegmentsUseOneSharedCompressionEnvelope()
    {
        var source = """
            effect "envelope" version 3
            short-clip compress
            scope letters current
                unit grapheme
                stagger 100ms
                segment enter fixed (group.index + 1) * 100ms
                    at 0 rotation offset(0)
                    at 1 rotation offset(30)
                end
                segment rest flex 1
                end
            end
            """;
        var line = new SubtitleLine { Text = "ABC", End = new(1, 4) };
        var result = Compile(source, line);
        Assert.Equal(new MediaTime[] { new(0), new(1, 20), new(1, 10) }, result.Channels.Select(channel => channel.Start));
        Assert.Equal(new MediaTime[] { new(1, 20), new(3, 20), new(1, 4) }, result.Channels.Select(channel => channel.End));
        Assert.Throws<EffectScriptException>(() => Compile(source.Replace("compress", "reject", StringComparison.Ordinal), line));
    }

    [Fact]
    public void GroupIndexUsesFilteredStartOrderAndCannotDetermineGrouping()
    {
        var source = """
            effect "group-index" version 3
            short-clip compress
            scope letters current
                unit grapheme
                order reverse
                segment all flex 1
                    at 0 rotation offset(group.index)
                    at 1 rotation offset(group.count)
                end
            end
            """;
        var line = new SubtitleLine { Text = " A 😀 ", End = new(1) };
        var result = Compile(source, line);
        Assert.Equal(3, result.Ranges[0].Utf16Start);
        Assert.Equal(0, result.Channels[0].Offset.Keyframes[0].Value.Scalar);
        Assert.All(result.Channels, channel => Assert.Equal(2, channel.Offset.Keyframes[^1].Value.Scalar));
        Assert.Throws<EffectScriptException>(() => EffectScriptParser.Parse(source.Replace("unit grapheme", "unit chunk(group.count)", StringComparison.Ordinal)));
    }

    [Fact]
    public void EmptyExpansionAndCancelledCompilationNeverPublishDerivedData()
    {
        const string source = "effect \"empty\" version 3\nshort-clip compress\nfor i in range(0, 0)\nend";
        var result = Compile(source);
        Assert.Empty(result.Ranges);
        Assert.Empty(result.Channels);
        var script = EffectScriptParser.Parse(source);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => EffectScriptCompiler.CompileInstance(script,
            new() { Start = new(0), End = new(1) }, new() { End = new(1) }, null, cancellation.Token));
    }

    [Theory]
    [InlineData("range(0, 4097)")]
    [InlineData("range(0, 1, 0)")]
    public void InvalidOrExcessiveLoopsFailWithoutTruncation(string range)
    {
        var source = "effect \"loop-budget\" version 3\nshort-clip compress\nfor i in " + range + "\nend";
        Assert.Throws<EffectScriptException>(() => Compile(source));
    }

    [Theory]
    [InlineData("vec2(2, 3)", "scale")]
    [InlineData("vec3(10, 20, 720)", "rotation")]
    [InlineData("rgba(2, 0.25, 0.5, 1)", "fill")]
    public void VectorAndColorParametersKeepTheirDimensions(string value, string property)
    {
        var source = $$"""
            effect "dimensions" version 3
            short-clip compress
            var value = {{value}}
            scope whole current
                segment all flex 1
                    at 0 {{property}} value
                    at 1 {{property}} value
                end
            end
            """;
        var channel = Assert.Single(Compile(source).Channels);
        var expected = EffectScriptParameterValue.Parse(value).Animation!.Value;
        Assert.Equal(expected, channel.Offset.Keyframes[0].Value);
        Assert.Equal(expected.Kind, channel.Multiplier.Keyframes[0].Value.Kind);
    }

    [Fact]
    public void CompatibilityScansUntakenBranchesRatherThanOneArbitraryDefaultClip()
    {
        var source = """
            effect "conditional-text" version 3
            short-clip compress
            if clip.duration > 10s
                scope words current
                    unit word
                    segment all flex 1
                        at 0 scale base
                        at 1 scale base
                    end
                end
            end
            """;
        Assert.True(EffectScriptCompatibility.RequiresSubtitle(EffectScriptParser.Parse(source)));
    }

    [Fact]
    public void OverlappingScopesForOneTargetAreRejectedInsideAnInstance()
    {
        var source = """
            effect "overlap" version 3
            short-clip compress
            for i in range(0, 2)
                scope whole current
                    segment all flex 1
                        at 0 rotation 0
                        at 1 rotation 0
                    end
                end
            end
            """;
        Assert.Throws<EffectScriptException>(() => Compile(source));
    }

    [Fact]
    public void AllBuiltinsCompileWithDefaultsAtTinyNormalAndLongDurations()
    {
        foreach (var template in BuiltinEffectScripts.Templates)
        {
            Assert.Equal(3, template.Script.Version);
            Assert.NotEmpty(template.Script.Parameters);
            foreach (var duration in new MediaTime[] { new(1, 1000), new(1), new(10) })
            {
                var line = new SubtitleLine { Text = "A😀é", End = duration };
                var layer = new ProjectLayer { SubtitleId = line.Id, End = duration };
                var instance = new EffectInstance { Source = template.Source, Start = new(0), End = duration };
                var result = EffectScriptCompiler.CompileInstance(template.Script, instance, layer, line);
                Assert.NotEmpty(result.Channels);
                Assert.All(result.Channels, channel => Assert.True(channel.Start >= MediaTime.Zero && channel.End <= duration));
            }
        }
    }

    [Fact]
    public void TheDeliveredParameterizedLetterExampleCompilesWithItsRealSource()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "parameterized-letters.aegifx"));
        foreach (var duration in new MediaTime[] { new(1, 1000), new(1), new(10) })
        {
            var line = new SubtitleLine { Text = "A😀é", End = duration };
            var result = Compile(source, line);
            Assert.Equal(3, result.Ranges.Length);
            Assert.All(result.Channels, channel => Assert.True(channel.Start >= MediaTime.Zero && channel.End <= duration));
        }
    }

    [Fact]
    public void FullVectorRotationPromotesSymbolicBaseAndKeepsThreeComponents()
    {
        var channel = Assert.Single(Compile("""
            effect "rotation-vector" version 3
            short-clip compress
            scope whole current
                segment all flex 1
                    at 0 rotation base
                    at 1 rotation offset(vec3(10, 20, 720))
                end
            end
            """).Channels);
        Assert.Equal(AnimationProperty.ROTATION_3D, channel.Target.Property);
        Assert.Equal(new SceneVector3(1, 1, 1), channel.Multiplier.Keyframes[0].Value.Vector3);
        Assert.Equal(new SceneVector3(10, 20, 720), channel.Offset.Keyframes[^1].Value.Vector3);
    }

    [Fact]
    public void PingPongReversesTheOriginalPowerCurve()
    {
        var channel = Assert.Single(Compile("""
            effect "power-return" version 3
            short-clip compress
            scope whole current
                segment bounce fixed 100ms pingpong
                    at 0 rotation offset(0) power(2)
                    at 1 rotation offset(90)
                end
                segment rest flex 1
                end
            end
            """).Channels);
        Assert.Equal(22.5, SceneEvaluator.EvaluateScalarTrack(channel.Offset, new(1, 20)), 10);
        Assert.Equal(22.5, SceneEvaluator.EvaluateScalarTrack(channel.Offset, new(3, 20)), 10);
    }

    [Fact]
    public void HugeCycleExpansionReportsTheSourceBeforeAnyEmission()
    {
        var source = """
            effect "cycle-budget" version 3
            short-clip compress
            scope whole current
                segment pulse flex 1 cycle 0.000001ms pingpong
                    at 0 rotation offset(0)
                    at 1 rotation offset(1)
                end
            end
            """;
        var error = Assert.Throws<EffectScriptException>(() => Compile(source));
        Assert.Contains("预算", error.Message, StringComparison.Ordinal);
        Assert.True(error.Line > 0);
    }

    [Theory]
    [InlineData("opacity", "2")]
    [InlineData("fill", "rgba(1, 1, 1, 2)")]
    public void ImpossibleAbsoluteValuesFailAtInstanceCompilation(string property, string value)
    {
        var source = $$"""
            effect "absolute-bounds" version 3
            short-clip compress
            scope whole current
                segment all flex 1
                    at 0 {{property}} {{value}}
                    at 1 {{property}} {{value}}
                end
            end
            """;
        Assert.Throws<EffectScriptException>(() => Compile(source));
    }

    private static CompiledEffectScript Compile(string source, SubtitleLine? line = null)
    {
        var end = line?.End ?? new(1);
        var layer = new ProjectLayer { End = end, SubtitleId = line?.Id };
        return EffectScriptCompiler.CompileInstance(EffectScriptParser.Parse(source), new() { Source = source, Start = new(0), End = end }, layer, line);
    }
}
