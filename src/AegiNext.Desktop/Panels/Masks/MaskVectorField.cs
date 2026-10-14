using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Panels.Masks;

internal sealed class MaskVectorField(MaskNumericField x, MaskNumericField y) : ObservableObject
{
    public MaskNumericField X { get; } = x;
    public MaskNumericField Y { get; } = y;
    public AnimationPropertyRowViewModel? Row => X.Row;
    public AnimationTrackTarget? Target => X.Target;
    public string Label => Localization.Get(Target is { } target ? "Workbench." + target.Property : "Workbench.Pivot");
    public bool CanEdit => X.CanEdit && Y.CanEdit;
    public decimal Minimum => X.Minimum;
    public decimal Maximum => X.Maximum;

    internal void RefreshLabel() => OnPropertyChanged(nameof(Label));
}
