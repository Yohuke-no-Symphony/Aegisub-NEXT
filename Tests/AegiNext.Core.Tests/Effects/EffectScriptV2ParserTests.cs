using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Effects;

public sealed class EffectScriptV2ParserTests
{
    private static readonly string[] splitLiterals = ["#", ",", "\"", "\\", "\r\n", "、", "\t"];

    [Fact]
    public void NamedScopesKeepIndependentTargetsGroupingTimingAndSegmentModifiers()
    {
        var script = EffectScriptRegressionParser.Parse("""
            effect "grouped-pop" version 2
            short-clip compress
            scope fade subtitle
                segment enter fixed 300ms
                    at 0 opacity 0 ease-out
                    at 1 opacity base
                end
                segment rest flex 1
                end
            end
            scope letters range(2, 4)
                unit chunk(2)
                delay 0ms
                stagger 60ms
                order reverse
                state normal
                segment pulse fixed 150ms repeat 2 pingpong
                    at 0 scale base power(2)
                    at 1 scale factor(1.25, 1.25)
                end
                segment rest flex 1
                end
            end
            """);

        Assert.Equal(2, script.Version);
        Assert.Empty(script.Segments);
        Assert.Equal(2, script.Scopes.Length);
        Assert.Equal(EffectScriptTargetKind.SUBTITLE, script.Scopes[0].Target.Kind);
        var scope = script.Scopes[1];
        Assert.Equal("letters", scope.Name);
        Assert.Equal(new EffectScriptTargetSelector(EffectScriptTargetKind.RANGE, 2, 4), scope.Target);
        Assert.Equal(EffectScriptUnitKind.CHUNK, scope.Unit.Kind);
        Assert.Equal(2, scope.Unit.Count);
        Assert.Equal(MediaTime.Zero, scope.Delay);
        Assert.Equal(new MediaTime(3, 50), scope.Stagger);
        Assert.Equal(EffectScriptOrder.REVERSE, scope.Order);
        var pulse = scope.Segments[0];
        Assert.Equal(new MediaTime(3, 20), pulse.FixedDuration);
        Assert.Equal(2, pulse.RepeatCount);
        Assert.True(pulse.PingPong);
        Assert.Null(pulse.CycleDuration);
        Assert.Equal(KeyframeInterpolation.POWER, pulse.Keyframes[0].Interpolation);
        Assert.Equal(2, pulse.Keyframes[0].Exponent);
        Assert.Equal(1.25, pulse.Keyframes[1].Value.X);
        Assert.Equal(11, scope.Line);
        Assert.Equal(1, scope.Column);
    }

    [Fact]
    public void ScopeAndSegmentDefaultsPreserveTheExistingConstructorContract()
    {
        var v1 = EffectScriptRegressionParser.Parse(EffectScriptRegressionParser.POP_IN);
        Assert.Equal(1, v1.Version);
        Assert.Empty(v1.Scopes);
        Assert.All(v1.Segments, segment =>
        {
            Assert.Equal(1, segment.RepeatCount);
            Assert.False(segment.PingPong);
            Assert.Null(segment.CycleDuration);
        });

        var scope = Assert.Single(EffectScriptRegressionParser.Parse(Source()).Scopes);
        Assert.Equal(EffectScriptTargetKind.CURRENT, scope.Target.Kind);
        Assert.Equal(EffectScriptUnitKind.GROUP, scope.Unit.Kind);
        Assert.Equal(MediaTime.Zero, scope.Delay);
        Assert.Equal(MediaTime.Zero, scope.Stagger);
        Assert.Equal(EffectScriptOrder.FORWARD, scope.Order);
        Assert.Equal(SubtitleAnimationState.NORMAL, scope.State);
    }

    [Theory]
    [InlineData("group", EffectScriptUnitKind.GROUP)]
    [InlineData("grapheme", EffectScriptUnitKind.GRAPHEME)]
    [InlineData("word", EffectScriptUnitKind.WORD)]
    [InlineData("line", EffectScriptUnitKind.LINE)]
    [InlineData("paragraph", EffectScriptUnitKind.PARAGRAPH)]
    public void SimpleUnitsAreParsedWithoutInferringTextSegmentation(string name, EffectScriptUnitKind kind)
    {
        var scope = Assert.Single(EffectScriptRegressionParser.Parse(Source($"unit {name}")).Scopes);
        Assert.Equal(kind, scope.Unit.Kind);
    }

    [Fact]
    public void SplitLiteralsKeepQuotedHashesEscapesCommasAndFullNewlines()
    {
        var source = Source("""unit split("#", ",", "\"", "\\", "\r\n", "、", "\t") # outside comment""");
        var scope = Assert.Single(EffectScriptRegressionParser.Parse("\uFEFF# header\r\n" + source.Replace("\n", "\r\n", StringComparison.Ordinal)).Scopes);

        Assert.Equal(EffectScriptUnitKind.SPLIT, scope.Unit.Kind);
        Assert.Equal(splitLiterals, scope.Unit.Delimiters);
    }

