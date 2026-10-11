namespace AegiNext.Desktop.Layouts;

internal sealed record WorkspaceLayoutSnapshot
{
    public const int CURRENT_VERSION = 7;

    public int Version { get; init; } = CURRENT_VERSION;
    public LayoutNodeSnapshot Main { get; init; } = new();
    public IReadOnlyList<LayoutFloatingSnapshot> Floating { get; init; } = [];
    public IReadOnlyList<string> HiddenPanelIds { get; init; } = [];
    public string? FocusedPanelId { get; init; }
}
