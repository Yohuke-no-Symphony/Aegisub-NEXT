using AegiNext.Core.Effects;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Editing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Controls;

/// <summary>复用共享草稿输入编辑模板参数，只发布提交意图。</summary>
public sealed class EffectScriptParameterEditor : UserControl
{
    public static readonly StyledProperty<EffectScriptParameterDraftSet?> DraftsProperty =
        AvaloniaProperty.Register<EffectScriptParameterEditor, EffectScriptParameterDraftSet?>(nameof(Drafts));
    private readonly StackPanel rows = new() { Spacing = 4 };
    private int revision;

    /// <summary>创建支持字段恢复、严格选项及建议值的参数编辑区。</summary>
    public EffectScriptParameterEditor()
    {
        Content = rows;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(LostFocusEvent, (_, _) => QueueCommit(), RoutingStrategies.Bubble);
        AddHandler(NumericDragLabel.DragCompletedEvent, (_, args) =>
        {
            revision++;
            if (args.Changed)
            {
                CommitRequested?.Invoke(this, EventArgs.Empty);
            }
            args.Handled = true;
        });
        AddHandler(NumericDragLabel.DragStartedEvent, (_, args) =>
        {
            revision++;
            args.Handled = true;
        });
    }

    public event EventHandler? CommitRequested;
    public EffectScriptParameterDraftSet? Drafts
    {
        get => GetValue(DraftsProperty);
        set => SetValue(DraftsProperty, value);
    }

