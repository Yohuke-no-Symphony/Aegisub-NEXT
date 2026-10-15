using System.Collections.Immutable;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

internal static class EffectInstanceTestSources
{
    internal const string SOURCE = """
        effect "instance-test" version 3
        short-clip compress
        var amount = 0.5 options ["半":0.5,"全":1]
        var duration = 100ms suggest ["快":100ms,"慢":200ms]
        scope clip current
            segment enter fixed duration
                at 0 opacity amount
                at 1 opacity base
            end
            segment rest flex 1
            end
        end
        """;

    internal const string GROUPED = """
        effect "grouped-instance-test" version 3
        short-clip compress
        scope letters current
            unit grapheme
            segment pulse fixed 100ms pingpong
                at 0 scale base
                at 1 scale factor(1.25, 1.25)
            end
            segment rest flex 1
            end
        end
        """;

    internal static ProjectDocument Document(int count = 1)
    {
        var subtitles = Enumerable.Range(0, count).Select(index => new SubtitleLine
        {
            Text = "ABCD", Start = new(index * 5), End = new(index * 5 + 4)
        }).ToImmutableArray();
        return new()
        {
            Subtitles = subtitles,
            Layers = subtitles.Select(line => new ProjectLayer
            {
                Id = line.Id, SubtitleId = line.Id, Start = line.Start, End = line.End
            }).ToImmutableArray()
        };
    }

    internal static ImmutableDictionary<string, EffectScriptParameterValue> Parameters(decimal amount = 0.5m,
        MediaTime? duration = null)
    {
        return new Dictionary<string, EffectScriptParameterValue>
        {
            ["amount"] = EffectScriptParameterValue.FromNumber(amount),
            ["duration"] = EffectScriptParameterValue.FromDuration(duration ?? new(1, 10))
        }.ToImmutableDictionary(StringComparer.Ordinal);
    }
}
