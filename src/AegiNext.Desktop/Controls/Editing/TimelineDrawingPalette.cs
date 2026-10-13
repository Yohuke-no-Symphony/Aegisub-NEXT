using Avalonia.Media;

namespace AegiNext.Desktop.Controls;

internal sealed class TimelineDrawingPalette
{
    internal TimelineDrawingPalette(bool dark)
    {
        Foreground = Brush(dark ? "#DDE3EE" : "#263247");
        Surface = Brush(dark ? "#131A26" : "#F2F5FA");
        AnimationSurface = Brush(dark ? "#20131A26" : "#D9F2F5FA");
        Grid = new(Brush(dark ? "#334155" : "#C4CEDC"));
        Track = Brush(dark ? "#202C40" : "#E2E9F3");
        SelectedTrack = Brush(dark ? "#354C73" : "#BED5F4");
        StyleBadge = Brush(dark ? "#415B86" : "#C9DAEF");
        InvalidClip = Brush(dark ? "#BDAD4759" : "#F2EBC4CA");
        ActiveClipBorder = Brush(dark ? "#A5B8FF" : "#365BA8");
        ClipForeground = dark ? Brushes.White : Brush("#16263D");
        Components = dark
            ? [Brush("#B7C6FF"), Brush("#F5C979"), Brush("#81D5AE")]
            : [Brush("#284DA6"), Brush("#8E5700"), Brush("#136645")];
        ColorComponents = dark
            ? [Brush("#EE7878"), Brush("#80D99B"), Brush("#81B8FF"), Foreground]
            : [Brush("#A22435"), Brush("#126F39"), Brush("#215EB8"), Foreground];
        MarkerBorder = new(dark ? Brushes.White : Brush("#243651"), 1.5);
        Playhead = new(Brush(dark ? "#FF6B7A" : "#B52542"), 2);
        SnapFill = Brush(dark ? "#304D90FF" : "#28395EAC");
        SnapBorder = new(Brush(dark ? "#9DC4FF" : "#305DAD"), 1.5);
    }

    internal SolidColorBrush Foreground { get; }
    internal SolidColorBrush Surface { get; }
    internal SolidColorBrush AnimationSurface { get; }
    internal Pen Grid { get; }
    internal SolidColorBrush Track { get; }
    internal SolidColorBrush SelectedTrack { get; }
    internal SolidColorBrush StyleBadge { get; }
    internal SolidColorBrush InvalidClip { get; }
    internal SolidColorBrush ActiveClipBorder { get; }
    internal IBrush ClipForeground { get; }
    internal SolidColorBrush[] Components { get; }
    internal SolidColorBrush[] ColorComponents { get; }
    internal Pen MarkerBorder { get; }
    internal Pen Playhead { get; }
    internal SolidColorBrush SnapFill { get; }
    internal Pen SnapBorder { get; }

    private static SolidColorBrush Brush(string value) => new(Color.Parse(value));
}
