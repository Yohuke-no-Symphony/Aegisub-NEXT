using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Styling;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace AegiNext.Desktop.Panels.SubtitleDetails;

internal sealed partial class SubtitleDetailsPanelView
{
    private readonly NumericDraftInput startTime = TimingInput("KaraokeStartInput");
    private readonly NumericDraftInput endTime = TimingInput("KaraokeEndInput");
    private readonly NumericDraftInput leadingDelayTime = TimingInput("KaraokeLeadingDelayInput");
    private readonly ToolbarToggleButton linkedTiming = new() { Name = "LinkedKaraokeTimingToggle" };
    private readonly Button createTiming = new() { Name = "CreateSelectedTimingButton" };
    private readonly Button generateTiming = new() { Name = "GenerateAllTimingButton" };
    private readonly Button restoreTiming = new() { Name = "RestoreCachedTimingButton" };
    private readonly Button splitTiming = new() { Name = "SplitKaraokeGroupButton" };
    private readonly Button mergeTiming = new() { Name = "MergeSelectedKaraokeGroupsButton" };
    private readonly Button fitTiming = new() { Name = "FitKaraokeAxisButton" };
    private readonly DraftPopup createTimingPopup = new()
    {
        Placement = PlacementMode.BottomEdgeAlignedLeft, VerticalOffset = 4
    };
    private readonly NumericDraftInput creationStart = TimingInput("CreateTimingStartInput");
    private readonly NumericDraftInput creationEnd = TimingInput("CreateTimingEndInput");
    private readonly TextBlock creationText = new() { TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
    private SubtitleLine? creationBaseline;
    private MediaTime creationOffset;
    private int creationSelectionStart;
    private int creationSelectionLength;

    private WrapPanel BuildTimingEditor()
    {
        foreach (var (input, edit) in new (NumericDraftInput Input, Action<string> Edit)[]
        {
            (startTime, coordinator.EditStart), (endTime, coordinator.EditEnd),
            (duration, coordinator.EditDuration), (leadingDelayTime, coordinator.EditLeadingDelay)
        })
        {
            input.PropertyChanged += (_, e) =>
            {
                if (!synchronizing && e.Property == NumericDraftInput.RawTextProperty)
                {
                    edit(input.RawText);
                }
            };
            input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    coordinator.TryCommit();
                    e.Handled = true;
                }
            };
            input.LostFocus += (_, _) =>
            {
                if (input.IsTitleDragging)
                {
                    return;
                }
                if (!completingInput)
                {
                    if (formattingPointerActive)
                    {
                        formattingFocusPending = true;
                    }
                    else
                    {
                        QueueStyleFocusCommit();
                    }
                }
            };
        }
        kind.SelectionChanged += (_, _) =>
        {
            if (!synchronizing && kind.SelectedIndex >= 0)
            {
                coordinator.SetHighlightKind((KaraokeHighlightKind)kind.SelectedIndex);
            }
        };
        linkedTiming.IsCheckedChanged += (_, _) =>
        {
            if (!synchronizing)
            {
                coordinator.LinkedTimingEnabled = linkedTiming.IsChecked == true;
            }
        };
        ConfigureToggle(linkedTiming, "Workbench.LinkedKaraokeTiming", WorkbenchIcon.Create("LinkedTiming"), "Workbench.LinkedKaraokeTimingHint");
        timingFields.Children.Add(Field("Workbench.KaraokeClipStart", startTime));
        timingFields.Children.Add(Field("Workbench.KaraokeClipEnd", endTime));
        timingFields.Children.Add(Field("Workbench.ClipDuration", duration));
        timingFields.Children.Add(Field("Workbench.HighlightBehavior", kind));
        timingFields.Children.Add(Field("Workbench.KaraokeEarliestStart", leadingDelayTime));
        snap.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty)
            {
                axis.IsSnapEnabled = snap.IsChecked == true;
            }
        };
        keepTimeLabels.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty)
            {
                axis.KeepDurationLabelsVisible = keepTimeLabels.IsChecked == true;
            }
        };
        // bindings.Add(axis.Bind(ToolTip.TipProperty, Localization.Observe("Workbench.KaraokeAxisGestureHint").ToBinding()));
        var popupFrame = PopupFrame(timingFields);
        popupFrame.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && clipPopup.IsOpen)
            {
                completingInput = true;
                try
                {
                    coordinator.Restore("Timing");
                    clipPopup.ForceClose();
                    axis.Focus();
                    e.Handled = true;
                }
                finally
                {
                    completingInput = false;
                }
            }
        }, RoutingStrategies.Tunnel, true);
        clipPopup.Child = popupFrame;
        FlyoutBase.SetAttachedFlyout(axis, clipPopup);
        ConfigureTimingAction(createTiming, "Workbench.CreateSelectedTiming", "Add", BeginManualTiming);
        ConfigureTimingAction(generateTiming, "Workbench.GenerateAllTiming", "Reset", () => coordinator.GenerateAllTiming());
        ConfigureTimingAction(restoreTiming, "Workbench.RestoreCachedTiming", "RestoreCachedTiming", () => coordinator.RestoreCachedTiming());
        ConfigureTimingAction(splitTiming, "Workbench.SplitKaraokeGroup", "Split", () => coordinator.SplitSelectedGroup());
        ConfigureTimingAction(mergeTiming, "Workbench.MergeSelectedKaraokeGroups", "Merge", () => coordinator.MergeSelectedGroups(), false);
        ToolTip.SetShowOnDisabled(mergeTiming, true);
        ConfigureTimingAction(fitTiming, "Workbench.FitKaraokeAxis", "Timeline", () => axis.FitToContent());
        foreach (var control in new Control[] { enableKaraoke, play, loop, snap, keepTimeLabels, linkedTiming })
        {
            control.Margin = new(0, 0, 6, 6);
        }
        linkedTiming.Margin = new(0, 0, 14, 6);
        generateTiming.Margin = new(0);
        var resetActions = new StackPanel
        {
            Name = "KaraokeResetActions", Orientation = Orientation.Horizontal, Margin = new(8, 0, 0, 6),
            Children = { ToolbarSeparator("KaraokeResetSeparator"), generateTiming }
        };
        BuildManualTimingPopup();
        return new()
        {
            Name = "KaraokeTimingActions", Children =
            {
                enableKaraoke, play, loop, snap, keepTimeLabels, linkedTiming,
                createTiming, restoreTiming, splitTiming, mergeTiming, fitTiming, resetActions
            }
        };
    }

    private void ConfigureTimingAction(Button button, string key, string icon, Action action, bool bindHint = true)
    {
        button.Content = WorkbenchIcon.Create(icon);
        button.Classes.Add("icon-button");
        button.Margin = new(0, 0, 6, 6);
        bindings.Add(button.Bind(AutomationProperties.NameProperty, Localization.Observe(key).ToBinding()));
        if (bindHint)
        {
            bindings.Add(button.Bind(ToolTip.TipProperty, Localization.Observe(key + "Hint").ToBinding()));
        }
        button.Click += async (_, _) => await session.RunCommandAsync(() =>
        {
            action();
            Refresh();
            return Task.CompletedTask;
        });
    }

    private void BuildManualTimingPopup()
    {
        var fields = new WrapPanel { Width = 424 };
        fields.Children.Add(Field("Workbench.KaraokeClipStart", creationStart));
        fields.Children.Add(Field("Workbench.KaraokeClipEnd", creationEnd));
        var confirm = Button("Workbench.ConfirmCreateTiming", "Add", () =>
        {
            ConfirmManualTiming();
            return Task.CompletedTask;
        });
        var cancel = Button("Workbench.Cancel", "Cancel", () =>
        {
            CancelManualTiming(true);
            return Task.CompletedTask;
        });
        cancel.Name = "CancelCreateTimingButton";
        var content = new StackPanel
        {
            Width = 424, Spacing = 8, Children =
            {
                Label("Workbench.CreateSelectedTiming"), creationText, fields,
                new StackPanel { Orientation = Orientation.Horizontal, Children = { confirm, cancel } }
            }
        };
        var frame = PopupFrame(content);
        frame.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                CancelManualTiming(true);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                ConfirmManualTiming();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel, true);
        createTimingPopup.Child = frame;
        createTimingPopup.Closing += (_, e) =>
        {
            e.Cancel = creationBaseline is not null && coordinator.Error is not null;
            if (!e.Cancel)
            {
                creationBaseline = null;
            }
        };
        FlyoutBase.SetAttachedFlyout(createTiming, createTimingPopup);
    }

    private static Border PopupFrame(Control content)
    {
        var frame = new Border { Padding = new(12), BorderThickness = new(1), CornerRadius = new(6), Child = content };
        frame.Bind(Border.BackgroundProperty, new DynamicResourceExtension("PreviewSurface"));
        frame.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("PreviewBorder"));
        frame.Classes.Add("business-surface");
        return frame;
    }

    private void BeginManualTiming()
    {
        if (!coordinator.TryCommit() || coordinator.Line is not { } line)
        {
            return;
        }
        creationSelectionStart = Math.Min(selectionStart, selectionEnd);
        creationSelectionLength = Math.Abs(selectionEnd - selectionStart);
        if (!CanCreateTiming(line, creationSelectionStart, creationSelectionLength))
        {
            return;
        }
        creationBaseline = line;
        creationOffset = line.Start - coordinator.ContentOrigin;
        creationText.Text = line.Text.Substring(creationSelectionStart, creationSelectionLength);
        creationStart.RawText = creationEnd.RawText = string.Empty;
        createTimingPopup.ShowAt(createTiming);
        creationStart.FocusInput();
    }

    private void ConfirmManualTiming()
    {
        if (creationBaseline is null || coordinator.Line != creationBaseline ||
            creationOffset != creationBaseline.Start - coordinator.ContentOrigin)
        {
            CancelManualTiming(false);
            return;
        }
        if (coordinator.CreateTiming(creationSelectionStart, creationSelectionLength, creationStart.RawText, creationEnd.RawText))
        {
            CancelManualTiming(false);
            axis.Focus();
        }
        else
        {
            FocusInput(coordinator.InvalidFieldKey == "End" ? creationEnd : creationStart);
        }
    }

    private void CancelManualTiming(bool dismissError)
    {
        creationBaseline = null;
        createTimingPopup.ForceClose();
        if (dismissError)
        {
            coordinator.DismissCreationError();
        }
    }

    private void RefreshTimingControls(SubtitleLine? line)
    {
        var enabled = coordinator.IsKaraokeEnabled;
        enableKaraoke.IsEnabled = line is { Text.Length: > 0 };
        enableKaraoke.IsChecked = enabled;
        play.IsEnabled = enabled && session.Controller.MediaInfo is not null;
        loop.IsEnabled = snap.IsEnabled = keepTimeLabels.IsEnabled = enabled;
        linkedTiming.IsEnabled = fitTiming.IsEnabled = enabled;
        axis.IsEnabled = enabled;
        if (creationBaseline is not null && (!enabled || line != creationBaseline ||
            Math.Min(selectionStart, selectionEnd) != creationSelectionStart ||
            Math.Abs(selectionStart - selectionEnd) != creationSelectionLength))
        {
            CancelManualTiming(false);
        }
        SetTimingText(startTime, coordinator.StartText);
        SetTimingText(endTime, coordinator.EndText);
        SetTimingText(duration, coordinator.DurationText);
        SetTimingText(leadingDelayTime, coordinator.LeadingDelayText);
        var clip = line?.Karaoke.FirstOrDefault(item => item.Id == coordinator.SelectedClipId);
        kind.ItemsSource = Enum.GetValues<KaraokeHighlightKind>().Select(value => Localization.Get("Workbench.KaraokeKind." + value)).ToArray();
        kind.SelectedIndex = clip is null ? -1 : (int)clip.HighlightKind;
        kind.IsEnabled = duration.IsEnabled = startTime.IsEnabled = endTime.IsEnabled = enabled && clip is not null;
        linkedTiming.IsChecked = coordinator.LinkedTimingEnabled;
        axis.IsTimingLinked = coordinator.LinkedTimingEnabled;
        leadingDelayTime.IsEnabled = enabled;
        createTiming.IsEnabled = enabled && line is not null && CanCreateTiming(line, Math.Min(selectionStart, selectionEnd), Math.Abs(selectionEnd - selectionStart));
        generateTiming.IsEnabled = enabled;
        restoreTiming.IsVisible = line is { InactiveKaraoke.IsEmpty: false };
        restoreTiming.IsEnabled = enabled && line is { InactiveKaraoke.IsEmpty: false };
        var boundaries = clip is null ? null : new SubtitleTextBoundaries(line!.Text);
        splitTiming.IsEnabled = enabled && clip is not null && boundaries!.IndexOf(clip.Utf16Start + clip.Utf16Length) -
            boundaries.IndexOf(clip.Utf16Start) > 1;
        var mergeReason = coordinator.MergeSelectionErrorKey;
        mergeTiming.IsEnabled = enabled && mergeReason is null;
        ToolTip.SetTip(mergeTiming, Localization.Get(mergeReason ?? "Workbench.MergeSelectedKaraokeGroupsHint"));
        if (clip is null && clipPopup.IsOpen)
        {
            clipPopup.ForceClose();
        }
    }

    private static bool CanCreateTiming(SubtitleLine line, int start, int length)
    {
        return length > 0 && start >= 0 && (long)start + length <= line.Text.Length &&
            !line.Karaoke.Concat(line.InactiveKaraoke).Any(clip => clip.Utf16Start < start + length &&
                clip.Utf16Start + clip.Utf16Length > start);
    }

    private static NumericDraftInput TimingInput(string name)
    {
        return new()
        {
            Name = name, Width = 200, Minimum = decimal.MinValue, Maximum = decimal.MaxValue,
            Increment = 0.01m, ShowButtonSpinner = false, PreserveDoublePrecision = true,
            NumberFormat = CultureInfo.InvariantCulture.NumberFormat
        };
    }

    private static void SetTimingText(NumericDraftInput input, string text)
    {
        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            input.Value = value;
        }
        input.RawText = text;
    }

    private string? TimingInputField(IReadOnlyList<Control> ancestors)
    {
        if (ancestors.Contains(startTime))
        {
            return "Start";
        }
        if (ancestors.Contains(endTime))
        {
            return "End";
        }
        if (ancestors.Contains(leadingDelayTime))
        {
            return "LeadingDelay";
        }
        return ancestors.Contains(duration) ? "Duration" : null;
    }

    private bool FocusTimingInput(string? fieldKey)
    {
        if (createTimingPopup.IsOpen && fieldKey is "Start" or "End")
        {
            FocusInput(fieldKey == "Start" ? creationStart : creationEnd);
            return true;
        }
        var input = fieldKey switch
        {
            "Start" => startTime, "End" => endTime, "Duration" => duration, "LeadingDelay" => leadingDelayTime,
            _ => null
        };
        if (input is null)
        {
            return false;
        }
        if (!clipPopup.IsOpen)
        {
            clipPopup.ShowAt(axis);
        }
        FocusInput(input);
        return true;
    }
}
