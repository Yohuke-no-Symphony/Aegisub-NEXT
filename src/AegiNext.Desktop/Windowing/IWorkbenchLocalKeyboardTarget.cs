using Avalonia.Input;

namespace AegiNext.Desktop.Windowing;

internal interface IWorkbenchLocalKeyboardTarget
{
    /// <summary>声明当前焦点应交由控件处理的本地按键。</summary>
    bool OwnsLocalKey(Key key, KeyModifiers modifiers, IInputElement focusedElement);
}
