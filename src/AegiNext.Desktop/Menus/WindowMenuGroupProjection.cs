using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Styling;
using Avalonia.Controls;

namespace AegiNext.Desktop.Menus;

internal sealed class WindowMenuGroupProjection
{
    private readonly WorkbenchMenuGroup definition;
    private readonly WorkbenchMenuCatalog catalog;
    private readonly Dictionary<WorkbenchCommand, MenuItem> commands = [];
    private readonly Dictionary<string, MenuItem> layouts = new(StringComparer.Ordinal);
    private readonly List<Control> commandControls = [];
    private string[]? layoutOrder;
    private readonly List<WindowMenuGroupProjection> children = [];

    internal WindowMenuGroupProjection(WorkbenchMenuGroup definition, WorkbenchMenuCatalog catalog)
    {
        this.definition = definition;
        this.catalog = catalog;
        if (definition.IconKey is { } iconKey)
        {
            Item.Icon = WorkbenchIcon.Create(iconKey);
        }
        foreach (var command in definition.Commands)
        {
            if (command is { } id)
            {
                var item = new MenuItem { Command = catalog.GetCommand(id), Icon = WorkbenchIcon.Create(id.ToString()) };
                commands.Add(id, item);
                commandControls.Add(item);
            }
            else
            {
                commandControls.Add(new Separator());
            }
        }
        if (!definition.Children.IsDefaultOrEmpty)
        {
            foreach (var child in definition.Children)
            {
                var projection = new WindowMenuGroupProjection(child, catalog);
                children.Add(projection);
                commandControls.Add(projection.Item);
            }
        }
        if (definition.Key != "Layouts")
        {
            foreach (var control in commandControls)
            {
                Item.Items.Add(control);
            }
        }
        Refresh();
    }

    internal MenuItem Item { get; } = new();

    internal void Refresh()
    {
        Item.Header = Localization.Get(definition.LabelKey);
        foreach (var child in children)
        {
            child.Refresh();
        }
        foreach (var pair in commands)
        {
            pair.Value.Header = catalog.GetDisplayLabel(pair.Key);
            pair.Value.InputGesture = catalog.GetGesture(pair.Key);
        }
        if (definition.Key != "Layouts")
        {
            return;
        }

        var choices = catalog.LayoutChoices;
        var ids = choices.Select(choice => choice.Id).ToArray();
        var changed = layoutOrder is null || !layoutOrder.SequenceEqual(ids);
        foreach (var id in layouts.Keys.Where(id => !ids.Contains(id, StringComparer.Ordinal)).ToArray())
        {
            layouts.Remove(id);
        }
        foreach (var choice in choices)
        {
            if (!layouts.TryGetValue(choice.Id, out var item))
            {
                item = new() { ToggleType = MenuItemToggleType.Radio };
                layouts.Add(choice.Id, item);
            }
            item.Header = choice.Title;
            item.Command = choice.Command;
            item.IsChecked = choice.IsSelected;
        }
        if (!changed)
        {
            return;
        }
        layoutOrder = ids;
        Item.Items.Clear();
        foreach (var choice in choices)
        {
            Item.Items.Add(layouts[choice.Id]);
        }
        Item.Items.Add(new Separator());
        foreach (var control in commandControls)
        {
            Item.Items.Add(control);
        }
    }
}
