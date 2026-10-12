using SkiaSharp;

namespace AegiNext.Rendering;

/// <summary>避免 GPU 字形 atlas 对常量颜色折叠时截断 HDR；输出保留预乘线性 RGBA。</summary>
internal sealed class GpuLinearColorShader : IDisposable
{
    private readonly SKRuntimeEffect effect = SKRuntimeEffect.CreateShader(
        "uniform float4 color; half4 main(float2 p) { return half4(color.rgb * color.a, color.a); }", out var error)
        ?? throw new InvalidOperationException($"The GPU linear color shader could not be created: {error}");

    internal void Apply(SKPaint paint, float red, float green, float blue, float alpha)
    {
        using var uniforms = new SKRuntimeEffectUniforms(effect);
        uniforms["color"] = new[] { red, green, blue, alpha };
        using var shader = effect.ToShader(uniforms)
            ?? throw new InvalidOperationException("The GPU linear color shader instance could not be created.");
        paint.Color = SKColors.White;
        paint.Shader = shader;
    }

    public void Dispose() => effect.Dispose();
}
