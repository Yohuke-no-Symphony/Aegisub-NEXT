using AegiNext.Desktop.I18n;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AegiNext.Desktop.Views;

internal sealed class AssetFolderDeletionDialog : Window
{
    private readonly List<IDisposable> localizationBindings = [];

    internal AssetFolderDeletionDialog(string name)
    {
        Width = 470;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        localizationBindings.Add(this.Bind(TitleProperty, Localization.Observe("Workbench.RemoveAssetFolder").ToBinding()));
        var message = new TextBlock { Name = "AssetFolderDeletionMessage", TextWrapping = TextWrapping.Wrap };
        localizationBindings.Add(message.Bind(TextBlock.TextProperty,
            Localization.Observe(() => Localization.Format("Workbench.AssetFolderDeletionConfirmation", name)).ToBinding()));
        var cancel = new Button { Name = "CancelButton", IsCancel = true, IsDefault = true };
        var delete = new Button { Name = "DeleteButton" };
        localizationBindings.Add(cancel.Bind(ContentControl.ContentProperty, Localization.Observe("Workbench.Cancel").ToBinding()));
        localizationBindings.Add(delete.Bind(ContentControl.ContentProperty, Localization.Observe("Workbench.Delete").ToBinding()));
        cancel.Click += (_, _) => Close(false);
        delete.Click += (_, _) => Close(true);
        Opened += (_, _) => cancel.Focus();
        Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 20,
            Children =
            {
                message,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Children = { cancel, delete }
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
