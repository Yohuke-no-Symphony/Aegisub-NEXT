using AegiNext.Core.Timing;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Rendering;

internal interface IEffectPresetPreviewRenderer : IDisposable
{
    /// <summary>在拥有渲染器的线程上渲染隔离样例；结果只包含不可变像素。</summary>
    SdrVideoFrame Render(EffectPresetPreviewRequest request, MediaTime position, CancellationToken cancellationToken);
}
