using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace;
using AegiNext.Koumori;

namespace AegiNext.Desktop.Panels.Timeline;

internal sealed partial class TimelinePanelViewModel
{
    private bool isKoumoriAvailable;
    private bool isKoumoriSnapEnabled;
    private int koumoriMaximumSnapMilliseconds = 250;
    private IReadOnlyList<KoumoriBoundary> koumoriBoundaries = [];
    private KoumoriSnapState koumoriSnapState;
    private bool hasKoumoriStartCandidates;
    private bool hasKoumoriEndCandidates;
    private string? koumoriSnapError;
    public bool IsKoumoriAvailable
    {
        get => isKoumoriAvailable;
        set => SetProperty(ref isKoumoriAvailable, value);
    }
    public bool IsKoumoriSnapEnabled
    {
        get => isKoumoriSnapEnabled;
        set => SetProperty(ref isKoumoriSnapEnabled, value);
    }
    public int KoumoriMaximumSnapMilliseconds
    {
        get => koumoriMaximumSnapMilliseconds;
        set => SetProperty(ref koumoriMaximumSnapMilliseconds, value);
    }
    public IReadOnlyList<KoumoriBoundary> KoumoriBoundaries
    {
        get => koumoriBoundaries;
        set => SetProperty(ref koumoriBoundaries, value);
    }
    public KoumoriSnapState KoumoriSnapState => koumoriSnapState;
    public bool HasKoumoriStartCandidates => hasKoumoriStartCandidates;
    public bool HasKoumoriEndCandidates => hasKoumoriEndCandidates;
    public string KoumoriStatus
    {
        get
        {
            var key = koumoriSnapState switch
            {
                KoumoriSnapState.Analyzing => "Koumori.Analyzing",
                KoumoriSnapState.Ready when hasKoumoriStartCandidates && !hasKoumoriEndCandidates => "Koumori.SnapStartsOnly",
                KoumoriSnapState.Ready when !hasKoumoriStartCandidates && hasKoumoriEndCandidates => "Koumori.SnapEndsOnly",
                KoumoriSnapState.Ready => "Koumori.SnapReady",
                KoumoriSnapState.ReadyEmpty => "Koumori.SnapEmpty",
                KoumoriSnapState.Failed => "Koumori.SnapFailed",
                _ => "Koumori.Snap"
            };
            return koumoriSnapState == KoumoriSnapState.Failed
                ? Localization.Format(key, koumoriSnapError ?? string.Empty) : Localization.Get(key);
        }
    }

    internal void SetKoumoriSnapStatus(KoumoriSnapState state, bool starts = false, bool ends = false,
        string? error = null)
    {
        var changed = SetProperty(ref koumoriSnapState, state, nameof(KoumoriSnapState));
        changed |= SetProperty(ref hasKoumoriStartCandidates, starts, nameof(HasKoumoriStartCandidates));
        changed |= SetProperty(ref hasKoumoriEndCandidates, ends, nameof(HasKoumoriEndCandidates));
        changed |= koumoriSnapError != error;
        koumoriSnapError = error;
        if (changed)
        {
            RefreshKoumoriStatus();
        }
    }

    internal void RefreshKoumoriStatus() => OnPropertyChanged(nameof(KoumoriStatus));
}
