using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal sealed record AssNumericOperation(MediaTime Start, MediaTime End, double Value, double Exponent);
