using AegiNext.Core.Projects;
using AegiNext.Desktop.Workspace;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed partial class EffectsPanelViewModel
{
    private AnimationPropertyRowViewModel[] maskRows = [];

    public bool MaskExpanded { get => IsExpanded("mask"); set => SetExpanded("mask", value); }
    public bool CanEditMask => session.MaskEditing.CanEdit;
    public bool HasMask => session.SelectedLayer?.Mask is not null;
    public bool HasEditableMask => CanEditMask && HasMask;
    public bool NeedsMaskNodeSelection => session.ViewModel.Masks.IsVectorMask && !session.ViewModel.Masks.HasSelectedPoint;
    public AnimationPropertyRowViewModel[] MaskRows { get => maskRows; private set => SetItems(ref maskRows, value, nameof(MaskRows)); }
    public bool CanClearMaskNodeAnimation => session.SelectedLayer?.Tracks.Any(track =>
        AnimationPropertyMetadata.IsNodeProperty(track.Property)) == true;
    public IAsyncRelayCommand ClearMaskNodeAnimationCommand { get; private set; } = null!;
    internal void RefreshMaskPropertyGrid()
    {
        var layer = session.SelectedLayer;
        MaskRows = layer is null ? [] : session.PropertyEditing.GetMaskRows(layer.Id, session.SceneEditing.MaskNodeId);
        foreach (var property in new[] { nameof(MaskExpanded), nameof(CanEditMask), nameof(HasMask), nameof(HasEditableMask),
            nameof(NeedsMaskNodeSelection), nameof(CanClearMaskNodeAnimation) })
        {
            OnPropertyChanged(property);
        }
        ClearMaskNodeAnimationCommand.NotifyCanExecuteChanged();
    }
}
