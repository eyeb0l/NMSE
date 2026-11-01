using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Coordination;

namespace NMSE.UI.Avalonia.Panels.Settlements;

internal sealed class SettlementSlotPickerDialog : Window
{
    private readonly ComboBox _slots = new();

    internal SettlementSlotPickerDialog(JsonArray settlements)
    {
        Title = UiStrings.Get("settlement.slot_picker_title");
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var choices = new List<string>();
        for (int index = 0; index < settlements.Length; index++)
        {
            string name = settlements.GetObject(index).GetString("Name") ?? string.Empty;
            choices.Add(string.IsNullOrWhiteSpace(name)
                ? UiStrings.Format("settlement.slot_label", index + 1)
                : UiStrings.Format("settlement.slot_label_named", index + 1, name));
        }
        _slots.ItemsSource = choices;
        _slots.SelectedIndex = choices.Count > 0 ? 0 : -1;
        var ok = new Button { Content = UiStrings.Get("common.ok"), IsDefault = true, MinWidth = 85 };
        ok.Click += (_, _) => Close(_slots.SelectedIndex);
        var cancel = new Button { Content = UiStrings.Get("common.cancel"), IsCancel = true, MinWidth = 85 };
        cancel.Click += (_, _) => Close(-1);
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = UiStrings.Get("settlement.slot_picker_message"), TextWrapping = global::Avalonia.Media.TextWrapping.Wrap },
                _slots,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, ok },
                },
            },
        };
    }
}

internal sealed class SettlementProductionPickerDialog : Window
{
    private readonly ListBox _items = new();
    private readonly TextBox _filter = new();
    private readonly List<ProductionChoice> _all;

    internal SettlementProductionPickerDialog(AvaloniaResourceCatalog resources, IReadOnlyDictionary<string, string> allowed)
    {
        Title = UiStrings.Get("settlement.production_header");
        Width = 600;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _all = allowed.Select(pair => new ProductionChoice(pair.Key, pair.Value)).OrderBy(item => item.Name).ToList();
        _filter.PlaceholderText = UiStrings.Get("common.filter");
        _filter.TextChanged += (_, _) => ApplyFilter();
        _items.SelectionMode = SelectionMode.Single;
        var ok = new Button { Content = UiStrings.Get("common.ok"), IsDefault = true, MinWidth = 85 };
        ok.Click += (_, _) => Close((_items.SelectedItem as ProductionChoice)?.Id);
        var cancel = new Button { Content = UiStrings.Get("common.cancel"), IsCancel = true, MinWidth = 85 };
        cancel.Click += (_, _) => Close(null);
        Content = new Grid
        {
            Margin = new Thickness(12),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 8,
            Children =
            {
                _filter,
                _items,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, ok },
                },
            },
        };
        Grid.SetRow(_items, 1);
        Grid.SetRow((Control)((Grid)Content).Children[2], 2);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string filter = _filter.Text?.Trim() ?? string.Empty;
        _items.ItemsSource = _all.Where(item =>
            filter.Length == 0 ||
            item.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            item.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    private sealed record ProductionChoice(string Id, string Name)
    {
        public override string ToString() => $"{Name} ({Id})";
    }
}