    /// <summary>定位保留原文的首个非法字段。</summary>
    public bool FocusInvalidField()
    {
        var field = Drafts?.InvalidFieldKey;
        var row = rows.Children.OfType<StackPanel>().FirstOrDefault(control =>
            control.DataContext is EffectScriptParameterDraft draft && draft.InvalidFieldKey == field);
        var number = row?.GetVisualDescendants().OfType<NumericDraftInput>().FirstOrDefault(input =>
            input.Name == "EffectParameter_" + field);
        if (number is not null)
        {
            return number.FocusInput();
        }
        var color = row?.GetVisualDescendants().OfType<ColorDraftInput>().FirstOrDefault();
        if (color is not null)
        {
            return color.TryFocusInvalidField();
        }
        var input = row?.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Focusable);
        input?.BringIntoView();
        return input?.Focus() == true;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DraftsProperty)
        {
            revision++;
            ObserveColors(change.OldValue as EffectScriptParameterDraftSet, false);
            rows.Children.Clear();
            foreach (var draft in Drafts?.Rows ?? [])
            {
                rows.Children.Add(CreateRow(draft));
            }
            if (this.IsAttachedToVisualTree())
            {
                ObserveColors(Drafts, true);
            }
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ObserveColors(Drafts, true);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        revision++;
        ObserveColors(Drafts, false);
        base.OnDetachedFromVisualTree(e);
    }

    private void ObserveColors(EffectScriptParameterDraftSet? drafts, bool subscribe)
    {
        foreach (var draft in drafts?.Rows ?? [])
        {
            draft.Color.Committed -= OnColorCommitted;
            if (subscribe && !draft.IsStrictChoice && draft.Definition.Kind == EffectScriptParameterKind.COLOR)
            {
                draft.Color.Committed += OnColorCommitted;
            }
        }
    }

    private void OnColorCommitted(object? sender, ColorDraftCommittedEventArgs args) => CommitRequested?.Invoke(this, EventArgs.Empty);

    private StackPanel CreateRow(EffectScriptParameterDraft draft)
    {
        var body = new StackPanel { DataContext = draft, Spacing = 2 };
        var row = new PropertyRowControl { Header = draft.Label };
        if (draft.IsStrictChoice)
        {
            row.Content = CreateChoices(draft);
        }
        else
        {
            var input = CreateInput(draft);
            if (draft.Choices.Count == 0)
            {
                row.Content = input;
            }
            else
            {
                var values = new StackPanel { Spacing = 4 };
                values.Children.Add(CreateChoices(draft));
                values.Children.Add(input);
                row.Content = values;
            }
            if (input is NumericDraftInput scalar)
            {
                row.Header = new NumericDragLabel { Text = draft.Label, Input = scalar, Classes = { "field" } };
            }
            row.IsVector = input is VectorDraftInput or ColorDraftInput;
        }
        body.Children.Add(row);
        var error = new TextBlock { Classes = { "field" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        error.Bind(TextBlock.TextProperty, new Binding(nameof(EffectScriptParameterDraft.Error)));
        error.Bind(IsVisibleProperty, new Binding(nameof(EffectScriptParameterDraft.Error))
        {
            Converter = Avalonia.Data.Converters.StringConverters.IsNotNullOrEmpty
        });
        error.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("PreviewError"));
        body.Children.Add(error);
        return body;
    }

    private ComboBox CreateChoices(EffectScriptParameterDraft draft)
    {
        var choices = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = draft.Choices,
            ItemTemplate = new FuncDataTemplate<EffectScriptParameterChoice>((value, _) => new TextBlock { Text = value?.Label })
        };
        choices.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(EffectScriptParameterDraft.SelectedChoice)) { Mode = BindingMode.OneWay });
        choices.SelectionChanged += (_, _) =>
        {
            if (choices.SelectedItem is EffectScriptParameterChoice choice && !Equals(choice, draft.SelectedChoice))
            {
                draft.Choose(choice);
                CommitRequested?.Invoke(this, EventArgs.Empty);
            }
        };
        return choices;
    }

    private Control CreateInput(EffectScriptParameterDraft draft)
    {
        if (draft.Definition.Kind is EffectScriptParameterKind.VECTOR2 or EffectScriptParameterKind.VECTOR3)
        {
            return new VectorDraftInput
            {
                HasZ = draft.HasZ,
                PreserveDoublePrecision = true,
                Minimum = decimal.MinValue,
                Maximum = decimal.MaxValue,
                XFieldKey = "EffectParameter_" + draft.Name + "_x",
                YFieldKey = "EffectParameter_" + draft.Name + "_y",
                ZFieldKey = "EffectParameter_" + draft.Name + "_z",
                XBinding = new Binding("X.Value") { Mode = BindingMode.TwoWay },
                YBinding = new Binding("Y.Value") { Mode = BindingMode.TwoWay },
                ZBinding = new Binding("Z.Value") { Mode = BindingMode.TwoWay },
                XTextBinding = new Binding("X.RawText") { Mode = BindingMode.TwoWay },
                YTextBinding = new Binding("Y.RawText") { Mode = BindingMode.TwoWay },
                ZTextBinding = new Binding("Z.RawText") { Mode = BindingMode.TwoWay }
            };
        }
        if (draft.Definition.Kind == EffectScriptParameterKind.COLOR)
        {
            return new ColorDraftInput { Draft = draft.Color, CommitOnLostFocus = false };
        }
        if (draft.Definition.Kind == EffectScriptParameterKind.BOOLEAN)
        {
            var input = new CheckBox();
            input.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(EffectScriptParameterDraft.BooleanValue)) { Mode = BindingMode.OneWay });
            input.IsCheckedChanged += (_, _) =>
            {
                if (input.IsChecked == true != draft.BooleanValue)
                {
                    draft.RawText = input.IsChecked == true ? "true" : "false";
                    CommitRequested?.Invoke(this, EventArgs.Empty);
                }
            };
            return input;
        }
        if (draft.Definition.Kind == EffectScriptParameterKind.NUMBER)
        {
            var input = new NumericDraftInput { Name = "EffectParameter_" + draft.Name, Minimum = decimal.MinValue, Maximum = decimal.MaxValue };
            input.Bind(NumericDraftInput.RawTextProperty, new Binding(nameof(EffectScriptParameterDraft.RawText)) { Mode = BindingMode.TwoWay });
            return input;
        }
        var text = new TextBox { Name = "EffectParameter_" + draft.Name, MinWidth = 0 };
        text.Bind(TextBox.TextProperty, new Binding(nameof(EffectScriptParameterDraft.RawText)) { Mode = BindingMode.TwoWay });
        return text;
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Source is Control focused && focused.GetSelfAndVisualAncestors().OfType<ComboBox>().Any() ||
            rows.GetVisualDescendants().OfType<ComboBox>().Any(choice => choice.IsDropDownOpen))
        {
            return;
        }
        if (args.Key == Key.Escape && args.Source is Control source &&
            source.GetSelfAndVisualAncestors().OfType<Control>().Select(control => control.DataContext)
                .OfType<EffectScriptParameterDraft>().FirstOrDefault() is { } draft)
        {
            revision++;
            if (source.GetSelfAndVisualAncestors().OfType<ColorDraftInput>().Any())
            {
                return;
            }
            var vector = source.GetSelfAndVisualAncestors().OfType<VectorDraftInput>().FirstOrDefault();
            var input = source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>().FirstOrDefault();
            var key = input is null ? null : vector?.GetInputField(input);
            if (key is not null)
            {
                draft.RestoreComponent(key.EndsWith("_x", StringComparison.Ordinal) ? 0 : key.EndsWith("_y", StringComparison.Ordinal) ? 1 : 2);
            }
            else
            {
                draft.Restore();
            }
            args.Handled = true;
        }
        else if (args.Key == Key.Enter)
        {
            revision++;
            CommitRequested?.Invoke(this, EventArgs.Empty);
            args.Handled = true;
        }
    }

    private void QueueCommit()
    {
        var expected = Drafts;
        var version = ++revision;
        Dispatcher.UIThread.Post(() =>
        {
            if (version == revision && ReferenceEquals(expected, Drafts) && this.IsAttachedToVisualTree() &&
                !this.GetVisualDescendants().OfType<NumericDraftInput>().Any(input => input.IsTitleDragging))
            {
                CommitRequested?.Invoke(this, EventArgs.Empty);
            }
        }, DispatcherPriority.Background);
    }
}
