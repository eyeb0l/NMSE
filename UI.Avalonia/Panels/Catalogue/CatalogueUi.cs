using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NMSE.Data;

namespace NMSE.UI.Avalonia.Panels.Catalogue;

internal static class CatalogueUi
{
    internal static Grid PageGrid(Control toolbar, Control content, Control? footer = null)
    {
        var grid = new Grid
        {
            Margin = new Thickness(10),
            RowDefinitions = new RowDefinitions(footer == null ? "Auto,*" : "Auto,*,Auto"),
            RowSpacing = 8,
            Children = { toolbar, content }
        };
        global::Avalonia.Controls.Grid.SetRow(content, 1);
        if (footer != null)
        {
            grid.Children.Add(footer);
            global::Avalonia.Controls.Grid.SetRow(footer, 2);
        }
        return grid;
    }

    internal static StackPanel Toolbar(params Control[] controls)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        foreach (Control control in controls) panel.Children.Add(control);
        return panel;
    }

    internal static Button Button(string text, Action action)
    {
        var button = new Button { Content = text };
        button.Click += (_, _) => action();
        return button;
    }

    internal static Button AsyncButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text };
        button.Click += async (_, _) => await action();
        return button;
    }

    internal static Button JsonButton(Action action)
    {
        var button = Button("📑", action);
        button.Padding = new Thickness(7, 3);
        ToolTip.SetTip(button, UiStrings.Get("goto_json.tooltip"));
        return button;
    }

    internal static TextBox FilterBox(string placeholder, Action<string> changed)
    {
        var box = new TextBox { Width = 260, PlaceholderText = placeholder };
        box.TextChanged += (_, _) => changed(box.Text ?? string.Empty);
        return box;
    }

    internal static DataGrid Grid(bool readOnly = true, DataGridSelectionMode selection = DataGridSelectionMode.Extended) => new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = readOnly,
        CanUserResizeColumns = true,
        CanUserSortColumns = true,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        SelectionMode = selection,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch
    };

    internal static DataGridTextColumn TextColumn(string header, string property, double width = 0, bool star = false, bool readOnly = true) => new()
    {
        Header = header,
        Binding = new Binding(property) { Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay },
        IsReadOnly = readOnly,
        Width = star ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width)
    };

    internal static DataGridTemplateColumn ImageColumn(string header, string property, double width = 44) => new()
    {
        Header = header,
        Width = new DataGridLength(width),
        CellTemplate = new FuncDataTemplate<object>((item, _) =>
        {
            var image = new Image { Width = 28, Height = 28, Margin = new Thickness(3), Stretch = Stretch.Uniform };
            image.Bind(Image.SourceProperty, new Binding(property));
            return image;
        })
    };

    internal static async Task<bool> ConfirmAsync(Window owner, string title, string message)
    {
        var yes = new Button { Content = UiStrings.Get("common.yes"), MinWidth = 90 };
        var no = new Button { Content = UiStrings.Get("common.no"), MinWidth = 90 };
        var dialog = new Window
        {
            Title = title,
            Width = 500,
            Height = 190,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
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
        yes.Click += (_, _) => dialog.Close(true);
        no.Click += (_, _) => dialog.Close(false);
        return await dialog.ShowDialog<bool>(owner);
    }

    internal static async Task MessageAsync(Window owner, string title, string message)
    {
        var close = new Button { Content = UiStrings.Get("common.close"), MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new Window
        {
            Title = title,
            Width = 520,
            Height = 210,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(18), Spacing = 14,
                Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, close }
            }
        };
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }
}
