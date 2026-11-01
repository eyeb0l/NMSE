using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using NMSE.Data;

namespace NMSE.UI.Avalonia.Panels.Multitools;

internal sealed class ArchiveSelectionDialog : Window
{
    private readonly ListBox _list = new();

    private ArchiveSelectionDialog(IReadOnlyList<string> entries, string title)
    {
        Title = title;
        Width = 430;
        Height = 330;
        MinWidth = 320;
        MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _list.ItemsSource = entries;
        _list.SelectedIndex = entries.Count > 0 ? 0 : -1;
        _list.DoubleTapped += (_, _) => Accept();
        var accept = new Button { Content = UiStrings.Get("common.import"), IsDefault = true };
        var cancel = new Button { Content = UiStrings.Get("common.cancel"), IsCancel = true };
        accept.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close(-1);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, accept } };
        var root = new Grid { Margin = new Thickness(12), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 8,
            Children = { new TextBlock { Text = $"{title}:" }, _list, buttons } };
        Grid.SetRow(_list, 1); Grid.SetRow(buttons, 2);
        Content = root;
    }

    public static Task<int> ShowAsync(Window owner, IReadOnlyList<string> entries, string title) =>
        new ArchiveSelectionDialog(entries, title).ShowDialog<int>(owner);

    private void Accept() => Close(_list.SelectedIndex);
}
