using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.UI.Avalonia.Dialogs;

internal static class HotActionParametersWindow
{
    internal sealed record Parameters(string Id, int Number, int X, int Y);

    internal static async Task<Parameters?> ShowAsync(Window owner, string title, HotActionParameterKind kind, JsonObject? slot)
    {
        var (x, y) = HotActionsLogic.GetInventoryIndex(slot);
        NumericUpDown Number(int value, int minimum, int maximum) => new() { Minimum = minimum, Maximum = maximum, Value = value, FormatString = "0" };
        var number = Number(HotActionsLogic.GetNumber(slot), 0, kind == HotActionParameterKind.CreatureFood ? 9999 : 999);
        var indexX = Number(x, -1, 999);
        var indexY = Number(y, -1, 999);
        var id = new TextBox { Text = HotActionsLogic.GetId(slot) };
        var fields = new StackPanel { Spacing = 8 };
        void Row(string key, Control control)
        {
            fields.Children.Add(new TextBlock { Text = UiStrings.Get("hotkeys." + key) });
            fields.Children.Add(control);
        }
        switch (kind)
        {
            case HotActionParameterKind.Index: Row("index", number); break;
            case HotActionParameterKind.InventoryItem:
                Row("inventory", number); Row("slot_x", indexX); Row("slot_y", indexY); break;
            case HotActionParameterKind.EmoteId: Row("emote_id", id); break;
            case HotActionParameterKind.WeaponMode: Row("weapon_mode", number); break;
            case HotActionParameterKind.CreatureFood: Row("amount", number); Row("item_id", id); break;
        }
        var apply = new Button { Content = UiStrings.Get("common.ok"), IsDefault = true };
        var cancel = new Button { Content = UiStrings.Get("common.cancel"), IsCancel = true };
        fields.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { apply, cancel } });
        var window = new Window { Title = title, Width = 440, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new Border { Padding = new Thickness(16), Child = fields } };
        apply.Click += (_, _) => window.Close(new Parameters(string.IsNullOrWhiteSpace(id.Text) ? "^" : id.Text.Trim(),
            (int)(number.Value ?? 0), (int)(indexX.Value ?? -1), (int)(indexY.Value ?? -1)));
        cancel.Click += (_, _) => window.Close(null);
        return await window.ShowDialog<Parameters?>(owner);
    }
}
