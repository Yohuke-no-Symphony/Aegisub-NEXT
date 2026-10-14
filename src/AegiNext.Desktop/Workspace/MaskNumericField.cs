using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed class MaskNumericField(string key, string labelKey, AnimationTrackTarget? target, int component, double minimum, double maximum, AnimationPropertyRowViewModel? row = null)
{
    private double original;

    public string Key { get; } = key;
    public string FieldKey => Row is null ? Key : Component == 0 ? Row.XFieldKey : Row.YFieldKey;
    public string Label => Localization.Get(labelKey) + (Target is { } identity && AnimationPropertyMetadata.GetComponentCount(identity.Property) > 1 ? " " + AnimationPropertyMetadata.GetComponentName(identity.Property, Component) : string.Empty);
    public AnimationTrackTarget? Target { get; } = target;
    public int Component { get; } = component;
    public decimal Minimum { get; } = (decimal)minimum;
    public bool CanEdit => Row?.IsEnabled != false;
    public decimal Maximum { get; } = (decimal)maximum;
    public AnimationPropertyRowViewModel? Row { get; } = row;
    public NumericValueDraft Draft { get; } = row is null ? new() : component == 0 ? row.X : row.Y;
    internal double Original => Row?.GetOriginalComponent(Component) ?? original;

    internal void Load(double value)
    {
        original = value;
        Draft.Load(value);
    }
    internal bool IsDirty => Row is not null ? Row.HasDraft : Draft.RawText != ((decimal)Original).ToString(System.Globalization.CultureInfo.CurrentCulture);
}
