using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Effects;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证特效通用属性行的稳定输入入口、共享标题和完整属性可发现性。</summary>
public sealed class EffectsCommonRowsUiTests
{
    /// <summary>主变换、合成与混合模式复用同一行外壳，宽窄及主题不改变输入名和字段步长。</summary>
    [AvaloniaTheory]
    [InlineData(260, "zh-CN", true)]
    [InlineData(650, "en-US", false)]
    public async Task CommonRowsShareTheirShellAndFieldTypographyAndPreserveInputNamesAndSteps(int width, string language, bool dark)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = language });
        var effects = context.ViewModel.Effects;
        effects.TransformExpanded = true;
        effects.CompositeExpanded = true;
        using var view = new EffectsPanelView(effects, context.Session);
        var host = new Window { Width = width, Height = 1000, Content = view, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        host.Show();
        try
        {
            Flush(host);
            var source = context.Session.DocumentSnapshot;
            var blend = UiTestActions.Find<ComboBox>(view, "BlendCombo");
            var blendShell = Assert.Single(blend.GetVisualAncestors().OfType<PropertyRowControl>());
            var blendTitle = Assert.Single(blendShell.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && text.Text == Localization.Get("Workbench.Blend"));
            foreach (var (name, property, inputName, increment) in new[]
            {
                ("PositionRow", AnimationProperty.POSITION, "PositionInput", 1m),
                ("ScaleRow", AnimationProperty.SCALE, "ScaleInput", 0.01m),
                ("OpacityRow", AnimationProperty.OPACITY, "OpacityInput", 0.05m),
                ("BlurRow", AnimationProperty.BLUR, "BlurInput", 0.5m)
            })
            {
                var row = UiTestActions.Find<AnimationPropertyRowControl>(view, name);
                row.BringIntoView();
                Flush(host);
                var shell = Assert.Single(row.GetVisualDescendants().OfType<PropertyRowControl>());
                var title = Assert.Single(shell.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.IsEffectivelyVisible && text.Text == row.Label);
                Assert.Equal(11, title.FontSize);
                Assert.DoesNotContain(title.GetVisualAncestors(), ancestor => ancestor is Button);
                Assert.Equal(blendTitle.FontSize, title.FontSize);
                Assert.Equal(blendTitle.Foreground?.ToString(), title.Foreground?.ToString());
                Assert.Equal(increment, row.Increment);
                Assert.True(row.ShowAnimationActions);
                var actions = row.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.IsEffectivelyVisible && button.Classes.Contains("animation-property-action")).ToArray();
                Assert.Equal(3, actions.Length);
                Assert.All(actions, button => Assert.NotNull(button.Command));
                Assert.Equal(property, row.CommandParameter);
                Assert.InRange(row.Bounds.Width, 1, view.Bounds.Width);
                if (row.IsVector)
                {
                    var vector = UiTestActions.Find<VectorDraftInput>(row, inputName);
                    var prefix = property == AnimationProperty.POSITION ? "Position" : "Scale";
                    Assert.Equal(prefix + "XInput", vector.XInputName);
                    Assert.Equal(prefix + "YInput", vector.YInputName);
                    Assert.Same(UiTestActions.Find<NumericDraftInput>(vector, prefix + "XInput"), vector.FindFieldInput(row.XFieldKey));
                    Assert.True(vector.Bounds.Width > 0);
                    Assert.True(vector.Bounds.Width <= row.Bounds.Width);
                }
                else
                {
                    Assert.Equal(inputName, UiTestActions.Find<NumericDraftInput>(row, inputName).Name);
                }
            }
            Assert.Same(source, context.Session.DocumentSnapshot);
        }
        finally
        {
            host.Close();
        }
    }

    /// <summary>正式属性清单在矩形和所选矢量节点场景的编辑行并集中完整出现，旧分量不会重新暴露。</summary>
    [AvaloniaFact]
    public async Task RectangleAndSelectedVectorNodeRowsExposeExactlyAllCurrentAnimationProperties()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var effects = context.ViewModel.Effects;
        effects.TransformExpanded = true;
        effects.TypographyExpanded = true;
        effects.FillExpanded = true;
        effects.StrokeExpanded = true;
        effects.ShadowExpanded = true;
        effects.CompositeExpanded = true;
        effects.MaskExpanded = true;
        effects.PathExpanded = true;
        var node = new MaskNode { Position = new(10, 20) };
        var exposed = new HashSet<AnimationProperty>();
        using var view = new EffectsPanelView(effects, session);
        var host = new Window { Width = 650, Height = 1200, Content = view };
        host.Show();
        try
        {
            foreach (var mask in new ClipMask[]
            {
                new RectangleClipMask { BottomRight = new(400, 300) },
                new VectorClipMask { Contours = [new() { Nodes = [node] }] }
            })
            {
                session.Editor.SetClipMask(session.SelectedLayer!.Id, mask);
                if (mask is VectorClipMask)
                {
                    session.MaskEditing.SelectNode(node.Id);
                }
                Flush(host);
                foreach (var row in view.GetVisualDescendants().OfType<AnimationPropertyRowControl>())
                {
                    var property = Property(row);
                    Assert.Contains(property, AnimationPropertyMetadata.CurrentProperties);
                    exposed.Add(property);
                    Assert.Equal(AnimationPropertyMetadata.GetValueKind(property) == AnimationValueKind.SCALAR, row.IsScalar);
                    Assert.Equal(AnimationPropertyMetadata.GetValueKind(property) is AnimationValueKind.VECTOR or AnimationValueKind.VECTOR3, row.IsVector);
                    Assert.Equal(AnimationPropertyMetadata.GetValueKind(property) == AnimationValueKind.COLOR, row.IsColor);
                    Assert.False(string.IsNullOrWhiteSpace(row.Label));
                    Assert.True(row.ShowAnimationActions);
                    Assert.NotNull(row.AnimationOptionsCommand);
                    Assert.NotNull(row.AddKeyframeCommand);
                    Assert.NotNull(row.ResetCommand);
                    Assert.Single(row.GetVisualDescendants().OfType<PropertyRowControl>());
                }
            }
            Assert.Equal(25, AnimationPropertyMetadata.CurrentProperties.Length);
            Assert.Equal(AnimationPropertyMetadata.CurrentProperties.Order(), exposed.Order());
        }
        finally
        {
            host.Close();
        }
    }

    /// <summary>真实非法输入与既有别名同步，语言、主题和焦点刷新不提交草稿，Esc 保留其他字段。</summary>
    [AvaloniaTheory]
    [InlineData("PositionXInput")]
    [InlineData("ScaleXInput")]
    [InlineData("OpacityInput")]
    [InlineData("BlurInput")]
    public async Task InvalidCommonRowInputPreservesItsRawAliasThroughLanguageThemeAndFocusChanges(string inputName)
    {
        await using var context = new MainWindowTestContext();
        var originalLanguage = Localization.SelectedLanguageID;
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var effects = context.ViewModel.Effects;
        effects.TransformExpanded = true;
        effects.CompositeExpanded = true;
        var input = UiTestActions.Find<NumericDraftInput>(context.Window, inputName);
        input.BringIntoView();
        Flush(context.Window);
        var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        var originalText = input.RawText;
        var source = context.Session.DocumentSnapshot;
        try
        {
            Assert.True(text.Focus());
            UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
            context.Window.KeyTextInput("7e-");
            var other = UiTestActions.Find<NumericDraftInput>(context.Window, "RotationInput");
            other.RawText = "35";
            Assert.True(UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline").Focus());
            Flush(context.Window);
            foreach (var language in new[] { "zh-CN", "en-US" })
            {
                Localization.SetLanguage(language);
                context.Window.RequestedThemeVariant = language == "zh-CN" ? ThemeVariant.Dark : ThemeVariant.Light;
                context.Session.RefreshEffectsInspectorTarget();
                Flush(context.Window);
                Assert.Equal("7e-", input.RawText);
                Assert.Equal("7e-", text.Text);
                Assert.Equal("7e-", RawAlias(effects, inputName));
                Assert.Equal("35", other.RawText);
                Assert.Same(source, context.Session.DocumentSnapshot);
                Assert.False(text.IsFocused);
            }
            Assert.True(text.Focus());
            UiTestActions.Press(context.Window, Key.Escape);
            Flush(context.Window);
            Assert.Equal(originalText, input.RawText);
            Assert.Equal(originalText, RawAlias(effects, inputName));
            Assert.Equal("35", other.RawText);
            Assert.Same(source, context.Session.DocumentSnapshot);
        }
        finally
        {
            effects.RestoreField(inputName);
            effects.RestoreField("RotationInput");
            context.Session.TryCommitDrafts(false);
            Localization.SetLanguage(originalLanguage);
        }
    }

    /// <summary>合成模糊使用正式 512 上限，合法值提交一次，超限原始输入留在同一草稿。</summary>
    [AvaloniaFact]
    public async Task BlurAcceptsTheMetadataMaximumAndRejectsValuesBeyondItWithoutChangingTheProject()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        context.ViewModel.Effects.CompositeExpanded = true;
        var input = UiTestActions.Find<NumericDraftInput>(context.Window, "BlurInput");
        input.BringIntoView();
        Flush(context.Window);
        var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        var source = context.Session.DocumentSnapshot;
        try
        {
            Assert.Equal((decimal)AnimationPropertyMetadata.GetMaximum(AnimationProperty.BLUR), input.Maximum);
            Type(context.Window, text, "512");
            Assert.Equal(512, context.Session.PreviewDocument.Layers[0].Blur);
            Assert.Same(source, context.Session.DocumentSnapshot);
            UiTestActions.Press(context.Window, Key.Enter);
            Flush(context.Window);
            Assert.Equal(512, context.Session.SelectedLayer!.Blur);
            var committed = context.Session.DocumentSnapshot;
            Type(context.Window, text, "512.5");
            UiTestActions.Press(context.Window, Key.Enter);
            Flush(context.Window);
            Assert.False(context.Session.TryCommitDrafts(false));
            Assert.Equal("512.5", input.RawText);
            Assert.Equal("512.5", context.ViewModel.Effects.BlurText);
            Assert.Same(committed, context.Session.DocumentSnapshot);
            UiTestActions.Press(context.Window, Key.Escape);
            Flush(context.Window);
            Assert.Equal("512", input.RawText);
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(source, context.Session.DocumentSnapshot);
        }
        finally
        {
            context.ViewModel.Effects.RestoreField("BlurInput");
            context.Session.TryCommitDrafts(false);
        }
    }

    /// <summary>真实下拉选择混合模式仅产生一次编辑，开放下拉及语言刷新不重写动画或新增撤销。</summary>
    [AvaloniaFact]
    public async Task BlendPointerSelectionCommitsOnceAndDropdownAndLanguageRefreshDoNotWriteUntouchedAnimation()
    {
        await using var context = new MainWindowTestContext();
        var originalLanguage = Localization.SelectedLanguageID;
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var layerId = session.SelectedLayer!.Id;
        session.Editor.SetKeyframe(layerId, AnimationProperty.OPACITY, new(new(0), 0.2));
        session.Editor.SetKeyframe(layerId, AnimationProperty.OPACITY, new(new(5), 0.8));
        session.Editor.Reset(session.DocumentSnapshot);
        context.ViewModel.Effects.CompositeExpanded = true;
        var source = session.DocumentSnapshot;
        var tracks = session.SelectedLayer.Tracks;
        var blend = UiTestActions.Find<ComboBox>(context.Window, "BlendCombo");
        try
        {
            var opacity = UiTestActions.Find<NumericDraftInput>(context.Window, "OpacityInput");
            opacity.BringIntoView();
            Flush(context.Window);
            Assert.True(Assert.Single(opacity.GetVisualDescendants().OfType<TextBox>()).Focus());
            OpenDropdown(context.Window, blend);
            var item = Assert.IsAssignableFrom<Control>(blend.ContainerFromIndex((int)BlendMode.SCREEN));
            item.BringIntoView();
            Flush(context.Window);
            var root = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(item));
            var point = item.TranslatePoint(new Avalonia.Point(item.Bounds.Width / 2, item.Bounds.Height / 2), root)!.Value;
            root.MouseDown(point, MouseButton.Left);
            root.MouseUp(point, MouseButton.Left);
            Flush(context.Window);
            Assert.Equal((int)BlendMode.SCREEN, blend.SelectedIndex);
            Assert.Equal(BlendMode.SCREEN, session.SelectedLayer!.Blend);
            Assert.Equal(tracks, session.SelectedLayer.Tracks);
            var committed = session.DocumentSnapshot;
            OpenDropdown(context.Window, blend);
            var items = blend.ItemsSource;
            var selected = blend.SelectedItem;
            for (var refresh = 0; refresh < 5; refresh++)
            {
                session.Tick();
                session.RefreshEffectsInspectorTarget();
                Flush(context.Window);
                Assert.True(blend.IsDropDownOpen);
                Assert.Same(items, blend.ItemsSource);
                Assert.Same(selected, blend.SelectedItem);
                Assert.Same(committed, session.DocumentSnapshot);
                Assert.Equal(tracks, session.SelectedLayer.Tracks);
            }
            blend.IsDropDownOpen = false;
            foreach (var language in new[] { "zh-CN", "en-US" })
            {
                Localization.SetLanguage(language);
                Flush(context.Window);
                Assert.Equal((int)BlendMode.SCREEN, blend.SelectedIndex);
                Assert.Equal((int)BlendMode.SCREEN, context.ViewModel.Effects.Blend);
                Assert.Same(committed, session.DocumentSnapshot);
            }
            Assert.True(session.Editor.Undo());
            Assert.Same(source, session.DocumentSnapshot);
            Assert.False(session.Editor.CanUndo);
        }
        finally
        {
            blend.IsDropDownOpen = false;
            Localization.SetLanguage(originalLanguage);
        }
    }

    private static AnimationProperty Property(AnimationPropertyRowControl row) => row.DataContext is AnimationPropertyRowViewModel model
        ? model.Target.Property : Assert.IsType<AnimationProperty>(row.CommandParameter);

    private static string RawAlias(EffectsPanelViewModel effects, string inputName) => inputName switch
    {
        "PositionXInput" => effects.PositionXText,
        "ScaleXInput" => effects.ScaleXText,
        "OpacityInput" => effects.OpacityText,
        "BlurInput" => effects.BlurText,
        _ => throw new ArgumentOutOfRangeException(nameof(inputName))
    };

    private static void Type(Window host, TextBox text, string value)
    {
        Assert.True(text.Focus());
        UiTestActions.Press(host, Key.A, RawInputModifiers.Control);
        host.KeyTextInput(value);
        Flush(host);
    }

    private static void OpenDropdown(Window host, ComboBox choice)
    {
        choice.BringIntoView();
        Flush(host);
        var root = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(choice));
        var point = choice.TranslatePoint(new Avalonia.Point(choice.Bounds.Width / 2, choice.Bounds.Height / 2), root)!.Value;
        root.MouseDown(point, MouseButton.Left);
        root.MouseUp(point, MouseButton.Left);
        Flush(host);
        Assert.True(choice.IsDropDownOpen);
    }

    private static void Flush(Window host)
    {
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
    }
}
