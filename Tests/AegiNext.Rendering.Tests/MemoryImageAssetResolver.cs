using AegiNext.Core.Projects;
using AegiNext.Rendering.Projects;

namespace AegiNext.Rendering.Tests;

internal sealed class MemoryImageAssetResolver(byte[] data) : IProjectAssetResolver
{
    internal int OpenCount { get; private set; }

    /// <inheritdoc />
    public Stream Open(ProjectAsset asset)
    {
        OpenCount++;
        return new MemoryStream(data, false);
    }
}
