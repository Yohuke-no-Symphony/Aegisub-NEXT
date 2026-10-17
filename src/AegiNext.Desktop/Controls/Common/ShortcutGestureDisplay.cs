using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace AegiNext.Desktop.Controls.Common;

/// <summary>以独立按键 badge 呈现快捷键；只转换显示，不修改绑定或录制输入。</summary>
public sealed class ShortcutGestureDisplay : UserControl
{
    public static readonly StyledProperty<string> GestureProperty =
        AvaloniaProperty.Register<ShortcutGestureDisplay, string>(nameof(Gesture), string.Empty);

    private readonly WrapPanel keys = new() { Orientation = Orientation.Horizontal };

    /// <summary>创建沿用共享主题与字体的按键显示。</summary>
    public ShortcutGestureDisplay()
    {
        Content = keys;
        VerticalContentAlignment = VerticalAlignment.Center;
        ClipToBounds = true;
    }

    public string Gesture
    {
        get => GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GestureProperty)
        {
            UpdateKeys();
        }
    }

    private void UpdateKeys()
    {
        IReadOnlyList<string> labels;
        try
        {
            labels = ShortcutGestureFormatter.GetKeyLabels(Gesture);
        }
        catch (InvalidDataException)
        {
            labels = [Gesture];
        }

        keys.Children.Clear();
        foreach (var label in labels)
        {
            keys.Children.Add(new Border
            {
                Classes = { "shortcut-key" },
                Child = new TextBlock { Text = label }
            });
        }

        AutomationProperties.SetName(this, string.Join('+', labels));
    }
}
