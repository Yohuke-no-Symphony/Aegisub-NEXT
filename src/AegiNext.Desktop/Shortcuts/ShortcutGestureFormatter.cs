namespace AegiNext.Desktop.Shortcuts;

/// <summary>将可持久化快捷键转换为当前平台的可读按键名称。</summary>
public static class ShortcutGestureFormatter
{
    /// <summary>按组合键顺序返回各按键名称；禁用的快捷键返回空列表。</summary>
    public static IReadOnlyList<string> GetKeyLabels(string gesture, bool? isMacOs = null)
    {
        var mac = isMacOs ?? OperatingSystem.IsMacOS();
        var normalized = ShortcutConfiguration.NormalizeGesture(gesture, mac);
        if (normalized.Length == 0)
        {
            return [];
        }

        return normalized.Split('+').Select(part => part switch
        {
            "CmdOrCtrl" => mac ? "Cmd" : "Ctrl",
            "Control" => "Ctrl",
            "Meta" => mac ? "Cmd" : "Win",
            "Back" => "Backspace",
            "Escape" => "Esc",
            "OemComma" => ",",
            "OemPeriod" => ".",
            "OemPlus" => "+",
            "OemMinus" => "-",
            "OemQuestion" => "/",
            "OemSemicolon" => ";",
            "OemQuotes" => "'",
            "OemOpenBrackets" => "[",
            "OemCloseBrackets" => "]",
            "OemPipe" => "\\",
            "OemTilde" => "`",
            _ when part.Length == 2 && part[0] == 'D' && char.IsAsciiDigit(part[1]) => part[1].ToString(),
            _ => part
        }).ToArray();
    }

    /// <summary>为菜单、错误提示和辅助功能生成与按键 badge 一致的组合键文本。</summary>
    public static string Format(string gesture, bool? isMacOs = null)
    {
        return string.Join('+', GetKeyLabels(gesture, isMacOs));
    }
}
