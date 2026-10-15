using AegiNext.Core.Effects;

namespace AegiNext.Core.Tests.Effects;

internal static class EffectScriptRegressionParser
{
    internal const string POP_IN = """
        effect "pop-in" version 1
        short-clip compress
        segment enter fixed 250ms
            at 0 scale factor(0.2, 0.2) ease-out
            at 1 scale base
        end
        segment stay flex 1
        end
        """;

    internal const string FADE_IN = """
        effect "fade-in" version 1
        short-clip compress
        segment enter fixed 300ms
            at 0 opacity 0 ease-out
            at 1 opacity base
        end
        segment stay flex 1
        end
        """;

    internal static EffectScript Parse(string source) => source.Contains("version 3", StringComparison.Ordinal)
        ? EffectScriptParser.Parse(source) : EffectScriptParser.ParseLegacy(source);
}
