using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace NMSE.UI.Avalonia.Panels.Catalogue;

internal sealed class CataloguePickerItem
{
    public Bitmap? Icon { get; init; }
    public required string Name { get; init; }
    public required string Id { get; init; }
    public required string Category { get; init; }
}

internal sealed class CataloguePickerDialog : Window
{
    private readonly IReadOnlyList<CataloguePickerItem> _source;
    private readonly ObservableCollection<CataloguePickerItem> _visible = [];
    private readonly DataGrid _grid;

    internal CataloguePickerDialog(string title, IReadOnlyList<CataloguePickerItem> items)
    {
        _source = items;
        Title = title;
        Width = 780;
        Height = 560;
        MinWidth = 560;
        MinHeight = 400;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var filter = CatalogueUi.FilterBox(NMSE.Data.UiStrings.Get("item_picker.filter_placeholder"), ApplyFilter);
        var clear = CatalogueUi.Button(NMSE.Data.UiStrings.Get("item_picker.clear"), () => filter.Text = string.Empty);
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        top.Children.Add(filter);
        top.Children.Add(clear);
        Grid.SetColumn(clear, 1);

        _grid = CatalogueUi.Grid(selection: DataGridSelectionMode.Extended);
        _grid.ItemsSource = _visible;
        _grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = NMSE.Data.UiStrings.Get("item_picker.col_icon"),
            Width = new DataGridLength(46),
            CellTemplate = new FuncDataTemplate<CataloguePickerItem>((item, _) => new Image
            {
                Source = item?.Icon, Width = 28, Height = 28, Margin = new Thickness(4), Stretch = Stretch.Uniform
            })
        });
        _grid.Columns.Add(CatalogueUi.TextColumn(NMSE.Data.UiStrings.Get("item_picker.col_name"), nameof(CataloguePickerItem.Name), star: true));
        _grid.Columns.Add(CatalogueUi.TextColumn(NMSE.Data.UiStrings.Get("item_picker.col_id"), nameof(CataloguePickerItem.Id), 180));
        _grid.Columns.Add(CatalogueUi.TextColumn(NMSE.Data.UiStrings.Get("item_picker.col_category"), nameof(CataloguePickerItem.Category), 250));
        _grid.DoubleTapped += OnDoubleTapped;

        var add = new Button { Content = NMSE.Data.UiStrings.Get("item_picker.add"), IsDefault = true, MinWidth = 92 };
        var cancel = new Button { Content = NMSE.Data.UiStrings.Get("common.cancel"), IsCancel = true, MinWidth = 92 };
        add.Click += (_, _) => Complete();
        cancel.Click += (_, _) => Close(Array.Empty<string>());
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Children = { cancel, add }
        };

        var root = new Grid
        {
            Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 10,
            Children = { top, _grid, buttons }
        };
        Grid.SetRow(_grid, 1);
        Grid.SetRow(buttons, 2);
        Content = root;
        ApplyFilter(string.Empty);
    }

    private void ApplyFilter(string text)
    {
        string filter = text.Trim();
        _visible.Clear();
        foreach (CataloguePickerItem item in _source.Where(item => filter.Length == 0 ||
                     item.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                     item.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                     item.Category.Contains(filter, StringComparison.CurrentCultureIgnoreCase)))
            _visible.Add(item);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e) => Complete();

    private void Complete()
    {
        string[] ids = _grid.SelectedItems.OfType<CataloguePickerItem>()
            .Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (ids.Length > 0) Close(ids);
    }
}
