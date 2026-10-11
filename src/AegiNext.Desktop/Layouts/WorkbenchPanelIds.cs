namespace AegiNext.Desktop.Layouts;

internal static class WorkbenchPanelIds
{
    public const string PREVIEW = "preview";
    public const string TIMELINE = "timeline";
    public const string SUBTITLES = "subtitles";
    public const string STYLES = "styles";
    public const string EFFECTS = "effects";
    public const string EXPORT = "export";
    public const string LOG = "log";
    public const string ASSETS = "assets";
    public const string MASKS = "masks";
    public const string SUBTITLE_DETAILS = "subtitleDetails";

    public static IReadOnlyList<string> All { get; } =
        [PREVIEW, TIMELINE, SUBTITLES, STYLES, EFFECTS, EXPORT, LOG, SUBTITLE_DETAILS, MASKS, ASSETS];
}
