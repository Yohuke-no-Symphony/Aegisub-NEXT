using System.Collections.Immutable;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Effects;

internal sealed record EffectScriptBoundSegment(string Name, MediaTime? FixedDuration, EffectScriptRational Weight,
    int RepeatCount, bool PingPong, MediaTime? CycleDuration, ImmutableArray<EffectScriptBoundKey> Keys, int Line, int Column);
