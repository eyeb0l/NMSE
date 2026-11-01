using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.UI.Avalonia.Dialogs;

internal static class DevNotesWindow
{
    internal static async Task<bool> ShowAsync(Window owner, JsonObject player, LocalisationService? localisation, CatalogueDatabase? catalogue = null)
    {
        var page = CatalogueCompletionLogic.ResolvePage(KnowledgeCatalogue.Pages.First(p => p.Id == "jr_devnotes"), catalogue);
        string Text(string key) => localisation?.Lookup(key) ?? ProcTechLogic.LookupGameString(null, key) ?? key;
        var count = new NumericUpDown { Minimum = 0, Maximum = DevNotesLogic.EntryCount, Value = DevNotesLogic.GetUnlockedCount(player, page.Slot, page.PageIndex), Width = 120 };
        var entries = new ListBox { ItemsSource = DevNotesLogic.Entries.Select(entry => $"{entry.Index + 1}. {Text(entry.NameLocKey)}").ToArray(), Height = 260 };
        var commentary = new TextBlock { TextWrapping = TextWrapping.Wrap };
        entries.SelectionChanged += (_, _) =>
        {
            if (entries.SelectedIndex < 0) return;
            var entry = DevNotesLogic.Entries[entries.SelectedIndex];
            commentary.Text = Text(entry.DateLocKey) + "\n\n" + Text(entry.LogLocKey);
        };
        var apply = new Button { Content = UiStrings.Get("common.apply") };
        var cancel = new Button { Content = UiStrings.Get("common.cancel"), IsCancel = true };
        var all = new Button { Content = "Unlock all" };
        var clear = new Button { Content = "Clear" };
        all.Click += (_, _) => count.Value = DevNotesLogic.EntryCount;
        clear.Click += (_, _) => count.Value = 0;
        var window = new Window
        {
            Title = "Developer commentary", Width = 760, Height = 720, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(16), Spacing = 8, Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { new TextBlock { Text = "Unlocked entries:" }, count, all, clear } },
                entries, new ScrollViewer { Height = 300, Content = commentary },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { apply, cancel } },
            } },
        };
        apply.Click += (_, _) => { bool changed = DevNotesLogic.SetUnlockedCount(player, (int)(count.Value ?? 0), page.Slot, page.PageIndex); window.Close(changed); };
        cancel.Click += (_, _) => window.Close(false);
        entries.SelectedIndex = 0;
        return await window.ShowDialog<bool>(owner);
    }
}