    [Theory]
    [InlineData("active", SubtitleAnimationState.ACTIVE)]
    [InlineData("inactive", SubtitleAnimationState.INACTIVE)]
    public void ScopeStateIsExplicitAndIndependentFromTheCaller(string value, SubtitleAnimationState state)
    {
        var scope = Assert.Single(EffectScriptRegressionParser.Parse(Source($"state {value}")).Scopes);
        Assert.Equal(state, scope.State);
    }

    [Fact]
    public void FlexCyclesRecordAFullPeriodAndUseInvariantDecimalWeights()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var source = Source(segment: "wave flex 0.5 cycle 0.000001ms pingpong");
            var segment = Assert.Single(Assert.Single(EffectScriptRegressionParser.Parse(source).Scopes).Segments);

            Assert.Null(segment.FixedDuration);
            Assert.Equal(0.5m, segment.FlexWeight);
            Assert.Equal(new MediaTime(1, 1000000000), segment.CycleDuration);
            Assert.Equal(1, segment.RepeatCount);
            Assert.True(segment.PingPong);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("unit chunk(0)")]
    [InlineData("unit chunk(1.5)")]
    [InlineData("unit split(\"\")")]
    [InlineData("unit split(\"、\", \"、\")")]
    [InlineData("unit split(\"\\q\")")]
    [InlineData("unit split(\"\\u0000\")")]
    [InlineData("unit split(\"\\uD800\")")]
    [InlineData("unit split(\"\\b\")")]
    [InlineData("unit split(\"\\f\")")]
    [InlineData("unit split(\"\\/\")")]
    [InlineData("unit split(\"\\u3001\")")]
    [InlineData("unit split(\"unfinished)")]
    [InlineData("unit random")]
    [InlineData("delay -1ms")]
    [InlineData("delay 86401s")]
    [InlineData("stagger -1ms")]
    [InlineData("stagger 0.0000001s")]
    [InlineData("order random")]
    [InlineData("state highlighted")]
    [InlineData("unknown value")]
    [InlineData("unit word\n    unit line")]
    [InlineData("delay 0ms\n    delay 0ms")]
    public void InvalidOrDuplicateScopeFieldsAreLocatedAndRejected(string directive)
    {
        var error = Assert.Throws<EffectScriptException>(() => EffectScriptRegressionParser.Parse(Source(directive)));
        Assert.True(error.Line >= 4);
        Assert.True(error.Column > 0);
    }

    [Theory]
    [InlineData("pulse fixed 300ms repeat 0")]
    [InlineData("pulse fixed 300ms repeat 1.5")]
    [InlineData("pulse fixed 300ms repeat 2147483648")]
    [InlineData("pulse fixed 300ms repeat 2 repeat 2")]
    [InlineData("pulse fixed 300ms cycle 300ms")]
    [InlineData("pulse flex 1 repeat 1")]
    [InlineData("pulse flex 1 pingpong")]
    [InlineData("pulse flex 1 cycle 0ms")]
    [InlineData("pulse flex 1 cycle 1s cycle 2s")]
    [InlineData("pulse flex 1 cycle 1s pingpong pingpong")]
    public void InvalidSegmentModifiersAreRejectedBeforeCompilation(string segment)
    {
        var error = Assert.Throws<EffectScriptException>(() => EffectScriptRegressionParser.Parse(Source(segment: segment)));
        Assert.Equal(4, error.Line);
        Assert.True(error.Column > 0);
    }

    [Theory]
    [InlineData("range(0, 1)")]
    [InlineData("range(1, 0)")]
    [InlineData("range(1.5, 2)")]
    [InlineData("range(2147483647, 2)")]
    [InlineData("unknown")]
    public void InvalidFixedRangeSelectorsAreRejected(string target)
    {
        var error = Assert.Throws<EffectScriptException>(() => EffectScriptRegressionParser.Parse(Source(target: target)));
        Assert.Equal(3, error.Line);
    }

    [Fact]
    public void SharedPropertyParserPreservesExactIndentedDiagnostics()
    {
        var source = Source().Replace("at 0 scale", "at 0 unknown", StringComparison.Ordinal);
        var error = Assert.Throws<EffectScriptException>(() => EffectScriptRegressionParser.Parse(source));
        Assert.Equal(5, error.Line);
        Assert.Equal(14, error.Column);
    }

    [Fact]
    public void ScopeFieldsCannotAppearAfterSegmentsAndBothLevelsRequireEnd()
    {
        var source = Source();
        Assert.Throws<EffectScriptException>(() => EffectScriptRegressionParser.Parse(source + "\nend"));
        Assert.Throws<EffectScriptException>(() => EffectScriptRegressionParser.Parse(source[..source.LastIndexOf("end", StringComparison.Ordinal)]));
        Assert.Throws<EffectScriptException>(() => EffectScriptRegressionParser.Parse(source.Replace("    end\nend", "    end\n    unit word\nend", StringComparison.Ordinal)));
    }

    [Fact]
    public void ScopeNamesAreGlobalButSegmentNamesAreLocal()
    {
        var source = Source();
        var body = source[source.IndexOf("scope ", StringComparison.Ordinal)..];
        Assert.Equal(2, EffectScriptRegressionParser.Parse(source + "\n" + body.Replace("scope main", "scope other", StringComparison.Ordinal)).Scopes.Length);
        Assert.Throws<EffectScriptException>(() => EffectScriptRegressionParser.Parse(source + "\n" + body));
        Assert.Throws<EffectScriptException>(() => EffectScriptRegressionParser.Parse(source.Replace("end\nend", "end\n    segment rest flex 1\n    end\nend", StringComparison.Ordinal)));
    }

    [Fact]
    public void VersionTwoRequiresScopesAndEachScopeRequiresKeysAndAFlexSegment()
    {
        Assert.Throws<EffectScriptException>(() => EffectScriptRegressionParser.Parse(Source().Replace("version 2", "version 1", StringComparison.Ordinal)));
        Assert.Throws<EffectScriptException>(() => EffectScriptRegressionParser.Parse(Source(segment: "pulse fixed 1s")));
        Assert.Throws<EffectScriptException>(() => EffectScriptRegressionParser.Parse("effect \"empty\" version 2\nshort-clip compress"));
        Assert.Throws<EffectScriptException>(() => EffectScriptRegressionParser.Parse("effect \"empty\" version 2\nshort-clip compress\nscope main current\nsegment rest flex 1\nend\nend"));
        Assert.Throws<EffectScriptException>(() => EffectScriptRegressionParser.Parse(EffectScriptRegressionParser.POP_IN.Replace("version 1", "version 2", StringComparison.Ordinal)));
    }

    [Fact]
    public void SegmentAndKeyBudgetsAreAggregatedAcrossScopes()
    {
        var valid = EffectScriptRegressionParser.Parse(Source());
        var scope = valid.Scopes[0];
        var duplicatedSegments = Enumerable.Range(0, 65).Select(index => scope with
        {
            Name = "scope-" + index,
            Segments = [scope.Segments[0], scope.Segments[0] with { Name = "other" }]
        }).ToImmutableArray();
        Assert.Throws<EffectScriptException>(() => EffectScriptValidator.Validate(valid with { Scopes = duplicatedSegments }));

        var keys = Enumerable.Range(0, 2049).Select(index => scope.Segments[0].Keyframes[0] with
        {
            Progress = decimal.Round(index / 2048m, 6)
        }).ToImmutableArray();
        var dense = scope with { Segments = [scope.Segments[0] with { Keyframes = keys }] };
        Assert.Throws<EffectScriptException>(() => EffectScriptValidator.Validate(valid with { Scopes = [dense, dense with { Name = "other" }] }));
    }

    [Fact]
    public void PublicModelValidationRejectsCrossVersionShapesAndInvalidMetadata()
    {
        var valid = EffectScriptRegressionParser.Parse(Source());
        var scope = valid.Scopes[0];
        Assert.Throws<EffectScriptException>(() => EffectScriptValidator.Validate(valid with { Version = 3 }));
        Assert.Throws<EffectScriptException>(() => EffectScriptValidator.Validate(valid with { Version = 1 }));
        Assert.Throws<EffectScriptException>(() => EffectScriptValidator.Validate(valid with { Segments = scope.Segments }));
        Assert.Throws<EffectScriptException>(() => EffectScriptValidator.Validate(valid with { Scopes = Enumerable.Repeat(scope, 129).ToImmutableArray() }));
        Assert.Throws<EffectScriptException>(() => EffectScriptValidator.Validate(valid with { Scopes = [scope with { Delay = new(-1) }] }));
        Assert.Throws<EffectScriptException>(() => EffectScriptValidator.Validate(valid with { Scopes = [scope with { Unit = new(EffectScriptUnitKind.CHUNK) { Count = 0 } }] }));
        Assert.Throws<EffectScriptException>(() => EffectScriptValidator.Validate(valid with { Scopes = [scope with { Segments = [scope.Segments[0] with { RepeatCount = 2 }] }] }));

        var v1 = EffectScriptRegressionParser.Parse(EffectScriptRegressionParser.POP_IN);
        Assert.Throws<EffectScriptException>(() => EffectScriptValidator.Validate(v1 with
        {
            Segments = v1.Segments.SetItem(0, v1.Segments[0] with { PingPong = true })
        }));
    }

    private static string Source(string? directive = null, string target = "current", string segment = "rest flex 1")
    {
        var fields = directive is null ? string.Empty : "    " + directive + "\n";
        return $"effect \"parser-case\" version 2\nshort-clip compress\nscope main {target}\n{fields}    segment {segment}\n        at 0 scale base ease-out\n        at 1 scale base\n    end\nend";
    }
}
