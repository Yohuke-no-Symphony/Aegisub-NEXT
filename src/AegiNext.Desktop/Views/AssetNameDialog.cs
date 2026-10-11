using AegiNext.Desktop.I18n;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace AegiNext.Desktop.Views;

internal abstract class AssetNameDialog : Window
{
    private readonly List<IDisposable> localizationBindings = [];

    /// <summary>创建采用统一校验和交互的素材名称输入窗口。</summary>
    protected AssetNameDialog(string name, string titleKey, string placeholderKey, string inputName)
    {
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        localizationBindings.Add(this.Bind(TitleProperty, Localization.Observe(titleKey).ToBinding()));
        var input = new TextBox { Name = inputName, Text = name, MaxLength = 256 };
        localizationBindings.Add(input.Bind(TextBox.PlaceholderTextProperty, Localization.Observe(placeholderKey).ToBinding()));
        var cancel = new Button { Name = "CancelButton", IsCancel = true };
        var confirm = new Button { Name = "ConfirmButton", IsDefault = true };
        localizationBindings.Add(cancel.Bind(ContentControl.ContentProperty, Localization.Observe("Workbench.Cancel").ToBinding()));
        localizationBindings.Add(confirm.Bind(ContentControl.ContentProperty, Localization.Observe("Workbench.Confirm").ToBinding()));
        input.TextChanged += (_, _) => confirm.IsEnabled = !string.IsNullOrWhiteSpace(input.Text) && !input.Text.Any(char.IsControl);
        cancel.Click += (_, _) => Close(null);
        confirm.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(input.Text) && !input.Text.Any(char.IsControl))
            {
                Close(input.Text.Trim());
            }
        };
        Opened += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };
        Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 16,
            Children =
            {
                input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Children = { cancel, confirm }
                }
            }
        };
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        foreach (var binding in localizationBindings)
        {
            binding.Dispose();
        }
        localizationBindings.Clear();
    }
}
