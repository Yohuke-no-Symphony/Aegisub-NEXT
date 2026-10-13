using AegiNext.Core.Effects;
using AegiNext.Desktop.Rendering;
using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Tests;

internal static class EffectPresetPreviewTestSources
{
    internal const string PATH = """
        effect "sample-path" version 1
        short-clip compress
        segment motion flex 1
            at 0 path-progress 0 linear
            at 1 path-progress 1
        end
        """;

    internal const string MASK = """
        effect "sample-mask" version 1
        short-clip compress
        segment motion flex 1
            at 0 mask-position base linear
            at 1 mask-position offset(20, 0)
        end
        """;

    internal const string RANGE = """
        effect "sample-range" version 2
        short-clip compress
        scope letters range(11, 1)
            segment pulse fixed 100ms pingpong
                at 0 scale base
                at 1 scale factor(1.25, 1.25)
            end
            segment rest flex 1
            end
        end
        """;

    internal const string LONG = """
        effect "long-sample" version 1
        short-clip reject
        segment enter fixed 5s
            at 0 opacity 0
            at 1 opacity base
        end
        segment rest flex 1
        end
        """;

    internal static EffectPresetPreviewRequest Request(string id = "card", long revision = 1, double scaling = 1,
        string? source = null, SystemFontCatalog? catalog = null)
    {
        return new(id, source ?? BuiltinEffectScripts.Get("fade-in").Source, revision, scaling, catalog ?? SystemFontCatalog.Empty);
    }
}
