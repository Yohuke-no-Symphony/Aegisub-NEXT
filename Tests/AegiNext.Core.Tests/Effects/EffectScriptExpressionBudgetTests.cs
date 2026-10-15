using AegiNext.Core.Effects;

namespace AegiNext.Core.Tests.Effects;

public sealed class EffectScriptExpressionBudgetTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(64)]
    public void LeftAssociativeConstantsWithinTheDepthBudgetKeepTheirValue(int terms)
    {
        var value = EffectScriptParameterValue.Parse(Sum("1", terms));

        Assert.Equal(terms, value.Number);
    }

    [Fact]
    public void LeftAssociativeConstantAtDepthSixtyFiveFailsBeforeEvaluation()
    {
        var error = Assert.Throws<EffectScriptException>(() => EffectScriptParameterValue.Parse(Sum("1", 65)));

        Assert.Contains("64", error.Message, StringComparison.Ordinal);
        Assert.Contains("预算", error.Message, StringComparison.Ordinal);
        Assert.True(error.Column > 0);
    }

    [Fact]
    public void LeftDeepKeyAstFailsWithASourceLocationBeforeUnknownSymbolInference()
    {
        var source = $$"""
            effect "expression-budget" version 3
            short-clip compress
            scope whole current
                segment all flex 1
                    at 0 rotation {{Sum("unknown", 65)}}
                    at 1 rotation 0
                end
            end
            """;

        var error = Assert.Throws<EffectScriptException>(() => EffectScriptParser.Parse(source));

        Assert.Contains("64", error.Message, StringComparison.Ordinal);
        Assert.Contains("预算", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("未知变量", error.Message, StringComparison.Ordinal);
        Assert.Equal(5, error.Line);
        Assert.True(error.Column > 1);
    }

    [Fact]
    public void ConstructorDepthIncludesItsLeftAssociativeArgument()
    {
        var error = Assert.Throws<EffectScriptException>(() => EffectScriptParameterValue.Parse($"vec2({Sum("1", 64)}, 0)"));

        Assert.Contains("64", error.Message, StringComparison.Ordinal);
        Assert.Contains("预算", error.Message, StringComparison.Ordinal);
    }

    private static string Sum(string term, int count) => string.Join(" + ", Enumerable.Repeat(term, count));
}
