using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Data;

namespace NMSE.UI.Avalonia.Panels.BasesStorage;

internal static class BaseUi
{
    internal static Grid FormGrid() => new()
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, RowSpacing = 6
    };

    internal static TextBlock AddRow(Grid grid, int row, string label, Control field)
    {
        while (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(text, row); Grid.SetRow(field, row); Grid.SetColumn(field, 1);
        grid.Children.Add(text); grid.Children.Add(field); return text;
    }

    internal static TextBlock AddHeader(Grid grid, int row, string text)
    {
        while (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var header = new TextBlock { Text = text, FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 9, 0, 2) };
        Grid.SetRow(header, row); Grid.SetColumnSpan(header, 2); grid.Children.Add(header); return header;
    }

    internal static Button JsonButton(Action action)
    {
        var button = new Button { Content = "📑", Padding = new Thickness(7, 3) };
        button.Click += (_, _) => action(); return button;
    }

    internal static async Task<bool> ConfirmAsync(Window owner, string title, string message)
    {
        var yes = new Button { Content = UiStrings.Get("common.yes"), MinWidth = 90 };
        var no = new Button { Content = UiStrings.Get("common.no"), MinWidth = 90 };
        var window = new Window
        {
            Title = title, Width = 480, Height = 190, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(18), Spacing = 14,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { no, yes } }
                }
            }
        };
        yes.Click += (_, _) => window.Close(true); no.Click += (_, _) => window.Close(false);
        return await window.ShowDialog<bool>(owner);
    }

    internal static async Task MessageAsync(Window owner, string title, string message)
    {
        var close = new Button { Content = UiStrings.Get("common.close"), MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right };
        var window = new Window
        {
            Title = title, Width = 520, Height = 210, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(18), Spacing = 14,
                Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, close }
            }
        };
        close.Click += (_, _) => window.Close(); await window.ShowDialog(owner);
    }
}
