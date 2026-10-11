using AegiNext.Core.Projects;

namespace AegiNext.Rendering.Images;

internal sealed record ImageSessionCacheEntry(ImageDecodeSession Session, LinkedListNode<ProjectAsset> Node);
