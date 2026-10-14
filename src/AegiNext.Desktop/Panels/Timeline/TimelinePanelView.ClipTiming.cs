using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.Timeline;

internal sealed partial class TimelinePanelView
{
    private TextBox clipStartInput = null!;
    private TextBox clipEndInput = null!;
    private int clipTimingFocusRevision;

    private void BindClipTimingInputs()
    {
        clipStartInput = this.FindControl<TextBox>("TimelineClipStartInput")!;
        clipEndInput = this.FindControl<TextBox>("TimelineClipEndInput")!;
        foreach (var input in new[] { clipStartInput, clipEndInput })
        {
            input.GotFocus += OnClipTimingGotFocus;
            input.LostFocus += OnClipTimingLostFocus;
            input.AddHandler(KeyDownEvent, OnClipTimingKeyDown, RoutingStrategies.Tunnel);
        }
        RefreshClipTimingErrors();
    }

    private void OnClipTimingGotFocus(object? sender, FocusChangedEventArgs e) => clipTimingFocusRevision++;

    private void OnClipTimingKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox input)
        {
            return;
        }
        if (e.Key == Key.Escape)
        {
            clipTimingFocusRevision++;
            viewModel.RestoreClipTimingField(input.Name!);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            clipTimingFocusRevision++;
            viewModel.CommitClipTimingDrafts();
            e.Handled = true;
        }
    }

    private void OnClipTimingLostFocus(object? sender, RoutedEventArgs e)
    {
        var revision = clipTimingFocusRevision;
        var root = TopLevel.GetTopLevel(this);
        var document = session.DocumentSnapshot;
        var layerId = session.SelectedLayerId;
        Dispatcher.UIThread.Post(() =>
        {
            if (!disposed && revision == clipTimingFocusRevision && layerId == session.SelectedLayerId &&
                ReferenceEquals(document, session.DocumentSnapshot) && root is not null &&
                ReferenceEquals(root, TopLevel.GetTopLevel(this)) && this.IsAttachedToVisualTree() &&
                !clipStartInput.IsFocused && !clipEndInput.IsFocused)
            {
                viewModel.CommitClipTimingDrafts(false);
            }
        }, DispatcherPriority.Background);
    }

    private void RefreshClipTimingErrors()
    {
        foreach (var input in new[] { clipStartInput, clipEndInput })
        {
            DataValidationErrors.SetErrors(input,
                viewModel.InvalidClipTimingField == input.Name && viewModel.ClipTimingValidationError is { } error ? new[] { error } : null);
        }
    }

    private void UnbindClipTimingInputs()
    {
        clipTimingFocusRevision++;
        foreach (var input in new[] { clipStartInput, clipEndInput })
        {
            input.GotFocus -= OnClipTimingGotFocus;
            input.LostFocus -= OnClipTimingLostFocus;
            input.RemoveHandler(KeyDownEvent, OnClipTimingKeyDown);
        }
    }
}
