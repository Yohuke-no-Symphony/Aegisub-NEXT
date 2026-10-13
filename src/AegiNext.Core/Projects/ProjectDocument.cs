using System.Collections.Immutable;
using AegiNext.Core.Media;

namespace AegiNext.Core.Projects;

/// <summary>AegiNext 不可变工程快照；所有边界通过 ProjectValidator 验证。</summary>
public sealed record ProjectDocument
{
    public const int CURRENT_VERSION = 17;
    public int Version { get; init; } = CURRENT_VERSION;
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Untitled";
    public int Width { get; init; } = 1920;
    public int Height { get; init; } = 1080;
    public MediaRatio FrameRate { get; init; } = new(30, 1);
    public double ReferenceWhiteNits { get; init; } = 203;
    public ProjectMediaBinding? Media { get; init; }
    public ImmutableArray<ProjectAsset> Assets { get; init; } = [];
    public ProjectAssetLibrary AssetLibrary { get; init; } = new();
    public ImmutableArray<ProjectTrack> Tracks { get; init; } = [ProjectTrack.Default];
    public ImmutableArray<SubtitleLine> Subtitles { get; init; } = [];
    public ImmutableArray<SubtitleColorTag> ColorTags { get; init; } = [];
    public ImmutableArray<ProjectLayer> Layers { get; init; } = [];
    public ImmutableArray<EffectPreset> Presets { get; init; } = [];
    public TimelineViewState TimelineViewState { get; init; } = new();
}
