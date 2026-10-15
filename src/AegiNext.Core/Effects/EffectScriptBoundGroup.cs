using System.Collections.Immutable;

namespace AegiNext.Core.Effects;

internal sealed record EffectScriptBoundGroup(Guid? RangeId, ImmutableArray<EffectScriptBoundSegment> Segments);
