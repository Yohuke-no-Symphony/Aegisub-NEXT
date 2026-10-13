using AegiNext.Core.Effects;
using AegiNext.Core.Timing;
using AegiNext.Media.Preview;
using AegiNext.Rendering.Fonts;
using AegiNext.Rendering.Projects;

namespace AegiNext.Desktop.Rendering;

internal sealed class EffectPresetPreviewRenderer : IEffectPresetPreviewRenderer
{
    private readonly EffectPresetPreviewOptions options;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<string, LinkedListNode<EffectPresetPreviewCompiledScene>> scenes = new(StringComparer.Ordinal);
    private readonly LinkedList<EffectPresetPreviewCompiledScene> sceneOrder = new();
    private ProjectSceneRenderer? renderer;
    private SystemFontCatalog? fontCatalog;
    private byte[] background = [];
    private (int Width, int Height) backgroundSize;
    private bool disposed;

    internal EffectPresetPreviewRenderer(EffectPresetPreviewOptions options)
    {
        options.Validate();
        this.options = options;
    }

    internal long CompilationCount { get; private set; }
    internal int CachedSceneCount => scenes.Count;

    /// <inheritdoc />
    public SdrVideoFrame Render(EffectPresetPreviewRequest request, MediaTime position, CancellationToken cancellationToken)
    {
        VerifyThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var scene = GetScene(request, cancellationToken);
        if (renderer is null || !ReferenceEquals(fontCatalog, request.FontCatalog))
        {
            renderer?.Dispose();
            fontCatalog = request.FontCatalog;
            renderer = new(new DirectoryProjectAssetResolver(Path.GetTempPath()), fontCatalog);
        }
        var size = EffectPresetPreviewCacheKey.GetOutputSize(options, request.RenderScaling);
        if (backgroundSize != size)
        {
            backgroundSize = size;
            background = new byte[checked(size.Width * size.Height * 4)];
            for (var index = 0; index < background.Length; index += 4)
            {
                background[index] = (byte)options.BackgroundBgra;
                background[index + 1] = (byte)(options.BackgroundBgra >> 8);
                background[index + 2] = (byte)(options.BackgroundBgra >> 16);
                background[index + 3] = byte.MaxValue;
            }
        }
        var pixels = renderer.ComposePreview(scene.Document, position, background,
            size.Width, size.Height, size.Width * 4, size.Width, size.Height, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(size.Width, size.Height, pixels);
    }

    private EffectPresetPreviewScene GetScene(EffectPresetPreviewRequest request, CancellationToken cancellationToken)
    {
        if (scenes.TryGetValue(request.SourceHash, out var cached))
        {
            sceneOrder.Remove(cached);
            sceneOrder.AddFirst(cached);
        }
        else
        {
            EffectPresetPreviewScene? scene = null;
            Exception? error = null;
            try
            {
                CompilationCount++;
                scene = EffectPresetPreviewScene.Create(options, request.Source, cancellationToken);
            }
            catch (Exception failure) when (failure is EffectScriptException or InvalidDataException or EffectPresetPreviewRequirementException)
            {
                error = failure;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (sceneOrder.Count >= options.MaximumCompiledScenes)
            {
                scenes.Remove(sceneOrder.Last!.Value.SourceHash);
                sceneOrder.RemoveLast();
            }
            cached = sceneOrder.AddFirst(new EffectPresetPreviewCompiledScene(request.SourceHash, scene, error));
            scenes.Add(request.SourceHash, cached);
        }
        if (cached.Value.Error is { } diagnostic)
        {
            throw diagnostic;
        }
        return cached.Value.Scene!;
    }

    private void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != ownerThread)
        {
            throw new InvalidOperationException("特效预览渲染器必须在创建线程上使用和释放。");
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
        scenes.Clear();
        sceneOrder.Clear();
        background = [];
    }
}
