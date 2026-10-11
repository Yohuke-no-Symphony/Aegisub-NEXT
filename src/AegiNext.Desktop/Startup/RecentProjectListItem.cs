using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Styling;
using Avalonia.Media;
using Material.Icons;

namespace AegiNext.Desktop.Startup;

internal sealed class RecentProjectListItem(RecentProjectEntry entry) : WelcomeListItem
{
    private readonly RecentProjectIcon icon = new(entry.Name);

    internal RecentProjectEntry Entry { get; } = entry;
    public override bool IsProject => true;
    public bool IsPinned => Entry.IsPinned;
    public string PinActionLabel => Localization.Get(IsPinned ? "Welcome.UnpinProject" : "Welcome.PinProject");
    public MaterialIconKind PinIconKind => WorkbenchIcon.ResolveKind(IsPinned ? "Unpin" : "Pin");
    public string Name => Entry.Name;
    public string Path => Entry.Path;
    public string IconInitials => icon.Initials;
    public IBrush IconBackground => IsUnavailable ? Brushes.Gainsboro : icon.Background;
    public IBrush IconForeground => icon.Foreground;
    public IBrush IconDarkBackground => IsUnavailable ? Brushes.DimGray : icon.DarkBackground;
    public IBrush IconDarkForeground => icon.DarkForeground;
    public bool IsUnavailable => !File.Exists(Path);
    public bool CanOpenFolder => Directory.Exists(System.IO.Path.GetDirectoryName(Path));
    public string Availability => IsUnavailable ? Localization.Get("Welcome.ProjectUnavailable") : string.Empty;

    internal void Refresh()
    {
        OnPropertyChanged(nameof(IsUnavailable));
        OnPropertyChanged(nameof(CanOpenFolder));
        OnPropertyChanged(nameof(IconBackground));
        OnPropertyChanged(nameof(IconDarkBackground));
        OnPropertyChanged(nameof(Availability));
        OnPropertyChanged(nameof(PinActionLabel));
    }
}
