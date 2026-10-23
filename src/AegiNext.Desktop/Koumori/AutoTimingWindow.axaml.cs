using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Windowing;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Koumori;

/// <summary>生成、试听和批量应用字幕时间段的独立窗口。</summary>
public sealed partial class AutoTimingWindow : Window, IWindowTitleBarHost
{
    private readonly WindowTitleBar titleBar;
    private readonly AutoTimingViewModel? viewModel;
    private IWindowChrome? chrome;

    /// <summary>加载自动打轴窗口布局。</summary>
    public AutoTimingWindow()
    {
        AvaloniaXamlLoader.Load(this);
        titleBar = this.FindControl<WindowTitleBar>("AutoTimingTitleBar")!;
        chrome = WindowChrome.Attach(this, titleBar);
        Closed += OnClosed;
    }

    internal AutoTimingWindow(WorkbenchSession session) : this()
    {
        viewModel = new(session);
        DataContext = viewModel;
    }

    WindowTitleBar IWindowTitleBarHost.TitleBar => titleBar;

    void IWindowTitleBarHost.ReleaseStandaloneChrome() => ReleaseChrome();

    private void ReleaseChrome()
    {
        chrome?.Dispose();
        chrome = null;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        ReleaseChrome();
        viewModel?.Dispose();
    }
}
