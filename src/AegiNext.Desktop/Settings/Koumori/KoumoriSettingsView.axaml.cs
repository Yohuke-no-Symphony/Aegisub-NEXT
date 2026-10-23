using AegiNext.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Settings.Koumori;

/// <summary>呈现自动打轴设置，连接字段确认、恢复和输入法组合保护。</summary>
public sealed partial class KoumoriSettingsView : UserControl
{
    /// <summary>加载吸附距离输入。</summary>
    public KoumoriSettingsView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        var input = this.FindControl<NumericDraftInput>("MaximumSnapMillisecondsInput")!;
        var title = this.FindControl<NumericDragLabel>("MaximumSnapMillisecondsInputTitle")!;
        var focusRevision = 0L;
        KoumoriSettingsViewModel? dragModel = null;
        title.DragStarted += (_, _) =>
        {
            focusRevision++;
            dragModel = DataContext as KoumoriSettingsViewModel;
        };
        title.DragCompleted += (_, args) =>
        {
            focusRevision++;
            var frozenModel = dragModel;
            dragModel = null;
            if (args.Changed && IsEffectivelyVisible && ReferenceEquals(DataContext, frozenModel))
            {
                frozenModel?.CommitMaximumSnapMilliseconds();
            }
        };
        title.DragCanceled += (_, _) =>
        {
            focusRevision++;
            dragModel = null;
        };
        DataContextChanged += (_, _) =>
        {
            focusRevision++;
            title.CancelDrag();
        };
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty && !IsVisible)
            {
                title.CancelDrag();
            }
        };
        input.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (DataContext is KoumoriSettingsViewModel model && args.Key is Key.Enter or Key.Escape &&
                !HasComposition(input))
            {
                focusRevision++;
                if (args.Key == Key.Escape)
                {
                    model.RestoreMaximumSnapMilliseconds();
                }
                else
                {
                    model.CommitMaximumSnapMilliseconds();
                }

                args.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        input.LostFocus += (_, _) =>
        {
            var revision = ++focusRevision;
            var currentModel = DataContext as KoumoriSettingsViewModel;
            Dispatcher.UIThread.Post(() =>
            {
                if (revision == focusRevision && !input.IsTitleDragging && !input.IsKeyboardFocusWithin && IsEffectivelyVisible &&
                    currentModel is not null && ReferenceEquals(DataContext, currentModel) && !HasComposition(input))
                {
                    currentModel.CommitMaximumSnapMilliseconds();
                }
            }, DispatcherPriority.Background);
        };
    }

    private static bool HasComposition(Control input) => input.GetVisualDescendants().OfType<TextPresenter>()
        .Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
}
