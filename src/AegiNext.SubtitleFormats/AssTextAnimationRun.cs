using System.Collections.Immutable;

namespace AegiNext.SubtitleFormats;

internal sealed record AssTextAnimationRun(int Utf16Start, int Utf16Length, bool Karaoke,
    ImmutableDictionary<string, AssTextAnimationSnapshot> Channels)
{
    internal bool Equivalent(AssTextAnimationRun other) => Karaoke == other.Karaoke &&
        Channels.All(pair => pair.Value.Equivalent(other.Channels[pair.Key]));
}
