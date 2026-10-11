using System.Collections.Immutable;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Editing;
using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Panels.Subtitles;

internal sealed class SubtitlesPanelViewModel : ObservableObject
{
    private readonly WorkbenchSession session;
    private SubtitleRow[] rows = [];
    private SubtitleRow[] visibleRows = [];
    private SubtitleRow? selectedRow;
    private string? validationError;
    private Guid? invalidRowId;
    private ImmutableArray<ProjectTrack> tracks = [];
    private ProjectTrack? selectedTrack;
    private ProjectDocument? moveContextDocument;
    private Guid[] moveContextIds = [];
    private SubtitleColorTagFilter colorTagFilter = SubtitleColorTagFilter.All;
    private SubtitleColorTagFilterChoice[] colorTagFilters = [];
    private Guid filterProjectId;
    private bool hideTimes;
    private bool hideDuration;

    internal SubtitlesPanelViewModel(WorkbenchSession session)
    {
        this.session = session;
        MoveCommand = new(() => session.MoveSubtitleSelectionAsync(moveContextIds, moveContextDocument),
            () => moveContextIds.Length > 0 && ReferenceEquals(moveContextDocument, session.DocumentSnapshot) &&
                !session.IsClosing && !session.IsProjectBusy && !session.IsUpdating);
        MergeCueCommand = new(session.MergeVisibleSubtitleSelectionAsync, () => session.CanMergeVisibleSubtitleSelection);
    }

    public SubtitleRow[] Rows
    {
        get => rows;
        set
        {
            if (SetProperty(ref rows, value))
            {
                RefreshVisibleRows();
            }
        }
    }

    public SubtitleRow? SelectedRow
    {
        get => selectedRow;
        set => SetProperty(ref selectedRow, value);
    }

    public IReadOnlyList<Guid> SelectedIds => session.SelectedSubtitleIds;

    public bool HideTimes
    {
        get => hideTimes;
        set => SetProperty(ref hideTimes, value);
    }

    public bool HideDuration
    {
        get => hideDuration;
        set => SetProperty(ref hideDuration, value);
    }

    public AsyncRelayCommand MoveCommand { get; }

    internal void SetMoveContext()
    {
        moveContextDocument = session.DocumentSnapshot;
        moveContextIds = SelectedIds.ToArray();
        RefreshMoveCommand();
    }

    internal void RefreshMoveCommand() => MoveCommand.NotifyCanExecuteChanged();

