using AegiNext.Core.Timing;

namespace AegiNext.Export;

/// <summary>音轨处理的位置、可选完成比例和当前阶段。</summary>
public sealed record AudioExportProgress(MediaTime Position, double? Fraction, string Stage);
