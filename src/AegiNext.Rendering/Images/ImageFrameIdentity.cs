using AegiNext.Core.Projects;

namespace AegiNext.Rendering.Images;

internal readonly record struct ImageFrameIdentity(ProjectAsset Asset, int FrameIndex);
