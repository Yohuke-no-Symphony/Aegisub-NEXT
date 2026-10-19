using System.Globalization;
using AegiNext.Application.Presets;
using AegiNext.Application;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Styling;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Windowing;
using AegiNext.Rendering.Projects;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace AegiNext.Desktop.Panels.SubtitleDetails;

internal sealed partial class SubtitleDetailsPanelView : UserControl, IWorkbenchPanelView, IWorkbenchFocusCommandTarget
{
    private readonly WorkbenchSession session;
    private readonly SubtitleDetailsCoordinator coordinator;
    private readonly List<IDisposable> bindings = [];
    private readonly RichSubtitleEditor rich = new() { Name = "RichSubtitleInput", RestoreOnEscape = false };
    private readonly KaraokeClipAxis axis = new() { Name = "KaraokeAxis" };
    private readonly TextBlock error = new() { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
    private readonly NumericDraftInput duration = TimingInput("KaraokeDurationInput");
    private readonly ComboBox kind = new() { Name = "KaraokeHighlightKindInput", Width = 200 };
    private readonly ComboBox presets = new()
    {
        Name = "SelectionStylePresetCombo", Width = 120, Height = 32, VerticalAlignment = VerticalAlignment.Center
    };
    private readonly FontFamilyPicker selectionFont = new() { Name = "SelectionFontInput", Width = 200, RestoreOnEscape = false, CommitOnLostFocus = false };
    private readonly ComboBox visualState = new() { Name = "SubtitleVisualStateInput" };
    private readonly DraftPopup clipPopup = new() { OverlayDismissEventPassThrough = true,
        Placement = PlacementMode.BottomEdgeAlignedLeft, VerticalOffset = 4 };
    private readonly ToolbarToggleButton enableKaraoke = new() { Name = "EnableKaraokeToggle" };
    private readonly ToolbarToggleButton loop = new() { Name = "SubtitleLoopToggle", IsChecked = false };
    private readonly ToolbarToggleButton snap = new() { Name = "KaraokeSnapToggle", IsChecked = true };
    private readonly ToolbarToggleButton keepTimeLabels = new() { Name = "KaraokeTimeLabelsToggle", IsChecked = false };
    private readonly Button play;
    private readonly StackPanel styleToolbar = new() { Name = "SelectionStyleToolbar", Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
    private readonly WrapPanel styleFields = new() { Name = "SelectionStyleFields" };
    private readonly WrapPanel timingFields = new() { Name = "KaraokeTimingFields", Width = 424 };
    private readonly List<Control> bodyOnlyFields = [];
    private readonly List<Button> selectionActions = [];
    private readonly Dictionary<string, ToolbarToggleButton> toggles = [];
    private bool synchronizing;
    private bool disposed;
    private bool completingInput;
    private bool formattingPointerActive;
    private bool formattingFocusPending;
    private Window? hostWindow;
    private int focusRevision;
    private int styleFocusRevision;
    private int selectionStart;
    private int selectionEnd;
    private Guid? previousTarget;
    private long lastPreviewRefresh;
    private Guid? loadedHighlightCueId;
    private KaraokeHighlightStyle? loadedHighlightStyle;
    private KaraokeVisualState? loadedVisualState;
    private bool lastPlaybackState;
    private bool IsEditingVisualState => coordinator.VisualState is not null;
    private Guid? bodyPresetId;
    private Guid? highlightPresetId;
    private readonly Guid selectionOverridePresetId = Guid.NewGuid();

    internal SubtitleDetailsPanelView(WorkbenchSession session)
    {
        this.session = session;
        coordinator = session.Details;
        Focusable = true;
        Classes.Add("business-surface");
        rich.TextEditRequested += OnTextEdit;
        rich.SelectionChanged += OnSelection;
        rich.RestoreRequested += (_, _) => coordinator.Restore("Text");
        axis.SelectionRequested += (_, e) =>
        {
            if (coordinator.SelectClips(e.SelectedClipIds, e.PrimaryClipId))
            {
                if (coordinator.Line?.Karaoke.FirstOrDefault(value => value.Id == e.PrimaryClipId) is { } clip)
                {
                    SetTextSelection(clip.Utf16Start, clip.Utf16Start + clip.Utf16Length);
                }
                Refresh();
            }
        };
        axis.RangeRequested += (_, e) => coordinator.SetRange(e.BaselineLine, e.AnimationOffset, e.ClipId, e.Start, e.End, e.IsTimingLinked);
        axis.ClipEditRequested += (_, e) =>
        {
            if (coordinator.Line?.Id == e.SubtitleId && coordinator.SelectedClipId == e.ClipId)
            {
                clipPopup.PlacementRect = e.Anchor;
                clipPopup.ShowAt(axis);
            }
        };
        clipPopup.Closing += (sender, e) =>
        {
            e.Cancel = !coordinator.TryPrepare(session.Editor.Snapshot, out _);
            if (!e.Cancel)
            {
                QueueStyleFocusCommit();
            }
        };
        play = IconButton("Workbench.PlaySegment", "Play", async () =>
        {
            if (coordinator.IsPlaying)
            {
                await coordinator.StopPlaybackAsync();
            }
            else if (HasFocusWithin())
            {
                await coordinator.PlayAsync(coordinator.SelectedClipId is not null, loop.IsChecked == true);
            }
        });
        play.Name = "SubtitlePlayPauseButton";
        ConfigureToggle(loop, "Workbench.LoopPlayback", WorkbenchIcon.Create("Loop"));
        loop.PropertyChanged += async (_, e) =>
        {
            if (!synchronizing && e.Property == ToggleButton.IsCheckedProperty)
            {
                await session.RunCommandAsync(() => coordinator.SetLoopEnabledAsync(loop.IsChecked == true));
            }
        };
        ConfigureToggle(snap, "Workbench.KaraokeSnap", WorkbenchIcon.Create("Magnet"), "Workbench.KaraokeSnapHint");
        ConfigureToggle(keepTimeLabels, "Workbench.KaraokeKeepTimeLabels", WorkbenchIcon.Create("Clock"), "Workbench.KaraokeKeepTimeLabelsHint");
        ConfigureToggle(enableKaraoke, "Workbench.EnableKaraoke", IconLabel("Workbench.EnableKaraoke", "EnableHighlight"),
            "Workbench.EnableKaraokeHint");
        enableKaraoke.Width = double.NaN;
        enableKaraoke.Padding = new(8, 0);
        visualState.Classes.Add("icon-selector");
        visualState.Resources["ComboBoxThemeMinWidth"] = 0d;
        visualState.SelectionBoxItemTemplate = new FuncDataTemplate<SubtitleVisualStateChoice>((choice, _) =>
            choice is null ? null : BuildVisualStateIcon(choice, true));
        visualState.ItemTemplate = new FuncDataTemplate<SubtitleVisualStateChoice>((choice, _) =>
            choice is null ? null : BuildVisualStateMenuItem(choice));
        visualState.SelectionChanged += (_, _) =>
        {
            if (!synchronizing && visualState.SelectedItem is SubtitleVisualStateChoice choice)
            {
                coordinator.SetVisualState(choice.State);
                Refresh();
            }
        };
        BuildStyleFields();
        styleToolbar.Children.Add(ToolbarSeparator("VisualStateSeparator"));
        styleToolbar.Children.Add(visualState);
        var toolbar = new Grid { Name = "SubtitleDetailsToolbar" };
        toolbar.Children.Add(new ScrollViewer
        {
            Name = "SelectionToolbarScroll", Content = styleToolbar, Height = 32,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        enableKaraoke.PropertyChanged += (_, e) =>
        {
            if (!synchronizing && e.Property == ToggleButton.IsCheckedProperty)
            {
                coordinator.SetKaraokeEnabled(enableKaraoke.IsChecked == true);
                Refresh();
            }
        };
        var timingActions = BuildTimingEditor();
        var richContent = new Grid { RowDefinitions = new("Auto,Auto,Auto"), RowSpacing = 4 };
        rich.MinHeight = 96;
        richContent.Children.Add(rich);
        Grid.SetRow(timingActions, 1);
        richContent.Children.Add(timingActions);
        Grid.SetRow(axis, 2);
        richContent.Children.Add(axis);
        var root = new SubtitleDetailsLayout(toolbar, styleFields, richContent, error);
        Content = new VisualLayerManager
        {
            Name = "SubtitleDetailsVisualLayers", EnableAdornerLayer = true, ClipToBounds = true,
            Child = new ScrollViewer
            {
                Name = "SubtitleDetailsScroll", Content = root,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            }
        };
        presets.SelectionChanged += async (_, _) =>
        {
            if (!synchronizing && presets.SelectedItem is StylePresetListItem item)
            {
                if (IsEditingVisualState)
                {
                    await session.RunCommandAsync(ApplyHighlightPresetAsync);
                }
                else
                {
                    bodyPresetId = item.Id;
                }
            }
        };
        styleFields.AddHandler(KeyDownEvent, OnStyleFieldKeyDown, RoutingStrategies.Bubble, true);
        styleFields.AddHandler(LostFocusEvent, OnStyleFieldLostFocus, RoutingStrategies.Bubble);
        BindNumericDrags();
        coordinator.Changed += OnCoordinatorChanged;
        session.StyleLibraryChanged += OnStyleLibraryChanged;
        session.Fonts.Changed += OnFontsChanged;
        selectionFont.PropertyChanged += OnFontPickerChanged;
        session.ViewModel.GesturesCancelled += OnGesturesCancelled;
        session.PreviewUpdated += OnPreviewUpdated;
        Localization.LanguageChanged += OnLanguageChanged;
        AddHandler(GotFocusEvent, OnPanelGotFocus, RoutingStrategies.Bubble);
        AddHandler(LostFocusEvent, OnPanelLostFocus, RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel, true);
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Source is Control source && source.GetSelfAndVisualAncestors().Any(value => value is ToolbarToggleButton button &&
                toggles.ContainsValue(button)))
            {
                formattingPointerActive = true;
                formattingFocusPending = false;
                ++styleFocusRevision;
            }
        }, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, (_, _) => EndFormattingPointer(), RoutingStrategies.Tunnel, true);
        coordinator.Synchronize();
        Refresh();
    }

    private void BuildStyleFields()
    {
        foreach (var (name, text) in new[] { ("Bold", "B"), ("Italic", "I"), ("Underline", "U"), ("Strikethrough", "S") })
        {
            var button = new ToolbarToggleButton { Name = name + "SelectionButton" };
            ConfigureToggle(button, "Workbench." + name, text);
            button.Click += (_, _) => Toggle(name);
            button.PointerCaptureLost += (_, _) => EndFormattingPointer();
            toggles.Add(name, button);
            styleToolbar.Children.Add(button);
            bodyOnlyFields.Add(button);
        }
        styleToolbar.Children.Add(ToolbarSeparator("SelectionActionSeparator"));
        presets.Resources["ComboBoxThemeMinWidth"] = 0d;
        presets.DisplayMemberBinding = new Binding("Name");
        bindings.Add(presets.Bind(ToolTip.TipProperty, Localization.Observe("Workbench.StylePreset").ToBinding()));
        bindings.Add(presets.Bind(AutomationProperties.NameProperty, Localization.Observe("Workbench.StylePreset").ToBinding()));
        styleToolbar.Children.Add(presets);
        styleToolbar.Children.Add(SelectionAction("Workbench.ApplyToSelection", "ApplySelectionPreset", ApplyPresetAsync));
        styleToolbar.Children.Add(SelectionAction("Workbench.ClearSelectionStyle", "ClearSelectionStyle", () =>
        {
            if (IsEditingVisualState)
            {
                coordinator.ApplyHighlightStyle(null);
            }
            else
            {
                coordinator.ClearSelectionStyle(Math.Min(selectionStart, selectionEnd), Math.Abs(selectionEnd - selectionStart));
            }
            return Task.CompletedTask;
        }));
        var font = selectionFont;
        font.PreviewProvider = session.Fonts.PreviewProvider;
        font.RefreshFontCandidates(session.Fonts.Candidates);
        font.SetCurrentFont(SubtitleFontSelectionService.FromStyle(coordinator.SelectionStyle()));
        bindings.Add(font.Bind(AutoCompleteBox.TextProperty, new Binding(nameof(SubtitleDetailsStyleDraft.FontFamily))
            { Source = coordinator.StyleDraft, Mode = BindingMode.TwoWay }));
        font.FamilyCommitted += (_, value) =>
        {
            coordinator.StyleDraft.SelectFont(value.Selection);
            coordinator.CompleteInput(nameof(SubtitleDetailsStyleDraft.FontFamily), false);
        };
        var fontField = Field("Workbench.Font", font);
        var fontSizeField = Field("Workbench.FontSize", Number(coordinator.StyleDraft, nameof(SubtitleDetailsStyleDraft.FontSizeText), "SelectionFontSizeInput", 0.01m, 4096));
        var letterSpacingField = Field("Workbench.LetterSpacing", Number(coordinator.StyleDraft,
            nameof(SubtitleDetailsStyleDraft.LetterSpacingText), "SelectionLetterSpacingInput", -4096, 4096));
        bodyOnlyFields.Add(letterSpacingField);
        bodyOnlyFields.Add(fontField);
        bodyOnlyFields.Add(fontSizeField);
        styleFields.Children.Add(fontField);
        styleFields.Children.Add(fontSizeField);
        styleFields.Children.Add(letterSpacingField);
        styleFields.Children.Add(Field("Workbench.Fill", StyleColor(nameof(SubtitleDetailsStyleDraft.Fill), "SelectionFillInput")));
        styleFields.Children.Add(Field("Workbench.Stroke", StyleColor(nameof(SubtitleDetailsStyleDraft.Stroke), "SelectionStrokeInput")));
        styleFields.Children.Add(Field("Workbench.FillBlur", Number(null, nameof(SubtitleDetailsStyleDraft.FillBlurText), "SelectionFillBlurInput", 0, 512)));
        styleFields.Children.Add(Field("Workbench.StrokeBlur", Number(null, nameof(SubtitleDetailsStyleDraft.StrokeBlurText), "SelectionStrokeBlurInput", 0, 512)));
        styleFields.Children.Add(Field("Workbench.StrokeWidth", Number(null, nameof(SubtitleDetailsStyleDraft.StrokeWidthText), "SelectionStrokeWidthInput", 0, 4096)));
        styleFields.Children.Add(Field("Workbench.Shadow", StyleColor(nameof(SubtitleDetailsStyleDraft.Shadow), "SelectionShadowInput")));
        styleFields.Children.Add(ShadowOffset(null, "Selection"));
        styleFields.Children.Add(Field("Workbench.ShadowBlur", Number(null, nameof(SubtitleDetailsStyleDraft.ShadowBlurText), "SelectionShadowBlurInput", 0, 512)));
    }

    private ColorDraftInput StyleColor(string property, string name)
    {
        var input = new ColorDraftInput { Name = name, Width = 200, RestoreOnEscape = false, CommitOnLostFocus = false };
        bindings.Add(input.Bind(ColorDraftInput.DraftProperty, new Binding(property)));
        return input;
    }

    private StackPanel ShadowOffset(object? draft, string prefix)
    {
        var vector = new Grid { Name = prefix + "ShadowOffsetInput", ColumnDefinitions = new("Auto,*,Auto,*"), ColumnSpacing = 6 };
        var column = 0;
        foreach (var (axisName, property) in new[] { ("X", nameof(SubtitleDetailsStyleDraft.ShadowXText)), ("Y", nameof(SubtitleDetailsStyleDraft.ShadowYText)) })
        {
            var input = Number(draft, property, prefix + "Shadow" + axisName + "Input", -4096, 4096);
            var label = new NumericDragLabel { Text = axisName, Input = input, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, column++);
            vector.Children.Add(label);
            input.Width = double.NaN;
            input.MinWidth = 0;
            Grid.SetColumn(input, column++);
            vector.Children.Add(input);
        }
        return Field("Workbench.ShadowOffset", vector);
    }

    private NumericDraftInput Number(object? draft, string property, string name, decimal minimum, decimal maximum)
    {
        var input = new NumericDraftInput { Name = name, Width = 200, Minimum = minimum, Maximum = maximum, Increment = 1,
            ShowButtonSpinner = false, NumberFormat = CultureInfo.InvariantCulture.NumberFormat };
        var valueBinding = DraftBinding(draft, property, BindingMode.OneWay);
        valueBinding.Converter = new FuncValueConverter<string, object>(text =>
            decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : BindingOperations.DoNothing);
        bindings.Add(input.Bind(NumericUpDown.ValueProperty, valueBinding));
        bindings.Add(input.Bind(NumericDraftInput.RawTextProperty, DraftBinding(draft, property, BindingMode.TwoWay)));
        return input;
    }

    private static Binding DraftBinding(object? draft, string property, BindingMode mode)
    {
        var binding = new Binding(property) { Mode = mode };
        if (draft is not null)
        {
            binding.Source = draft;
        }
        return binding;
    }

    private StackPanel Field(string key, Control input)
    {
        var field = new StackPanel { Name = input.Name is null ? null : input.Name + "Field", Spacing = 4, Width = 200, Margin = new Thickness(0, 0, 12, 8) };
        field.Children.Add(Label(key, input as NumericDraftInput));
        field.Children.Add(input);
        return field;
    }

    private TextBlock Label(string key, NumericDraftInput? input = null)
    {
        var label = input is null ? new TextBlock() : new NumericDragLabel { Input = input };
        label.Classes.Add("field");
        bindings.Add(label.Bind(TextBlock.TextProperty, Localization.Observe(key).ToBinding()));
        return label;
    }

    private Button Button(string key, string icon, Func<Task> action)
    {
        var button = new Button
        {
            Name = key[(key.LastIndexOf('.') + 1)..] + "Button",
            Content = IconLabel(key, icon),
            Margin = new Thickness(0, 0, 6, 6)
        };
        bindings.Add(button.Bind(ToolTip.TipProperty, Localization.Observe(key).ToBinding()));
        bindings.Add(button.Bind(AutomationProperties.NameProperty, Localization.Observe(key).ToBinding()));
        button.Click += async (_, _) => await session.RunCommandAsync(action);
        return button;
    }

    private IconText IconLabel(string key, string icon)
    {
        var content = new IconText { IconKey = icon };
        bindings.Add(content.Bind(IconText.TextProperty, Localization.Observe(key).ToBinding()));
        return content;
    }

    private Button IconButton(string key, string icon, Func<Task> action)
    {
        var button = new Button { Name = key[(key.LastIndexOf('.') + 1)..] + "Button", Content = WorkbenchIcon.Create(icon) };
        button.Classes.Add("icon-button");
        button.Click += async (_, _) => await session.RunCommandAsync(action);
        return button;
    }

    private Button SelectionAction(string key, string icon, Func<Task> action)
    {
        var button = IconButton(key, icon, action);
        bindings.Add(button.Bind(ToolTip.TipProperty, Localization.Observe(key + "Hint").ToBinding()));
        bindings.Add(button.Bind(AutomationProperties.NameProperty, Localization.Observe(key).ToBinding()));
        selectionActions.Add(button);
        return button;
    }

    private void ConfigureToggle(ToolbarToggleButton toggle, string key, object content, string? hintKey = null)
    {
        toggle.Content = content;
        bindings.Add(toggle.Bind(ToolTip.TipProperty, Localization.Observe(hintKey ?? key).ToBinding()));
        bindings.Add(toggle.Bind(AutomationProperties.NameProperty, Localization.Observe(key).ToBinding()));
    }

    private static Separator ToolbarSeparator(string name) => new()
    {
        Name = name, Width = 1, Height = 24, Margin = new(4, 0), VerticalAlignment = VerticalAlignment.Center,
        Template = new FuncControlTemplate<Separator>((_, _) =>
        {
            var line = new Border();
            line.Bind(Border.BackgroundProperty, new DynamicResourceExtension("PreviewBorder"));
            return line;
        })
    };

    private void Toggle(string name)
    {
        var requested = toggles[name].IsChecked == true;
        coordinator.ApplySelectionFormatting(Math.Min(selectionStart, selectionEnd), Math.Abs(selectionEnd - selectionStart), style => name switch
        {
            "Bold" => session.Fonts.ToggleBold(style, requested),
            "Italic" => session.Fonts.ToggleItalic(style, requested),
            "Underline" => new SubtitleInlineStyleOverride { Underline = requested },
            _ => new SubtitleInlineStyleOverride { Strikethrough = requested }
        });
    }

    private async Task ApplyPresetAsync()
    {
        if (IsEditingVisualState)
        {
            await ApplyHighlightPresetAsync();
            return;
        }
        if (presets.SelectedItem is not StylePresetListItem selected || coordinator.Line is not { } line || !coordinator.TryCommit())
        {
            return;
        }
        var start = Math.Min(selectionStart, selectionEnd);
        var length = Math.Abs(selectionEnd - selectionStart);
        var snapshot = session.Editor.Snapshot;
        var prepared = await SubtitleStylePresetService.PrepareAsync(session.StyleLibrary.Snapshot.Presets.Single(value => value.Id == selected.Id),
            snapshot, session.ProjectDirectory);
        if (coordinator.Line?.Id != line.Id || !ReferenceEquals(snapshot, session.Editor.Snapshot))
        {
            return;
        }
        session.Editor.Apply("Apply subtitle selection preset", _ => ProjectEditingOperations.ApplySubtitleInlineStyle(
            prepared.Project, line.Id, start, length,
            SubtitleInlineStyleOverride.FromStyle(SubtitleStyleScaling.ConvertUnits(prepared.Style, line.Style.Scaling,
                snapshot.Width, snapshot.Height))));
    }

    private Task ApplyHighlightPresetAsync()
    {
        if ((presets.SelectedItem as StylePresetListItem)?.Id == selectionOverridePresetId)
        {
            return Task.CompletedTask;
        }
        if (presets.SelectedItem is not StylePresetListItem selected || coordinator.Line is not { } line || !coordinator.TryCommit())
        {
            return Task.CompletedTask;
        }
        var snapshot = session.Editor.Snapshot;
        var preset = session.StyleLibrary.Snapshot.Presets.FirstOrDefault(value => value.Id == selected.Id);
        var value = selected.Id == Guid.Empty ? null : preset is not null
            ? KaraokeHighlightStyle.FromStyle(preset.Id, preset.Name, SubtitleStyleScaling.ConvertUnits(preset.Style,
                line.Style.Scaling, snapshot.Width, snapshot.Height))
            : line.KaraokeStyle?.PresetId == selected.Id ? line.KaraokeStyle : null;
        if (coordinator.Line?.Id == line.Id && ReferenceEquals(snapshot, session.Editor.Snapshot))
        {
            coordinator.ApplyHighlightStyle(value);
        }
        return Task.CompletedTask;
    }

    private void RefreshHighlightPresets(SubtitleLine? line)
    {
        var style = coordinator.VisualState == KaraokeVisualState.ACTIVE ? line?.KaraokeStyle : null;
        var targetChanged = line?.Id != loadedHighlightCueId || style != loadedHighlightStyle || coordinator.VisualState != loadedVisualState;
        var id = targetChanged ? style?.PresetId ?? Guid.Empty : highlightPresetId ?? style?.PresetId ?? Guid.Empty;
        loadedHighlightCueId = line?.Id;
        loadedHighlightStyle = style;
        loadedVisualState = coordinator.VisualState;
        var options = new List<StylePresetListItem> { new(Guid.Empty, Localization.Get(
            coordinator.VisualState == KaraokeVisualState.INACTIVE ? "Workbench.InheritBodyVisual" : "Workbench.DefaultKaraokeStyle")) };
        options.AddRange(session.ViewModel.Styles.Subtitle.Presets);
        var start = Math.Min(selectionStart, selectionEnd);
        var end = Math.Max(selectionStart, selectionEnd);
        if (start == end)
        {
            start = 0;
            end = line?.Text.Length ?? 0;
        }
        if (line?.KaraokeStyleSpans.Any(span => span.Utf16Start < end &&
            span.Utf16Start + span.Utf16Length > start &&
            (coordinator.VisualState == KaraokeVisualState.INACTIVE ? span.InactiveStyle : span.ActiveStyle) is { HasOverrides: true }) == true)
        {
            options.Add(new(selectionOverridePresetId, Localization.Get("Workbench.CustomKaraokeStyle")));
            id = selectionOverridePresetId;
        }
        if (style is not null && options.All(value => value.Id != style.PresetId))
        {
            options.Add(new(style.PresetId, style.PresetName));
        }
        presets.ItemsSource = options;
        presets.SelectedItem = options.FirstOrDefault(value => value.Id == id) ?? options[0];
        highlightPresetId = (presets.SelectedItem as StylePresetListItem)?.Id;
    }

    private void OnTextEdit(object? sender, SubtitleTextEditEventArgs e)
    {
        coordinator.EditText(e.Start, e.Length, e.Replacement);
    }

    private void OnStyleFieldKeyDown(object? sender, KeyEventArgs e)
    {
        var field = StyleField(e.Source as Control);
        if (field is null)
        {
            return;
        }
        if (e.Key == Key.Enter)
        {
            ++styleFocusRevision;
            coordinator.TryCommit();
            e.Handled = true;
        }
    }

    private void OnStyleFieldLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Control source && source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>()
            .Any(input => input.IsTitleDragging))
        {
            return;
        }
        if (synchronizing || completingInput || StyleField(e.Source as Control) is null)
        {
            return;
        }
        if (formattingPointerActive)
        {
            formattingFocusPending = true;
            return;
        }
        QueueStyleFocusCommit();
    }

    private void EndFormattingPointer()
    {
        if (!formattingPointerActive)
        {
            return;
        }
        formattingPointerActive = false;
        if (formattingFocusPending)
        {
            formattingFocusPending = false;
            QueueStyleFocusCommit();
        }
    }

    private void QueueStyleFocusCommit()
    {
        var revision = ++styleFocusRevision;
        var draft = styleFields.DataContext;
        var document = session.DocumentSnapshot;
        var root = hostWindow;
        Dispatcher.UIThread.Post(() =>
        {
            if (!disposed && revision == styleFocusRevision && ReferenceEquals(draft, styleFields.DataContext) &&
                root is not null && ReferenceEquals(root, hostWindow) &&
                ReferenceEquals(document, session.DocumentSnapshot) &&
                !styleFields.GetVisualDescendants().OfType<Button>().Any(button => button.Flyout?.IsOpen == true) &&
                !(clipPopup.IsOpen && root.FocusManager.GetFocusedElement() is Visual focused &&
                  (ReferenceEquals(focused, timingFields) || focused.GetVisualAncestors().Contains(timingFields))))
            {
                if (formattingPointerActive)
                {
                    formattingFocusPending = true;
                    return;
                }
                coordinator.TryCommit();
            }
        }, DispatcherPriority.Background);
    }

    private static string? StyleField(Control? source)
    {
        var ancestors = source?.GetSelfAndVisualAncestors().OfType<Control>().ToArray() ?? [];
        var numeric = ancestors.OfType<NumericDraftInput>().FirstOrDefault();
        if (numeric?.Name is { } name)
        {
            return name switch
            {
                "SelectionLetterSpacingInput" => nameof(SubtitleDetailsStyleDraft.LetterSpacingText),
                "SelectionFillBlurInput" => nameof(SubtitleDetailsStyleDraft.FillBlurText),
                "SelectionStrokeBlurInput" => nameof(SubtitleDetailsStyleDraft.StrokeBlurText),
                "SelectionFontSizeInput" => nameof(SubtitleDetailsStyleDraft.FontSizeText),
                "SelectionStrokeWidthInput" => nameof(SubtitleDetailsStyleDraft.StrokeWidthText),
                "SelectionShadowXInput" => nameof(SubtitleDetailsStyleDraft.ShadowXText),
                "SelectionShadowYInput" => nameof(SubtitleDetailsStyleDraft.ShadowYText),
                "SelectionShadowBlurInput" => nameof(SubtitleDetailsStyleDraft.ShadowBlurText),
                _ => null
            };
        }
        if (ancestors.OfType<ColorDraftInput>().FirstOrDefault() is { } color)
        {
            return color.Name switch
            {
                "SelectionFillInput" => nameof(SubtitleDetailsStyleDraft.Fill),
                "SelectionStrokeInput" => nameof(SubtitleDetailsStyleDraft.Stroke),
                _ => nameof(SubtitleDetailsStyleDraft.Shadow)
            };
        }
        return ancestors.OfType<FontFamilyPicker>().Any() ? nameof(SubtitleDetailsStyleDraft.FontFamily) : null;
    }

    private void OnSelection(object? sender, EventArgs e)
    {
        if (synchronizing || sender is not RichSubtitleEditor editor)
        {
            return;
        }
        CancelNumericDrags();
        var start = editor.SelectionStart;
        var end = editor.SelectionEnd;
        if (!coordinator.SetStyleSelection(Math.Min(start, end), Math.Abs(end - start)) ||
            !coordinator.FollowTextSelection(Math.Min(start, end), Math.Abs(end - start)))
        {
            SetTextSelection(selectionStart, selectionEnd);
            return;
        }
        selectionStart = start;
        selectionEnd = end;
        session.SetTextCaret(coordinator.Line?.Id ?? Guid.Empty, end);
        Refresh();
    }

    private void SetTextSelection(int start, int end)
    {
        synchronizing = true;
        try
        {
            selectionStart = start;
            selectionEnd = end;
            rich.SetSelection(start, end);
        }
        finally
        {
            synchronizing = false;
        }
        coordinator.SetStyleSelection(Math.Min(start, end), Math.Abs(end - start));
    }

    private void Refresh()
    {
        synchronizing = true;
        try
        {
            var line = coordinator.Line;
            if (line?.Id != previousTarget)
            {
                clipPopup.ForceClose();
                previousTarget = line?.Id;
                selectionStart = selectionEnd = 0;
            }
            RefreshTimingControls(line);
            axis.SetContent(line, line is null ? MediaTime.Zero : line.Start - coordinator.ContentOrigin,
                coordinator.SelectedClipId, coordinator.SelectedClipIds);
            var localTime = session.ProjectPosition - coordinator.ContentOrigin;
            rich.SetContent(coordinator.PreviewDocument, line, session.ProjectDirectory, localTime >= MediaTime.Zero ? localTime : MediaTime.Zero,
                coordinator.VisualState switch
                {
                    KaraokeVisualState.ACTIVE => SubtitlePreviewMode.HIGHLIGHTED,
                    KaraokeVisualState.INACTIVE => SubtitlePreviewMode.INACTIVE,
                    _ => SubtitlePreviewMode.NORMAL
                }, session.Fonts.Catalog);
            rich.SetSelection(selectionStart, selectionEnd);
            error.Text = coordinator.Error ?? rich.RenderDiagnostic;
            error.IsVisible = !string.IsNullOrEmpty(error.Text);
            if (coordinator.IsPlaying != lastPlaybackState)
            {
                play.Content = WorkbenchIcon.Create(coordinator.IsPlaying ? "Pause" : "Play");
            }
            var playbackHint = Localization.Get(coordinator.IsPlaying ? "Workbench.Pause" : "Workbench.PlaySegment");
            ToolTip.SetTip(play, playbackHint);
            AutomationProperties.SetName(play, playbackHint);
            lastPlaybackState = coordinator.IsPlaying;
            axis.IsVisible = line is not null;
            visualState.IsEnabled = line is not null;
            visualState.ItemsSource = new SubtitleVisualStateChoice[]
            {
                new(null, Localization.Get("Workbench.VisualState.Normal")),
                new(KaraokeVisualState.INACTIVE, Localization.Get("Workbench.VisualState.Inactive")),
                new(KaraokeVisualState.ACTIVE, Localization.Get("Workbench.VisualState.Active"))
            };
            visualState.SelectedIndex = coordinator.VisualState switch
            {
                KaraokeVisualState.INACTIVE => 1,
                KaraokeVisualState.ACTIVE => 2,
                _ => 0
            };
            var appearanceHint = Localization.Get("Workbench.VisualState.Select") + " · " +
                (visualState.SelectedItem as SubtitleVisualStateChoice)?.Name;
            ToolTip.SetTip(visualState, appearanceHint);
            AutomationProperties.SetName(visualState, appearanceHint);
            styleFields.DataContext = IsEditingVisualState ? coordinator.HighlightDraft : coordinator.StyleDraft;
            rich.IsEnabled = line is not null;
            var start = Math.Min(selectionStart, selectionEnd);
            var end = Math.Max(selectionStart, selectionEnd);
            styleFields.IsEnabled = line is not null && (IsEditingVisualState ? line.Text.Length > 0 : start != end);
            presets.IsEnabled = styleFields.IsEnabled;
            foreach (var field in bodyOnlyFields)
            {
                field.IsEnabled = line is not null && !IsEditingVisualState && selectionStart != selectionEnd;
            }
            foreach (var action in selectionActions)
            {
                action.IsEnabled = styleFields.IsEnabled;
            }
            if (IsEditingVisualState)
            {
                RefreshHighlightPresets(line);
            }
            else
            {
                presets.ItemsSource = session.ViewModel.Styles.Subtitle.Presets;
                presets.SelectedItem = session.ViewModel.Styles.Subtitle.Presets.FirstOrDefault(value => value.Id == bodyPresetId) ?? session.ViewModel.Styles.Subtitle.SelectedPreset;
            }
            var style = coordinator.SelectionStyle();
            if (!coordinator.StyleDraft.IsFieldDirty(nameof(SubtitleDetailsStyleDraft.FontFamily)))
            {
                selectionFont.SetCurrentFont(SubtitleFontSelectionService.FromStyle(style));
            }
            foreach (var pair in toggles)
            {
                pair.Value.IsChecked = pair.Key switch
                {
                    "Bold" => style.Bold,
                    "Italic" => style.Italic,
                    "Underline" => style.Underline,
                    _ => style.Strikethrough
                };
            }
        }
        finally
        {
            synchronizing = false;
        }
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0 || e.Key is not (Key.Z or Key.Y))
        {
            return;
        }
        e.Handled = true;
        if (coordinator.TryCommit())
        {
            if (e.Key == Key.Y || (e.KeyModifiers & KeyModifiers.Shift) != 0)
            {
                session.Editor.Redo();
            }
            else
            {
                session.Editor.Undo();
            }
        }
    }

    private void OnCoordinatorChanged(object? sender, EventArgs e)
    {
        if (numericDragSnapshot is not null && NumericDragTargetMatches())
        {
            return;
        }
        CancelNumericDrags();
        Refresh();
    }
    private void OnStyleLibraryChanged(object? sender, EventArgs e) => Refresh();
    private void OnFontsChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshFontCandidates);
            return;
        }

        RefreshFontCandidates();
    }

    private void RefreshFontCandidates()
    {
        if (!disposed)
        {
            selectionFont.RefreshFontCandidates(session.Fonts.Candidates);
            Refresh();
        }
    }

    private void OnFontPickerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == AutoCompleteBox.IsDropDownOpenProperty && selectionFont.IsDropDownOpen)
        {
            _ = LoadFontCandidatesAsync();
        }
    }

    private async Task LoadFontCandidatesAsync()
    {
        if (disposed || session.IsClosing)
        {
            return;
        }

        try
        {
            await session.Fonts.EnsureLoadedAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (!disposed && !session.IsClosing)
            {
                session.LogError("Fonts", error);
            }
        }
    }
    private void OnLanguageChanged(object? sender, EventArgs e) => Refresh();
    private void OnGesturesCancelled(object? sender, EventArgs e) => CancelGestures();
    private void OnPreviewUpdated(object? sender, VideoPreviewUpdate e)
    {
        if (coordinator.IsPlaying != lastPlaybackState && HasFocusWithin())
        {
            Refresh();
            return;
        }
        if (coordinator.IsPlaying && HasFocusWithin() && Environment.TickCount64 - lastPreviewRefresh >= 100)
        {
            lastPreviewRefresh = Environment.TickCount64;
            Refresh();
        }
    }
    public string PanelId => WorkbenchPanelIds.SUBTITLE_DETAILS;

    /// <summary>判断当前焦点是否属于可结束输入的详情字段。</summary>
    public bool CanExecuteFocusCommand(WorkbenchCommand command, IInputElement focusedElement)
    {
        return command == WorkbenchCommand.END_TEXT_INPUT && !rich.IsSelectionGestureActive &&
            InputField(focusedElement as Control) is not null;
    }

    /// <summary>完成当前字段并将焦点保留在详情面板，阻止旧失焦回调重复提交。</summary>
    public bool TryExecuteFocusCommand(WorkbenchCommand command, IInputElement focusedElement)
    {
        if (command != WorkbenchCommand.END_TEXT_INPUT || InputField(focusedElement as Control) is not { } field)
        {
            return false;
        }
        ++styleFocusRevision;
        ++focusRevision;
        completingInput = true;
        try
        {
            if (!coordinator.CompleteInput(field, IsEditingVisualState))
            {
                return false;
            }
            return Focus();
        }
        finally
        {
            completingInput = false;
        }
    }

    private string? InputField(Control? source)
    {
        var ancestors = source?.GetSelfAndVisualAncestors().OfType<Control>().ToArray() ?? [];
        if (ancestors.Contains(rich))
        {
            return "Text";
        }
        if (TimingInputField(ancestors) is { } timingField)
        {
            return timingField;
        }
        return StyleField(source);
    }

    /// <summary>取消面板内未提交的指针手势。</summary>
    public void CancelGestures()
    {
        CancelNumericDrags();
        formattingPointerActive = formattingFocusPending = false;
        ++styleFocusRevision;
        axis.CancelGesture();
        CancelManualTiming(false);
    }

    /// <summary>将验证失败的详情输入重新聚焦。</summary>
    public void FocusInvalidField(string? fieldKey)
    {
        fieldKey ??= coordinator.InvalidFieldKey;
        var parts = fieldKey?.Split('.') ?? [];
        if (parts.Length > 1 && parts[0] is "Selection" or "Highlight")
        {
            Refresh();
            var name = parts[1] switch
            {
                nameof(SubtitleDetailsStyleDraft.FontFamily) => "SelectionFontInput",
                nameof(SubtitleDetailsStyleDraft.LetterSpacingText) => "SelectionLetterSpacingInput",
                nameof(SubtitleDetailsStyleDraft.FillBlurText) => "SelectionFillBlurInput",
                nameof(SubtitleDetailsStyleDraft.StrokeBlurText) => "SelectionStrokeBlurInput",
                nameof(SubtitleDetailsStyleDraft.FontSizeText) => "SelectionFontSizeInput",
                nameof(SubtitleDetailsStyleDraft.Fill) => "SelectionFillInput",
                nameof(SubtitleDetailsStyleDraft.Stroke) => "SelectionStrokeInput",
                nameof(SubtitleDetailsStyleDraft.StrokeWidthText) => "SelectionStrokeWidthInput",
                nameof(SubtitleDetailsStyleDraft.Shadow) => "SelectionShadowInput",
                nameof(SubtitleDetailsStyleDraft.ShadowXText) => "SelectionShadowXInput",
                nameof(SubtitleDetailsStyleDraft.ShadowYText) => "SelectionShadowYInput",
                nameof(SubtitleDetailsStyleDraft.ShadowBlurText) => "SelectionShadowBlurInput",
                _ => null
            };
            var input = styleFields.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Name == name);
            if (input is not null)
            {
                FocusInput(input, parts.Length > 2 ? parts[2] : null);
                return;
            }
        }
        if (FocusTimingInput(fieldKey))
        {
            return;
        }
        else
        {
            rich.Focus();
        }
    }

    private static void FocusInput(Control input, string? colorField = null)
    {
        input.BringIntoView();
        if (input is ColorDraftInput color && color.TryFocusInvalidField(colorField))
        {
            return;
        }
        var text = input.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        if (text is not null)
        {
            text.Focus();
        }
        else
        {
            input.Focus();
        }
    }

    private bool HasFocusWithin()
    {
        if (hostWindow is not { IsActive: true })
        {
            return false;
        }
        var focused = hostWindow.FocusManager.GetFocusedElement() as Visual;
        return focused is not null && (ReferenceEquals(focused, this) || focused.GetVisualAncestors().Contains(this) ||
            clipPopup.IsOpen && clipPopup.Child is { } popupContent &&
            (ReferenceEquals(focused, popupContent) || focused.GetVisualAncestors().Contains(popupContent))) ||
            createTimingPopup.IsOpen ||
            this.GetVisualDescendants().OfType<Button>().Any(button => button.Flyout?.IsOpen == true) ||
            this.GetVisualDescendants().OfType<FontFamilyPicker>().Any(picker => picker.IsDropDownOpen) ||
            this.GetVisualDescendants().OfType<ComboBox>().Any(picker => picker.IsDropDownOpen);
    }

    private void OnPanelGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (coordinator.IsPlaying != lastPlaybackState)
        {
            Refresh();
        }
    }

    private void OnPanelLostFocus(object? sender, RoutedEventArgs e)
    {
        var revision = ++focusRevision;
        var root = hostWindow;
        Dispatcher.UIThread.Post(() =>
        {
            if (!disposed && revision == focusRevision && ReferenceEquals(root, hostWindow) && !HasFocusWithin())
            {
                _ = session.RunCommandAsync(coordinator.StopPlaybackAsync);
            }
        }, DispatcherPriority.Background);
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ = LoadFontCandidatesAsync();
        hostWindow = TopLevel.GetTopLevel(this) as Window;
        if (hostWindow is not null)
        {
            hostWindow.Activated += OnHostActivated;
            hostWindow.Deactivated += OnHostDeactivated;
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ++focusRevision;
        ++styleFocusRevision;
        if (hostWindow is not null)
        {
            hostWindow.Activated -= OnHostActivated;
            hostWindow.Deactivated -= OnHostDeactivated;
            hostWindow = null;
        }
        axis.CancelGesture();
        clipPopup.ForceClose();
        CancelManualTiming(false);
        _ = session.RunCommandAsync(coordinator.StopPlaybackAsync);
        base.OnDetachedFromVisualTree(e);
    }

    private void OnHostDeactivated(object? sender, EventArgs e) => _ = session.RunCommandAsync(coordinator.StopPlaybackAsync);

    private void OnHostActivated(object? sender, EventArgs e)
    {
        if (!disposed && coordinator.InvalidFieldKey is { } field)
        {
            var root = hostWindow;
            var targetId = coordinator.Line?.Id;
            Dispatcher.UIThread.Post(() =>
            {
                if (!disposed && ReferenceEquals(root, hostWindow) && root?.IsActive == true &&
                    coordinator.Line?.Id == targetId && coordinator.InvalidFieldKey == field)
                {
                    FocusInvalidField(field);
                }
            }, DispatcherPriority.Background);
        }
    }

    /// <summary>释放详情输入、共享排版缓存及本地订阅。</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        if (hostWindow is not null)
        {
            hostWindow.Activated -= OnHostActivated;
            hostWindow.Deactivated -= OnHostDeactivated;
        }
        _ = session.RunCommandAsync(coordinator.StopPlaybackAsync);
        coordinator.Changed -= OnCoordinatorChanged;
        session.NumericGestureCancellationRequested -= OnNumericGestureCancellation;
        session.StyleLibraryChanged -= OnStyleLibraryChanged;
        session.Fonts.Changed -= OnFontsChanged;
        selectionFont.PropertyChanged -= OnFontPickerChanged;
        session.ViewModel.GesturesCancelled -= OnGesturesCancelled;
        session.PreviewUpdated -= OnPreviewUpdated;
        Localization.LanguageChanged -= OnLanguageChanged;
        axis.CancelGesture();
        clipPopup.ForceClose();
        CancelManualTiming(false);
        rich.Dispose();
        foreach (var binding in bindings)
        {
            binding.Dispose();
        }
        bindings.Clear();
    }
}
