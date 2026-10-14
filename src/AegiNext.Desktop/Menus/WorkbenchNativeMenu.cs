using System.Windows.Input;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace AegiNext.Desktop.Menus;

internal sealed class WorkbenchNativeMenu
{
    private readonly Dictionary<string, NativeMenuItem> groups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkbenchMenuGroup> groupDefinitions = new(StringComparer.Ordinal);
    private readonly Dictionary<WorkbenchCommand, NativeMenuItem> items = [];
    private readonly Dictionary<string, NativeMenuItem> layoutItems = new(StringComparer.Ordinal);
    private readonly WorkbenchMenuGroup[] definitions;
    private bool enabled = true;
    private string[] layoutOrder = [];

    internal WorkbenchNativeMenu(IEnumerable<WorkbenchMenuGroup> definitions,
        Func<WorkbenchCommand, ICommand> commandProvider, Func<string, Bitmap> iconProvider)
    {
        this.definitions = definitions.ToArray();
        foreach (var definition in this.definitions)
        {
            AddGroup(Menu, definition, commandProvider, iconProvider);
        }
    }

    private readonly Dictionary<string, NativeMenu> parents = new(StringComparer.Ordinal);

    private void AddGroup(NativeMenu parent, WorkbenchMenuGroup definition,
        Func<WorkbenchCommand, ICommand> commandProvider, Func<string, Bitmap> iconProvider)
    {
        var submenu = new NativeMenu();
        var group = new NativeMenuItem
        {
            Menu = submenu,
            Icon = definition.IconKey is { } iconKey ? iconProvider(iconKey) : null
        };
        groups.Add(definition.Key, group);
        groupDefinitions.Add(definition.Key, definition);
        parents.Add(definition.Key, parent);
        parent.Items.Add(group);
        foreach (var command in definition.Commands)
        {
            if (command is not { } value)
            {
                submenu.Items.Add(new NativeMenuItemSeparator());
                continue;
            }
            var item = new NativeMenuItem { Command = commandProvider(value), Icon = iconProvider(value.ToString()) };
            items.Add(value, item);
            submenu.Items.Add(item);
        }
        if (!definition.Children.IsDefaultOrEmpty)
        {
            foreach (var child in definition.Children)
            {
                AddGroup(submenu, child, commandProvider, iconProvider);
            }
        }
    }

    internal NativeMenu Menu { get; } = new();

    internal void Update(Func<string, string> groupLabel, Func<WorkbenchCommand, string> commandLabel,
        Func<WorkbenchCommand, string> gestureLabel)
    {
        foreach (var group in groups.ToArray())
        {
            var label = groupLabel(groupDefinitions[group.Key].LabelKey);
            if (group.Value.Header == label)
            {
                continue;
            }

            if (!OperatingSystem.IsMacOS())
            {
                group.Value.Header = label;
                continue;
            }

            var previousSubmenu = group.Value.Menu!;
            var children = previousSubmenu.Items.ToArray();
            previousSubmenu.Items.Clear();
            var submenu = new NativeMenu();
            foreach (var child in children)
            {
                submenu.Items.Add(child);
            }

            var parent = parents[group.Key];
            var index = parent.Items.IndexOf(group.Value);
            var replacement = new NativeMenuItem(label) { Menu = submenu, Icon = group.Value.Icon };
            if (index >= 0)
            {
                parent.Items[index] = replacement;
            }

            groups[group.Key] = replacement;
            foreach (var childKey in parents.Where(pair => ReferenceEquals(pair.Value, previousSubmenu)).Select(pair => pair.Key).ToArray())
            {
                parents[childKey] = submenu;
            }
        }

        foreach (var item in items)
        {
            var gesture = gestureLabel(item.Key);
            item.Value.Header = commandLabel(item.Key) + (gesture.Length == 0 ? string.Empty : "    " + gesture);
        }
    }

    internal void SetEnabled(bool value)
    {
        if (enabled == value)
        {
            return;
        }

        enabled = value;
        Menu.Items.Clear();
        if (value)
        {
            foreach (var definition in definitions)
            {
                Menu.Items.Add(groups[definition.Key]);
            }
        }
    }

    internal void UpdateLayouts(IReadOnlyList<LayoutMenuChoice> choices)
    {
        if (!groups.TryGetValue("Layouts", out var group))
        {
            return;
        }

        var branch = group.Menu!;
        var existingIds = layoutItems.Keys.ToArray();
        var ids = choices.Select(choice => choice.Id).ToArray();
        var changed = !layoutOrder.SequenceEqual(ids);
        foreach (var id in existingIds.Where(id => !choices.Any(choice => choice.Id == id)))
        {
            layoutItems.Remove(id);
        }

        foreach (var choice in choices)
        {
            if (!layoutItems.TryGetValue(choice.Id, out var item))
            {
                item = new() { Command = choice.Command, ToggleType = MenuItemToggleType.Radio };
                layoutItems.Add(choice.Id, item);
            }

            item.Header = choice.Title;
            item.IsChecked = choice.IsSelected;
        }

        if (changed)
        {
            layoutOrder = ids;
            branch.Items.Clear();
            foreach (var choice in choices)
            {
                branch.Items.Add(layoutItems[choice.Id]);
            }

            branch.Items.Add(new NativeMenuItemSeparator());
            foreach (var command in definitions.Single(value => value.Key == "Layouts").Commands)
            {
                branch.Items.Add(command is { } id ? items[id] : new NativeMenuItemSeparator());
            }
        }
    }
}