    internal void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedIds));
        MergeCueCommand.NotifyCanExecuteChanged();
    }

    public SubtitleRow[] VisibleRows => visibleRows;
    public ImmutableArray<ProjectTrack> Tracks => tracks;
    public ProjectTrack? SelectedTrack => selectedTrack;
    public SubtitleColorTagFilterChoice[] ColorTagFilters => colorTagFilters;
    public SubtitleColorTagFilterChoice? SelectedColorTagFilter => colorTagFilters.FirstOrDefault(choice => choice.Value == colorTagFilter);
    public bool IsColorTagFilterActive => !colorTagFilter.IsAll;
    public string ColorTagFilterHint => Localization.Get("Workbench.ColorTag.Filter") + " · " + SelectedColorTagFilter?.Name;
    internal SubtitleColorTagFilter ColorTagFilter => colorTagFilter;

    internal void RefreshColorTags()
    {
        var document = session.DocumentSnapshot;
        if (filterProjectId != document.Id || colorTagFilter.TagId is { } tagId && !document.ColorTags.Any(tag => tag.Id == tagId))
        {
            colorTagFilter = SubtitleColorTagFilter.All;
        }
        filterProjectId = document.Id;
        SubtitleColorTagFilterChoice[] choices =
        [
            new(SubtitleColorTagFilter.All, Localization.Get("Workbench.ColorTag.All")),
            new(SubtitleColorTagFilter.Untagged, Localization.Get("Workbench.ColorTag.None")),
            .. document.ColorTags.Select(tag => new SubtitleColorTagFilterChoice(new(false, tag.Id), tag.Name, tag.ColorHex))
        ];
        if (!colorTagFilters.SequenceEqual(choices))
        {
            colorTagFilters = choices;
            OnPropertyChanged(nameof(ColorTagFilters));
        }
        OnPropertyChanged(nameof(SelectedColorTagFilter));
        OnPropertyChanged(nameof(IsColorTagFilterActive));
        OnPropertyChanged(nameof(ColorTagFilterHint));
        RefreshVisibleRows();
    }

    internal void AcceptColorTagFilter(SubtitleColorTagFilter value)
    {
        colorTagFilter = value;
        OnPropertyChanged(nameof(SelectedColorTagFilter));
        OnPropertyChanged(nameof(IsColorTagFilterActive));
        OnPropertyChanged(nameof(ColorTagFilterHint));
        RefreshVisibleRows();
    }

    internal bool IsRowVisible(Guid id) => visibleRows.Any(row => row.Id == id);

    /// <summary>在统一草稿边界内切换列表标签筛选。</summary>
    public bool SelectColorTagFilter(SubtitleColorTagFilterChoice choice)
    {
        var accepted = session.TrySelectSubtitleColorTagFilter(choice.Value);
        OnPropertyChanged(nameof(SelectedColorTagFilter));
        return accepted;
    }

    internal void UpdateTracks(ImmutableArray<ProjectTrack> values, Guid? currentId)
    {
        if (!tracks.SequenceEqual(values))
        {
            tracks = values;
            OnPropertyChanged(nameof(Tracks));
        }

        var track = currentId is { } id ? values.Single(value => value.Id == id) : null;
        if (selectedTrack != track)
        {
            selectedTrack = track;
            OnPropertyChanged(nameof(SelectedTrack));
        }

        RefreshVisibleRows();
    }

    private void RefreshVisibleRows()
    {
        var indexed = Rows.ToDictionary(row => row.Id);
        var values = SelectedTrack is { } track
            ? session.ClipIndex.GetTrackSubtitles(track.Id).Where(colorTagFilter.Matches)
                .Where(line => indexed.ContainsKey(line.Id)).Select(line => indexed[line.Id]).ToArray() : [];
        for (var index = 0; index < values.Length; index++)
        {
            values[index].DisplayNumber = index + 1;
        }
        if (!visibleRows.SequenceEqual(values))
        {
            visibleRows = values;
            OnPropertyChanged(nameof(VisibleRows));
        }
        MergeCueCommand.NotifyCanExecuteChanged();
    }

    /// <summary>切换当前字幕轨道，保留全部行草稿的统一提交边界。</summary>
    public void SelectTrack(Guid id) => session.SelectTrack(id);

    public string? ValidationError
    {
        get => validationError;
        set => SetProperty(ref validationError, value);
    }

    public Guid? InvalidRowId
    {
        get => invalidRowId;
        set => SetProperty(ref invalidRowId, value);
    }

    public ICommand DetailsCommand => session.ViewModel.GetCommand(AegiNext.Desktop.Shortcuts.WorkbenchCommand.OPEN_SUBTITLE_DETAILS);

    public ICommand AddCueCommand => session.ViewModel.GetCommand(AegiNext.Desktop.Shortcuts.WorkbenchCommand.ADD_SUBTITLE);

    public ICommand DeleteCueCommand => session.ViewModel.GetCommand(AegiNext.Desktop.Shortcuts.WorkbenchCommand.DELETE_SUBTITLE);

    public ICommand SplitCueCommand => session.ViewModel.GetCommand(AegiNext.Desktop.Shortcuts.WorkbenchCommand.SPLIT_SUBTITLE);

    public AsyncRelayCommand MergeCueCommand { get; }
    /// <summary>选择字幕，切换前统一验证待提交草稿。</summary>
    public void SelectCue(Guid id) => session.SelectCue(id);
    /// <summary>同步字幕列表的主项与完整选择集合，切换前统一验证草稿。</summary>
    public bool SelectRows(Guid? primaryId, IEnumerable<Guid> ids) => session.SelectSubtitleRows(primaryId, ids);
    /// <summary>激活行内编辑目标，保留包含此行的多选集合。</summary>
    public void FocusRow(Guid id) => session.FocusSubtitleRow(id);
    /// <summary>记录文本拆分的字素光标位置。</summary>
    public void SetCaret(Guid id, int index) => session.SetTextCaret(id, index);
    /// <summary>焦点提交使用统一的原子草稿边界。</summary>
    public void CommitRow(SubtitleRow row) => session.CommitRow(row);
    /// <summary>提交草稿后激活下一行，或从当前行结尾到播放进度创建字幕。</summary>
    public Task<Guid?> AdvanceRowAsync(Guid id) => session.AdvanceSubtitleRowAsync(id);
}
