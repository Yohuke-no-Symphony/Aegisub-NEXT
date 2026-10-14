using System.Windows.Input;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Menus;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Material.Icons;
using Material.Icons.Avalonia;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证效果库菜单根的语义文案、图标和实时菜单生命周期。</summary>
public sealed class EffectLibraryMenuUiTests
{
    /// <summary>完整和折叠菜单的实际根显示三语预期文案，保留图标、子项和工程状态。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WindowLibraryBranchUsesCanonicalLabelsAndIconInBothMenuLayouts(bool overflow)
    {
        await using var context = new MainWindowTestContext();
        var catalog = new WorkbenchMenuCatalog(context.Window.GetCommand);
        catalog.Update(context.Session.Preferences);
        using var bar = new WindowMenuBar(catalog);
        var window = new Window { Width = 1000, Height = 400, Content = bar };
        MenuItem? library = null;
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            bar.SetAvailableWidth(overflow ? 1 : double.PositiveInfinity);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            library = FindWindowLibrary(bar, context.Window.GetCommand(WorkbenchCommand.OPEN_STYLE_LIBRARY), overflow);
            var children = library.Items.OfType<MenuItem>().ToArray();
            var icon = Assert.IsType<MaterialIcon>(library.Icon);
            var snapshot = context.Session.Editor.Snapshot;
            Assert.Equal(MaterialIconKind.ViewGridOutline, icon.Kind);
            Assert.True(icon.Drawing.Geometry!.Bounds.Width > 0 && icon.Drawing.Geometry.Bounds.Height > 0);
            library.IsSubMenuOpen = true;
            foreach (var language in new[] { "en-US", "zh-CN", "ja-JP", "en-US" })
            {
                Localization.SetLanguage(language);
                catalog.RefreshLanguage();
                Dispatcher.UIThread.RunJobs();
                bar.SetAvailableWidth(overflow ? 1 : double.PositiveInfinity);
                var current = FindWindowLibrary(bar, context.Window.GetCommand(WorkbenchCommand.OPEN_STYLE_LIBRARY), overflow);
                Assert.Same(library, current);
                Assert.Equal(ExpectedHeader(language), current.Header);
                Assert.Same(icon, current.Icon);
                Assert.Equal(MaterialIconKind.ViewGridOutline, icon.Kind);
                Assert.True(current.IsSubMenuOpen);
                Assert.Equal(children, current.Items.OfType<MenuItem>());
                Assert.Equal(ExpectedChildren(language), children.Select(child => Assert.IsType<string>(child.Header)));
                Assert.Equal(new[] { WorkbenchCommand.OPEN_STYLE_LIBRARY, WorkbenchCommand.OPEN_EFFECT_SCRIPT_LIBRARY,
                    WorkbenchCommand.OPEN_EFFECT_LIBRARY_TRANSFER }.Select(context.Window.GetCommand), children.Select(child => child.Command));
                Assert.Same(snapshot, context.Session.Editor.Snapshot);
                Assert.False(context.Session.Editor.CanUndo);
            }
        }
        finally
        {
            if (library is not null)
            {
                library.IsSubMenuOpen = false;
            }
            window.Close();
        }
    }

    /// <summary>原生根语言替换保留同一图标缓存和子命令；macOS 应用菜单复用该图标。</summary>
    [AvaloniaFact]
    public async Task NativeLibraryBranchKeepsItsIconAndCommandsAcrossAllThreeLanguages()
    {
        await using var context = new MainWindowTestContext();
        context.Session.UpdatePreferences(context.Session.Preferences with { WindowMenuOnMac = false });
        var native = Assert.IsType<NativeMenu>(NativeMenu.GetMenu(context.Window));
        var style = context.Window.GetCommand(WorkbenchCommand.OPEN_STYLE_LIBRARY);
        var first = FindNativeLibrary(native, style);
        var icon = Assert.IsType<Bitmap>(first.Icon);
        Assert.Equal(new Avalonia.PixelSize(24, 24), icon.PixelSize);
        var children = first.Menu!.Items.OfType<NativeMenuItem>().ToArray();
        var snapshot = context.Session.Editor.Snapshot;
        foreach (var language in new[] { "en-US", "zh-CN", "ja-JP", "en-US" })
        {
            Localization.SetLanguage(language);
            Dispatcher.UIThread.RunJobs();
            var current = FindNativeLibrary(native, style);
            Assert.Equal(ExpectedHeader(language), current.Header);
            Assert.Same(icon, current.Icon);
            Assert.Equal(children, current.Menu!.Items.OfType<NativeMenuItem>());
            Assert.Equal(ExpectedChildren(language), children.Select(child => child.Header));
            Assert.Equal(new[] { WorkbenchCommand.OPEN_STYLE_LIBRARY, WorkbenchCommand.OPEN_EFFECT_SCRIPT_LIBRARY,
                WorkbenchCommand.OPEN_EFFECT_LIBRARY_TRANSFER }.Select(context.Window.GetCommand), children.Select(child => child.Command));
            Assert.Same(snapshot, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            if (OperatingSystem.IsMacOS())
            {
                var applicationMenu = Assert.IsType<NativeMenu>(NativeMenu.GetMenu(Avalonia.Application.Current!));
                var applicationLibrary = Assert.Single(applicationMenu.Items.OfType<NativeMenuItem>(),
                    item => item.Menu?.Items.OfType<NativeMenuItem>().Any(child => ReferenceEquals(child.Command, style)) == true);
                Assert.Equal(ExpectedHeader(language), applicationLibrary.Header);
                Assert.Same(icon, applicationLibrary.Icon);
            }
        }
    }

    /// <summary>多个注册窗口借用同一效果库图标缓存，关闭附属窗口不会使主窗口图标失效。</summary>
    [AvaloniaFact]
    public async Task RegisteredWindowsShareTheLibraryIconWithoutChangingItsLifetimeOnAuxiliaryClose()
    {
        await using var context = new MainWindowTestContext();
        var main = Assert.IsType<NativeMenu>(NativeMenu.GetMenu(context.Window));
        var style = context.Window.GetCommand(WorkbenchCommand.OPEN_STYLE_LIBRARY);
        var icon = Assert.IsType<Bitmap>(FindNativeLibrary(main, style).Icon);
        context.Window.GetCommand(WorkbenchCommand.OPEN_SETTINGS).Execute(null);
        var settings = Assert.Single(context.Window.OwnedWindows.OfType<SettingsWindow>());
        try
        {
            var auxiliary = Assert.IsType<NativeMenu>(NativeMenu.GetMenu(settings));
            Assert.Same(icon, FindNativeLibrary(auxiliary, style).Icon);
            settings.Close();
            Assert.False(settings.IsVisible);
            Assert.Same(icon, FindNativeLibrary(main, style).Icon);
            using var stream = new MemoryStream();
            icon.Save(stream, PngBitmapEncoderOptions.Default);
            Assert.True(stream.Length > 0);
        }
        finally
        {
            settings.Close();
        }
    }

    private static MenuItem FindWindowLibrary(WindowMenuBar bar, ICommand style, bool overflow)
    {
        var menu = Assert.IsType<Menu>(bar.Content);
        IEnumerable<MenuItem> roots;
        if (overflow)
        {
            var compactRoot = Assert.Single(menu.Items.OfType<MenuItem>());
            Assert.Equal("☰", compactRoot.Header);
            Assert.Equal(WorkbenchMenuCatalog.Groups.Length, compactRoot.Items.Count);
            roots = compactRoot.Items.OfType<MenuItem>();
        }
        else
        {
            Assert.Equal("MainMenu", menu.Name);
            roots = menu.Items.OfType<MenuItem>();
        }
        return Assert.Single(roots.SelectMany(item => item.Items.OfType<MenuItem>()),
            item => item.Items.OfType<MenuItem>().Any(child => ReferenceEquals(child.Command, style)));
    }

    private static NativeMenuItem FindNativeLibrary(NativeMenu menu, ICommand style)
    {
        return Assert.Single(menu.Items.OfType<NativeMenuItem>().SelectMany(item => item.Menu!.Items.OfType<NativeMenuItem>()),
            item => item.Menu?.Items.OfType<NativeMenuItem>().Any(child => ReferenceEquals(child.Command, style)) == true);
    }

    private static string ExpectedHeader(string language)
    {
        return language switch
        {
            "zh-CN" => "效果库",
            "ja-JP" => "エフェクトライブラリ",
            _ => "Effect Library"
        };
    }

    private static string[] ExpectedChildren(string language)
    {
        return language switch
        {
            "zh-CN" => ["样式库", "特效脚本", "导入与导出"],
            "ja-JP" => ["スタイルライブラリ", "エフェクトスクリプト", "インポートとエクスポート"],
            _ => ["Style Library", "Effect Scripts", "Import & Export"]
        };
    }
}
