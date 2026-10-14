using System.Globalization;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed partial class EffectsPanelViewModel
{
    public NumericValueDraft PositionXDraft { get; } = new() { PreserveDoublePrecision = true };
    public NumericValueDraft PositionYDraft { get; } = new() { PreserveDoublePrecision = true };
    public NumericValueDraft ScaleXDraft { get; } = new() { PreserveDoublePrecision = true };
    public NumericValueDraft ScaleYDraft { get; } = new() { PreserveDoublePrecision = true };
    public NumericValueDraft OpacityDraft { get; } = new() { PreserveDoublePrecision = true };
    public NumericValueDraft BlurDraft { get; } = new() { PreserveDoublePrecision = true };

    private void InitializeNumericDrafts()
    {
        BindNumericDraft(PositionXDraft, nameof(PositionX), nameof(PositionXText), 0);
        BindNumericDraft(PositionYDraft, nameof(PositionY), nameof(PositionYText), 0);
        BindNumericDraft(ScaleXDraft, nameof(ScaleX), nameof(ScaleXText), 1);
        BindNumericDraft(ScaleYDraft, nameof(ScaleY), nameof(ScaleYText), 1);
        BindNumericDraft(OpacityDraft, nameof(Opacity), nameof(OpacityText), 1);
        BindNumericDraft(BlurDraft, nameof(Blur), nameof(BlurText), 0);
    }

    private void BindNumericDraft(NumericValueDraft draft, string valueProperty, string textProperty, double initialValue)
    {
        draft.Load(initialValue);
        draft.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NumericValueDraft.RawText))
            {
                OnPropertyChanged(textProperty);
            }
            else if (e.PropertyName == nameof(NumericValueDraft.Value))
            {
                OnPropertyChanged(valueProperty);
            }
        };
    }

    private static void SetNumericDraftValue(NumericValueDraft draft, decimal? value)
    {
        draft.RawText = value?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
    }
}
