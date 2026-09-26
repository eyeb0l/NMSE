using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using NMSE.Core;
using NMSE.Data;

namespace NMSE.UI.Avalonia.Panels.Catalogue;

public sealed class DatabaseSearchPanel : UserControl
{
    private readonly GameItemDatabase _database;
    private readonly TextBox _query = new();
    private readonly ComboBox _source = new() { MinWidth = 180 };
    private readonly DataGrid _grid = new() { IsReadOnly = true, AutoGenerateColumns = false };
    private readonly TextBlock _count = new();
    private readonly TextBox _details = new() { IsReadOnly = true, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, MinHeight = 85 };

    public DatabaseSearchPanel(GameItemDatabase database)
    {
        _database = database;
        foreach (string field in new[] { "Name", "Id", "ItemType", "Category", "SourceTable" })
            _grid.Columns.Add(new DataGridTextColumn { Header = field, Binding = new Binding(field), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _source.ItemsSource = new[] { UiStrings.Get("db_search.source_all") }.Concat(database.Items.Values.Select(x => x.SourceTable).Where(x => !string.IsNullOrEmpty(x)).Distinct().Order()).ToArray();
        _source.SelectedIndex = 0;
        _query.TextChanged += (_, _) => Refresh();
        _source.SelectionChanged += (_, _) => Refresh();
        _grid.SelectionChanged += (_, _) => _details.Text = (_grid.SelectedItem as GameItem)?.Description ?? string.Empty;
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        toolbar.Children.Add(_query); toolbar.Children.Add(_source); Grid.SetColumn(_source, 1);
        var root = new Grid { Margin = new Thickness(10), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 8 };
        root.Children.Add(toolbar); root.Children.Add(_grid); Grid.SetRow(_grid, 1);
        root.Children.Add(_count); Grid.SetRow(_count, 2); root.Children.Add(_details); Grid.SetRow(_details, 3);
        Content = root;
        ApplyUiLocalisation(); Refresh();
    }

    public void ApplyUiLocalisation()
    {
        _query.PlaceholderText = UiStrings.Get("db_search.placeholder");
        string[] keys = ["discovery.col_name", "discovery.col_id", "discovery.col_type", "discovery.col_category", "db_search.col_source"];
        for (int i = 0; i < keys.Length; i++) _grid.Columns[i].Header = UiStrings.Get(keys[i]);
        Refresh();
    }

    private void Refresh()
    {
        var query = DatabaseSearchLogic.ParseQuery(_query.Text);
        string? source = _source.SelectedIndex > 0 ? _source.SelectedItem as string : null;
        var rows = _database.Items.Values.Where(item => (source == null || item.SourceTable == source) && DatabaseSearchLogic.MatchesItem(item, query)).OrderBy(item => item.Name).ToArray();
        _grid.ItemsSource = rows;
        _count.Text = UiStrings.Format("db_search.count", rows.Length);
    }
}
