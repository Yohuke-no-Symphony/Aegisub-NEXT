using System.Globalization;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed partial class EffectsPanelViewModel
{
    private decimal? keyframeValueZ;
    private string keyframeValueZText = "0";
    private int keyframeCurveComponentMask;

    public decimal? KeyframeValueZ { get => keyframeValueZ; set => SetProperty(ref keyframeValueZ, value); }
    public string KeyframeValueZText { get => keyframeValueZText; set => SetProperty(ref keyframeValueZText, value); }
    public bool HasVectorZ => AnimationPropertyMetadata.GetValueKind(Property) == AnimationValueKind.VECTOR3;
    public int KeyframeComponentCount => AnimationPropertyMetadata.GetValueKind(Property) switch
    {
        AnimationValueKind.VECTOR => 2,
        AnimationValueKind.VECTOR3 => 3,
        AnimationValueKind.COLOR => 4,
        _ => 1
    };
    public bool HasKeyframeCurveComponents => KeyframeComponentCount > 1;
    public EffectComponentChoice[] KeyframeCurveComponents =>
    [
        new(0, AegiNext.Desktop.I18n.Localization.Get("Workbench.AnimationAllComponents")),
        .. Enumerable.Range(0, KeyframeComponentCount).Select(component =>
            new EffectComponentChoice(1 << component, AnimationPropertyMetadata.GetComponentName(Property, component)))
    ];
    public EffectComponentChoice? SelectedKeyframeCurveComponent
    {
        get => KeyframeCurveComponents.FirstOrDefault(choice => choice.Mask == keyframeCurveComponentMask);
        set
        {
            if (refreshingChoices || value is null || value.Mask == keyframeCurveComponentMask)
            {
                return;
            }
            if (!session.CanChangeKeyframeCurveComponent && !session.TryCommitDrafts())
            {
                OnPropertyChanged();
                return;
            }
            keyframeCurveComponentMask = value.Mask;
            OnPropertyChanged();
            session.RefreshKeyframeCurveInspector();
        }
    }

    internal int KeyframeCurveComponentMask => keyframeCurveComponentMask;

    internal void RefreshKeyframeCurveChoices()
    {
        if ((keyframeCurveComponentMask & ~((1 << KeyframeComponentCount) - 1)) != 0)
        {
            keyframeCurveComponentMask = 0;
        }
        OnPropertyChanged(nameof(HasVectorZ));
        OnPropertyChanged(nameof(HasKeyframeCurveComponents));
        OnPropertyChanged(nameof(KeyframeCurveComponents));
        OnPropertyChanged(nameof(SelectedKeyframeCurveComponent));
    }

    internal void LoadKeyframeNumbers(AnimationValue value)
    {
        var x = value.GetComponent(0).ToString("R", CultureInfo.CurrentCulture);
        var y = value.ComponentCount > 1 ? value.GetComponent(1).ToString("R", CultureInfo.CurrentCulture) : "0";
        var z = value.IsVector3 ? value.GetComponent(2).ToString("R", CultureInfo.CurrentCulture) : "0";
        if (Projection(x) is { } projectedX)
        {
            KeyframeValue = projectedX;
        }
        if (Projection(y) is { } projectedY)
        {
            KeyframeValueY = projectedY;
        }
        if (Projection(z) is { } projectedZ)
        {
            KeyframeValueZ = projectedZ;
        }
        KeyframeValueText = x;
        KeyframeValueYText = y;
        KeyframeValueZText = z;
    }

    internal void LoadKeyframeCurve(Keyframe frame)
    {
        var component = keyframeCurveComponentMask == 0 ? 0 : System.Numerics.BitOperations.TrailingZeroCount((uint)keyframeCurveComponentMask);
        var curve = frame.GetCurve(component < frame.Value.ComponentCount ? component : 0);
        Interpolation = (int)curve.Interpolation;
        LoadPowerExponent(curve.Exponent);
    }

    private static decimal? Projection(string text)
    {
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
            (value != 0 || double.Parse(text, CultureInfo.CurrentCulture) == 0) ? value : null;
    }
}
