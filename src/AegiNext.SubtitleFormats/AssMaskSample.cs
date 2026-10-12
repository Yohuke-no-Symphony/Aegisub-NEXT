using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal sealed record AssMaskSample(MediaTime Start, MediaTime End, MediaTime ContentTime, string Tags, bool Expanded);
