using AegiNext.Core.Effects;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Effects;

public sealed class EffectScriptLegacyCompilerTests
{
    [Fact]
    public void TracksOnlyCompilerRejectsScopeScriptsInsteadOfDiscardingTheirRanges()
    {
        var script = EffectScriptRegressionParser.Parse("""
            effect "scoped" version 2
            short-clip compress
            scope letters subtitle
                unit grapheme
                segment pulse fixed 150ms pingpong
                    at 0 scale base ease-in-out
                    at 1 scale factor(1.25, 1.25)
                end
                segment rest flex 1
                end
            end
            """);

        var error = Assert.Throws<EffectScriptException>(() => EffectScriptCompiler.Compile(script, new ProjectLayer()));
        Assert.Contains("CompileTarget", error.Message, StringComparison.Ordinal);
    }
}
