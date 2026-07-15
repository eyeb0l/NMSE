using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Data;
using NMSE.UI.Avalonia.Services;

namespace NMSE.UI.Avalonia.Dialogs;

/// <summary>A filterable, localized item picker supporting single and extended selection.</summary>
public sealed class ItemPickerDialog : Window
{
    private readonly ItemPickerViewModel _viewModel = new();
    private readonly TextBox _searchBox;
    private readonly ComboBox _typeCombo;
    private readonly ComboBox _categoryCombo;
    private readonly DataGrid _itemGrid;
    private readonly Button _acceptButton;
    private readonly TextBox _manualIdBox;

    public ItemPickerDialog(string? title = null)
    {
        Title = title ?? UiStrings.Get("inventory.item_picker");
        Width = 760;
        Height = 540;
        MinWidth = 560;
        MinHeight = 400;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        DataContext = _viewModel;

        _searchBox = new TextBox { PlaceholderText = UiStrings.Get("item_picker.filter_placeholder") };
        _searchBox.Bind(TextBox.TextProperty, new ReflectionBinding(nameof(ItemPickerViewModel.SearchText))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });

        var clearButton = new Button
        {
            Content = UiStrings.Get("item_picker.clear"),
            MinWidth = 34,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        clearButton.Click += (_, _) => _searchBox.Text = string.Empty;

        var searchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        searchRow.Children.Add(new TextBlock
        {
            Text = UiStrings.Get("inventory.label_search"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        searchRow.Children.Add(_searchBox);
        Grid.SetColumn(_searchBox, 1);
        searchRow.Children.Add(clearButton);
        Grid.SetColumn(clearButton, 2);

        _typeCombo = new ComboBox { MinWidth = 140 };
        _typeCombo.Bind(ItemsControl.ItemsSourceProperty, new ReflectionBinding(nameof(ItemPickerViewModel.TypeFilters)));
        _typeCombo.Bind(ComboBox.SelectedIndexProperty, new ReflectionBinding(nameof(ItemPickerViewModel.SelectedTypeIndex))
        {
            Mode = BindingMode.TwoWay,
        });
        _categoryCombo = new ComboBox { MinWidth = 140 };
        _categoryCombo.Bind(ItemsControl.ItemsSourceProperty, new ReflectionBinding(nameof(ItemPickerViewModel.CategoryFilters)));
        _categoryCombo.Bind(ComboBox.SelectedIndexProperty, new ReflectionBinding(nameof(ItemPickerViewModel.SelectedCategoryIndex))
        {
            Mode = BindingMode.TwoWay,
        });

        var filterRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*"), ColumnSpacing = 8 };
        filterRow.Children.Add(CreateCenteredLabel("inventory.label_filter_type"));
        filterRow.Children.Add(_typeCombo);
        Grid.SetColumn(_typeCombo, 1);
        var categoryLabel = CreateCenteredLabel("inventory.label_filter_category");
        categoryLabel.Margin = new Thickness(12, 0, 0, 0);
        filterRow.Children.Add(categoryLabel);
        Grid.SetColumn(categoryLabel, 2);
        filterRow.Children.Add(_categoryCombo);
        Grid.SetColumn(_categoryCombo, 3);

        var filters = new StackPanel { Spacing = 8, Children = { searchRow, filterRow } };

        _itemGrid = CreateItemGrid();
        _itemGrid.Bind(DataGrid.ItemsSourceProperty, new ReflectionBinding(nameof(ItemPickerViewModel.FilteredItems)));
        _itemGrid.SelectionChanged += OnGridSelectionChanged;
        _itemGrid.DoubleTapped += OnGridDoubleTapped;

        _manualIdBox = new TextBox { PlaceholderText = UiStrings.Get("item_picker.manual_placeholder") };
        _manualIdBox.Bind(TextBox.TextProperty, new ReflectionBinding(nameof(ItemPickerViewModel.ManualItemId))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        _manualIdBox.TextChanged += (_, _) => UpdateAcceptState();

        var manualButton = new Button { Content = UiStrings.Get("item_picker.add_manual") };
        manualButton.Click += OnConfirmManual;
        var manualRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        manualRow.Children.Add(CreateCenteredLabel("inventory.label_item_id"));
        manualRow.Children.Add(_manualIdBox);
        Grid.SetColumn(_manualIdBox, 1);
        manualRow.Children.Add(manualButton);
        Grid.SetColumn(manualButton, 2);

        var warning = new TextBlock
        {
            Text = UiStrings.Get("item_picker.manual_warning"),
            Foreground = Brushes.IndianRed,
            TextWrapping = TextWrapping.Wrap,
        };
        var manualArea = new StackPanel { Spacing = 5, Children = { manualRow, warning } };

        _acceptButton = new Button
        {
            Content = UiStrings.Get("item_picker.add"),
            IsDefault = true,
            IsEnabled = false,
            MinWidth = 92,
        };
        _acceptButton.Click += OnConfirmSelection;
        var cancelButton = new Button
        {
            Content = UiStrings.Get("common.cancel"),
            IsCancel = true,
            MinWidth = 92,
        };
        cancelButton.Click += (_, _) => Close(null);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelButton, _acceptButton },
        };

        var root = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
            RowSpacing = 12,
            Children = { filters, _itemGrid, manualArea, buttons },
        };
        Grid.SetRow(_itemGrid, 1);
        Grid.SetRow(manualArea, 2);
        Grid.SetRow(buttons, 3);
        Content = root;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    public string? SelectedItemId => _viewModel.HasResult ? _viewModel.ResultItemId : null;
    public IReadOnlyList<string> SelectedItemIds => _viewModel.ResultItemIds;
    public ItemPickerViewModel ViewModel => _viewModel;

    public void Initialize(
        GameItemDatabase database,
        AvaloniaIconProvider? iconProvider,
        string? filterCategory = null,
        string? filterType = null,
        bool multiSelect = false)
    {
        _itemGrid.SelectionMode = multiSelect ? DataGridSelectionMode.Extended : DataGridSelectionMode.Single;
        _viewModel.Initialize(database, iconProvider, filterCategory, filterType, multiSelect);
        _searchBox.Text = _viewModel.SearchText;
        _typeCombo.SelectedIndex = _viewModel.SelectedTypeIndex;
        _categoryCombo.SelectedIndex = _viewModel.SelectedCategoryIndex;
        _manualIdBox.Text = _viewModel.ManualItemId;
        UpdateAcceptState();
    }

    private DataGrid CreateItemGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            SelectionMode = DataGridSelectionMode.Single,
        };

        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = UiStrings.Get("item_picker.col_icon"),
            Width = new DataGridLength(46),
            CellTemplate = new FuncDataTemplate<ItemPickerItemViewModel>((item, _) => new Image
            {
                Source = item?.Icon,
                Width = 28,
                Height = 28,
                Margin = new Thickness(4),
                Stretch = Stretch.Uniform,
            }),
        });
        grid.Columns.Add(CreateTextColumn("item_picker.col_name", nameof(ItemPickerItemViewModel.Name), star: true));
        grid.Columns.Add(CreateTextColumn("item_picker.col_id", nameof(ItemPickerItemViewModel.Id), 160));
        grid.Columns.Add(CreateTextColumn("item_picker.col_category", nameof(ItemPickerItemViewModel.Category), 130));
        grid.Columns.Add(CreateTextColumn("common.type", nameof(ItemPickerItemViewModel.Type), 120));
        return grid;
    }

    private static DataGridTextColumn CreateTextColumn(string headerKey, string property, double width = 0, bool star = false) =>
        new()
        {
            Header = UiStrings.Get(headerKey),
            Binding = new ReflectionBinding(property),
            Width = star
                ? new DataGridLength(1, DataGridLengthUnitType.Star)
                : new DataGridLength(width),
        };

    private static TextBlock CreateCenteredLabel(string key) => new()
    {
        Text = UiStrings.Get(key),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private void OnGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _viewModel.SetSelectedItems(_itemGrid.SelectedItems.OfType<ItemPickerItemViewModel>());
        UpdateAcceptState();
    }

    private void OnGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_itemGrid.SelectedItems.Count > 0)
            ConfirmAndClose(manualOnly: false);
    }

    private void OnConfirmSelection(object? sender, RoutedEventArgs e) => ConfirmAndClose(manualOnly: false);
    private void OnConfirmManual(object? sender, RoutedEventArgs e) => ConfirmAndClose(manualOnly: true);

    private void ConfirmAndClose(bool manualOnly)
    {
        bool confirmed = manualOnly ? _viewModel.ConfirmManualId() : _viewModel.ConfirmSelection();
        if (confirmed)
            Close(_viewModel.ResultItemId);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ItemPickerViewModel.ManualItemId))
            UpdateAcceptState();
    }

    private void UpdateAcceptState()
    {
        _acceptButton.IsEnabled = _itemGrid.SelectedItems.Count > 0 ||
                                  !string.IsNullOrWhiteSpace(_manualIdBox.Text);
    }
}
