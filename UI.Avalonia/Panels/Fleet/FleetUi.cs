using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Data;

namespace NMSE.UI.Avalonia.Panels.Fleet;

internal static class FleetUi
{
    internal static Grid FormGrid() => new()
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        ColumnSpacing = 10,
        RowSpacing = 6,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    internal static TextBlock AddRow(Grid grid, int row, string text, Control control)
    {
        EnsureRow(grid, row);
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(label, row);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        grid.Children.Add(label);
        grid.Children.Add(control);
        return label;
    }

    internal static TextBlock AddHeader(Grid grid, int row, string text)
    {
        EnsureRow(grid, row);
        var header = new TextBlock
        {
            Text = text,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 10, 0, 2)
        };
        Grid.SetRow(header, row);
        Grid.SetColumnSpan(header, 2);
        grid.Children.Add(header);
        return header;
    }

    internal static StackPanel SeedEditor(TextBox box, Button generate) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Children = { box, generate }
    };

    internal static Button JsonButton(string tooltip, Action action)
    {
        var button = new Button { Content = "📑", Padding = new Thickness(7, 3) };
        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => action();
        return button;
    }

    internal static string NewSeed()
    {
        Span<byte> bytes = stackalloc byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return "0x" + Convert.ToHexString(bytes);
    }

    internal static async Task<bool> ConfirmAsync(Window owner, string title, string message)
    {
        var yes = new Button { Content = UiStrings.Get("common.yes"), MinWidth = 90 };
        var no = new Button { Content = UiStrings.Get("common.no"), MinWidth = 90 };
        var window = new Window
        {
            Title = title,
            Width = 460,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 15,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { no, yes }
                    }
                }
            }
        };
        yes.Click += (_, _) => window.Close(true);
        no.Click += (_, _) => window.Close(false);
        return await window.ShowDialog<bool>(owner);
    }

    internal static async Task MessageAsync(Window owner, string title, string message)
    {
        var close = new Button
        {
            Content = UiStrings.Get("common.close"), MinWidth = 90,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var window = new Window
        {
            Title = title,
            Width = 500,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 15,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, close
                }
            }
        };
        close.Click += (_, _) => window.Close();
        await window.ShowDialog(owner);
    }

    private static void EnsureRow(Grid grid, int row)
    {
        while (grid.RowDefinitions.Count <= row)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
    }
}
