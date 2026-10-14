using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.EffectLibrary.Transfer;

/// <summary>呈现效果库分享格式与逐行导入选择，保留宿主业务上下文。</summary>
public sealed partial class EffectLibraryTransferView : UserControl
{
    /// <summary>隔离初始化继承上下文，再加载编译绑定页面。</summary>
    public EffectLibraryTransferView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
    }
}
