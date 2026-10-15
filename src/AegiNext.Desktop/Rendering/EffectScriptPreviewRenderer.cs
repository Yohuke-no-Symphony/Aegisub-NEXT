using AegiNext.Core.Effects;
using AegiNext.Core.Timing;
using AegiNext.Media.Preview;
using AegiNext.Rendering.Fonts;
using AegiNext.Rendering.Projects;

namespace AegiNext.Desktop.Rendering;

internal sealed class EffectScriptPreviewRenderer : IEffectScriptPreviewRenderer
{
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private EffectScriptPreviewInput? compiledInput;
    private EffectPresetPreviewScene? scene;
    private Exception? compilationError;
    private Exception? renderingError;
    private ProjectSceneRenderer? renderer;
    private SystemFontCatalog? fontCatalog;
    private byte[] background = [];
    private (int Width, int Height) backgroundSize;
    private uint backgroundColor;
    private bool disposed;

    internal long CompilationCount { get; private set; }
    internal int CachedSceneCount => compiledInput is null ? 0 : 1;

    /// <inheritdoc />
    public SdrVideoFrame Render(EffectScriptPreviewRequest request, MediaTime position, CancellationToken cancellationToken)
    {
        VerifyThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var currentScene = GetScene(request.Input, cancellationToken);
        if (renderer is null || !ReferenceEquals(fontCatalog, request.FontCatalog))
        {
            renderer?.Dispose();
            renderingError = null;
            fontCatalog = request.FontCatalog;
            renderer = new(currentScene.Assets, fontCatalog);
        }
        if (renderingError is { } failure)
        {
            throw failure;
        }
        var size = request.GetOutputSize();
        var color = request.Input.Options.BackgroundBgra;
        if (backgroundSize != size || backgroundColor != color)
        {
            backgroundSize = size;
            backgroundColor = color;
            background = new byte[checked(size.Width * size.Height * 4)];
            for (var index = 0; index < background.Length; index += 4)
            {
                background[index] = (byte)color;
                background[index + 1] = (byte)(color >> 8);
                background[index + 2] = (byte)(color >> 16);
                background[index + 3] = byte.MaxValue;
            }
        }
        try
        {
            var pixels = renderer.ComposePreview(currentScene.Document, position, background,
                size.Width, size.Height, size.Width * 4, size.Width, size.Height, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new(size.Width, size.Height, pixels);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            renderingError = error;
            throw;
        }
    }

    private EffectPresetPreviewScene GetScene(EffectScriptPreviewInput input, CancellationToken cancellationToken)
    {
        if (compiledInput is null || !compiledInput.HasSameScene(input))
        {
            renderer?.Dispose();
            renderer = null;
            scene = null;
            compiledInput = null;
            compilationError = null;
            renderingError = null;
            try
            {
                var prepared = EffectPresetPreviewScene.Create(input.Options, input.Source, input.StylePreset, cancellationToken, input.Parameters);
                cancellationToken.ThrowIfCancellationRequested();
                scene = prepared;
            }
            catch (Exception error) when (error is EffectScriptException or InvalidDataException or EffectPresetPreviewRequirementException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                compilationError = error;
            }
            compiledInput = input;
            CompilationCount++;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (compilationError is { } diagnostic)
        {
            throw diagnostic;
        }
        return scene!;
    }

    private void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != ownerThread)
        {
            throw new InvalidOperationException("脚本样例渲染器必须在创建线程上使用和释放。");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        VerifyThread();
        if (disposed)
        {
            return;
        }
        disposed = true;
        renderer?.Dispose();
        renderer = null;
        scene = null;
        compiledInput = null;
        compilationError = null;
        renderingError = null;
        background = [];
    }
}
