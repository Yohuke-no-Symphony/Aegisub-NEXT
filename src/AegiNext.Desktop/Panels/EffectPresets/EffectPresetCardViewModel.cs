using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Rendering;
using CommunityToolkit.Mvvm.ComponentModel;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Panels.EffectPresets;

internal sealed class EffectPresetCardViewModel(string id, string name, string source, long revision, bool requiresSubtitle) : ObservableObject
{
    private string name = name;
    private Exception? previewError;
    private long revision = revision;

    public string Id { get; } = id;
    public string Source { get; } = source;
    public long Revision => revision;
    internal bool RequiresSubtitle { get; } = requiresSubtitle;
    public EffectScriptParameterDraftSet Parameters { get; } = new(source);
    internal bool CommitParameters()
    {
        if (!Parameters.TryCapture(out _))
        {
            return false;
        }
        revision++;
        return true;
    }
    public string Name
    {
        get => name;
        internal set => SetProperty(ref name, value);
    }
    public bool HasPreviewError => previewError is not null;
    public string? PreviewToolTip => HasPreviewError ? PreviewStatus : null;
    public string PreviewStatus => previewError switch
    {
        EffectPresetPreviewRequirementException { Requirement: EffectPresetPreviewRequirement.MOTION_PATH } =>
            Localization.Get("Workbench.EffectPresetRequiresPath"),
        { } error => Localization.Format("Workbench.EffectPresetPreviewUnavailable", error.Message),
        _ => string.Empty
    };

    internal void SetPreviewError(Exception? error)
    {
        previewError = error;
        RefreshLocalizedState();
    }

    internal void RefreshLocalizedState()
    {
        OnPropertyChanged(nameof(HasPreviewError));
        OnPropertyChanged(nameof(PreviewStatus));
        OnPropertyChanged(nameof(PreviewToolTip));
    }
}
