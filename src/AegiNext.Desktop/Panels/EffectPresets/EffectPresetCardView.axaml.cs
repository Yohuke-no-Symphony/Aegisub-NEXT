using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Rendering;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Panels.EffectPresets;

internal sealed partial class EffectPresetCardView : UserControl, IDisposable
{
    private readonly VideoFramePresenter presenter;

    internal EffectPresetCardView(EffectPresetCardViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        presenter = this.FindControl<VideoFramePresenter>("PreviewPresenter")!;
    }

    internal EffectPresetCardViewModel ViewModel { get; }
    internal EffectPresetPreviewRequest? Request { get; set; }
    internal MediaTime? PresentedPosition { get; private set; }
    internal bool IsPreviewAnimating { get; private set; }

    internal void Present(EffectPresetPreviewResult result)
    {
        ViewModel.SetPreviewError(result.Error);
        if (result.Frame is { } frame)
        {
            presenter.Present(frame);
        }
        else
        {
            presenter.Clear();
        }
        PresentedPosition = result.Position;
        IsPreviewAnimating = result.IsAnimated;
    }

    internal void Clear()
    {
        presenter.Clear();
        PresentedPosition = null;
        IsPreviewAnimating = false;
        Request = null;
    }

    /// <inheritdoc />
    public void Dispose() => presenter.Dispose();
}
