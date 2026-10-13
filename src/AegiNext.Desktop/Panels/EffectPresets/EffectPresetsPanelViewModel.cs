using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Panels.EffectPresets;

internal sealed class EffectPresetsPanelViewModel : ObservableObject
{
    private readonly WorkbenchSession session;
    private EffectPresetCardViewModel[] items = [];
    private EffectPresetCardViewModel? selectedPreset;
    private long revision;

    internal EffectPresetsPanelViewModel(WorkbenchSession session)
    {
        this.session = session;
        ApplyCommand = new(() => session.RunCommandAsync(session.EffectScripts.ApplySelectedAsync),
            () => session.EffectScripts.Availability(SelectedPreset?.Id) == EffectPresetApplyAvailability.READY);
        ManageEffectScriptsCommand = new(() => session.RequestSettingsAsync(SettingsPage.EFFECTS));
    }

    public EffectPresetCardViewModel[] Items
    {
        get => items;
        private set => SetProperty(ref items, value);
    }
    public EffectPresetCardViewModel? SelectedPreset
    {
        get => selectedPreset;
        set
        {
            if (SetProperty(ref selectedPreset, value))
            {
                RefreshAvailability();
            }
        }
    }
    public AsyncRelayCommand ApplyCommand { get; }
    public AsyncRelayCommand ManageEffectScriptsCommand { get; }
    public string ApplyHint => Localization.Get(session.EffectScripts.Availability(SelectedPreset?.Id) switch
    {
        EffectPresetApplyAvailability.NO_SELECTION => "Workbench.EffectPresetNoSelection",
        EffectPresetApplyAvailability.REQUIRES_SUBTITLE => "Workbench.EffectPresetRequiresSubtitle",
        _ => "Workbench.ApplyPreset"
    });
    public bool ShowApplyHint => session.EffectScripts.Availability(SelectedPreset?.Id) is
        EffectPresetApplyAvailability.NO_SELECTION or EffectPresetApplyAvailability.REQUIRES_SUBTITLE;

    internal void RefreshCatalog(IReadOnlyList<EffectScriptChoice> choices)
    {
        var current = Items.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var selectedId = SelectedPreset?.Id;
        var next = choices.Select(choice =>
        {
            if (current.TryGetValue(choice.Id, out var existing) && existing.Source == choice.Source)
            {
                existing.Name = choice.Name;
                existing.RefreshLocalizedState();
                return existing;
            }
            return new EffectPresetCardViewModel(choice.Id, choice.Name, choice.Source, ++revision, choice.RequiresSubtitle);
        }).ToArray();
        if (!Items.SequenceEqual(next))
        {
            Items = next;
        }
        SelectedPreset = Items.FirstOrDefault(item => item.Id == selectedId) ?? Items.FirstOrDefault();
        RefreshAvailability();
    }

    internal void RefreshAvailability()
    {
        ApplyCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ApplyHint));
        OnPropertyChanged(nameof(ShowApplyHint));
    }
}
