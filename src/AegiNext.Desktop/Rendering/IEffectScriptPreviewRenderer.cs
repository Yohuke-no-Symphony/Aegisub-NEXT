using AegiNext.Core.Timing;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Rendering;

internal interface IEffectScriptPreviewRenderer : IDisposable
{
    /// <summary>在创建线程渲染冻结的样例；返回与原生表面独立的不可变像素。</summary>
    SdrVideoFrame Render(EffectScriptPreviewRequest request, MediaTime position, CancellationToken cancellationToken);
}
