using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Contracts;
using NMSE.UI.Avalonia.Controls;
using NMSE.UI.Avalonia.Coordination;

namespace NMSE.UI.Avalonia.Panels.Settlements;

public sealed record SettlementDesktopActions(
    Func<SettlementFileRequest, Task<string?>> PickOpenFileAsync,
    Func<SettlementFileRequest, Task<string?>> PickSaveFileAsync,
    Func<string, string, Task<bool>> ConfirmAsync,
    Func<string, string, Task> ShowErrorAsync);

public sealed record SettlementFileRequest(string Title, string SuggestedFileName, params string[] Extensions);

/// <summary>Complete editor for owned settlements, production, perks and building-state bit fields.</summary>
public sealed class SettlementPanel : UserControl, ISaveEditorPanel, IGoToJsonSource
{
    private const int PerkSlotCount = 18;
    private readonly AvaloniaResourceCatalog _resources;
    private readonly SettlementDesktopActions _desktop;
    private readonly Random _random = new();
    private readonly ObservableCollection<string> _settlementNames = [];
    private readonly List<int> _filteredIndices = [];
    private readonly List<(TextBlock Label, string Key)> _localised = [];
    private readonly TextBlock _title;
    private readonly TextBlock _info;
    private readonly TextBlock _warning;
    private readonly ComboBox _settlementSelector;
    private readonly Button _deleteButton;
    private readonly Button _exportButton;
    private readonly Button _importButton;
    private readonly Button _goAllButton;
    private readonly Button _goSelectedButton;
    private readonly TabItem _statsTab;
    private readonly TabItem _productionTab;
    private readonly TabItem _buildingTab;
    private readonly TabItem _editorTab;
    private readonly TextBox _nameField = new() { MinWidth = 210 };
    private readonly TextBox _seedField = new() { MinWidth = 210 };
    private readonly Button _generateSeed = new();
    private readonly ComboBox _raceField = new() { MinWidth = 210 };
    private readonly InvariantNumericTextBox[] _statFields = new InvariantNumericTextBox[SettlementLogic.StatCount];
    private readonly TextBlock[] _statWarnings = new TextBlock[SettlementLogic.StatCount];
    private readonly InvariantNumericTextBox _populationField = new() { MinWidth = 170 };
    private readonly TextBlock _populationWarning = new();
    private readonly ComboBox _decisionType = new() { MinWidth = 210 };
    private readonly OptionalTimestampEditor _lastDecision = new();
    private readonly OptionalTimestampEditor _lastBugAttack = new();
    private readonly OptionalTimestampEditor _lastAlert = new();
    private readonly OptionalTimestampEditor _lastDebt = new();
    private readonly OptionalTimestampEditor _lastUpkeep = new();
    private readonly OptionalTimestampEditor _lastPopulation = new();
    private readonly OptionalTimestampEditor _missionStart = new();
    private readonly TextBox _missionSeed = new() { MinWidth = 210 };
    private readonly Button _generateMissionSeed = new();
    private readonly ComboBox[] _perkCombos = new ComboBox[PerkSlotCount];
    private readonly TextBox[] _perkSeeds = new TextBox[PerkSlotCount];
    private readonly Grid[] _perkSeedPanels = new Grid[PerkSlotCount];
    private readonly Button[] _perkGenerateButtons = new Button[PerkSlotCount];
    private readonly TextBlock[] _perkLabels = new TextBlock[PerkSlotCount];
    private readonly ObservableCollection<ProductionRow> _productionRows = [];
    private readonly DataGrid _productionGrid;
    private readonly DataGridTextColumn _productionNameColumn;
    private readonly DataGridTextColumn _productionIdColumn;
    private readonly DataGridTextColumn _productionAmountColumn;
    private readonly DataGridTemplateColumn _productionEditColumn;
    private readonly TextBlock _productionHeader;
    private readonly ComboBox[] _buildingCombos = new ComboBox[SettlementLogic.BuildingStateSlotCount];
    private readonly InvariantNumericTextBox[] _buildingValues = new InvariantNumericTextBox[SettlementLogic.BuildingStateSlotCount];
    private readonly TextBlock[] _buildingDescriptions = new TextBlock[SettlementLogic.BuildingStateSlotCount];
    private readonly TextBlock _buildingWarning;
    private readonly InvariantNumericTextBox _editorSlot = new() { Minimum = 1, Maximum = SettlementLogic.BuildingStateSlotCount, NumericValue = 1 };
    private readonly InvariantNumericTextBox _editorRaw = new() { Minimum = int.MinValue, Maximum = int.MaxValue };
    private readonly Button _editorApply = new();
    private readonly TextBlock _editorClass = new();
    private readonly TextBlock _editorState = new();
    private readonly TextBlock _editorWarning;
    private readonly CheckBox[] _editorInitial = new CheckBox[SettlementLogic.SettlementBuildingState.InitPhaseCount];
    private readonly CheckBox[] _editorUpgrade = new CheckBox[SettlementLogic.SettlementBuildingState.UpgradePhaseCount];
    private readonly CheckBox[] _editorTier = new CheckBox[SettlementLogic.SettlementBuildingState.TierBitCount];
    private readonly CheckBox[] _editorFlags = new CheckBox[SettlementLogic.SettlementBuildingState.FlagBitCount];
    private readonly Dictionary<string, string> _allowedProduction;
    private JsonArray? _settlements;
    private int[]? _rawStats;
    private int? _rawPopulation;
    private bool _hasPopulation;
    private int[]? _rawBuildingStates;
    private bool _hasBuildingStates;
    private bool _loading;
    private bool _editorUpdating;

    public SettlementPanel(AvaloniaResourceCatalog resources, SettlementDesktopActions desktop)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
        _allowedProduction = SettlementLogic.BuildAllowedProductionItems(resources.Items);

        _title = new TextBlock
        {
            FontFamily = Services.FontManager.NmsFont,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _goAllButton = new Button { Content = "\U0001F4D1", Width = 32, Height = 28 };
        _goAllButton.Click += (_, _) => GoToJsonRequested?.Invoke(
            this, new GoToJsonRequestEventArgs("PlayerStateData.SettlementStatesV2"));
        _goSelectedButton = new Button { Content = "\U0001F4D1", Width = 32, Height = 28 };
        _goSelectedButton.Click += (_, _) => GoToSelectedJson();
        var headerButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { _goAllButton, _goSelectedButton },
        };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { _title, headerButtons } };
        Grid.SetColumn(headerButtons, 1);

        _settlementSelector = new ComboBox { ItemsSource = _settlementNames, Width = 320 };
        _settlementSelector.SelectionChanged += OnSettlementSelected;
        _deleteButton = new Button();
        _deleteButton.Click += OnDelete;
        _exportButton = new Button();
        _exportButton.Click += OnExport;
        _importButton = new Button();
        _importButton.Click += OnImport;
        _warning = new TextBlock
        {
            Foreground = Brushes.DarkOrange,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 480,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var selectorRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _settlementSelector, _deleteButton, _exportButton, _importButton, _warning },
        };

        _statsTab = new TabItem { Content = CreateStatsAndPerksView() };
        (_productionTab, _productionGrid, _productionHeader, _productionNameColumn, _productionIdColumn,
            _productionAmountColumn, _productionEditColumn) = CreateProductionView();
        _buildingWarning = CreateWarningLabel();
        _buildingTab = new TabItem { Content = CreateBuildingStatesView() };
        _editorWarning = CreateWarningLabel();
        _editorTab = new TabItem { Content = CreateBuildingEditorView() };
        var tabs = new TabControl { FontSize = 13, Items = { _statsTab, _productionTab, _buildingTab, _editorTab } };
        _info = new TextBlock { TextWrapping = TextWrapping.Wrap };

        Content = new Grid
        {
            Margin = new Thickness(10),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 6,
            Children = { header, selectorRow, tabs, _info },
        };
        Grid.SetRow(selectorRow, 1);
        Grid.SetRow(tabs, 2);
        Grid.SetRow(_info, 3);
        ApplyUiLocalisation();
        ClearFields();
    }

    public event EventHandler? DataModified;
    public event EventHandler<GoToJsonRequestEventArgs>? GoToJsonRequested;

    public void LoadData(JsonObject saveData)
    {
        ArgumentNullException.ThrowIfNull(saveData);
        _loading = true;
        try
        {
            _settlementNames.Clear();
            _filteredIndices.Clear();
            ClearFields();
            JsonObject? playerState = saveData.GetObject("PlayerStateData");
            _settlements = playerState?.GetArray("SettlementStatesV2");
            if (playerState is null || _settlements is null || _settlements.Length == 0)
            {
                _info.Text = UiStrings.Get("settlement.no_settlements");
                return;
            }

            foreach (int index in SettlementLogic.FilterSettlements(saveData, playerState, _settlements))
            {
                _filteredIndices.Add(index);
                string name = _settlements.GetObject(index).GetString("Name")
                    ?? UiStrings.Format("settlement.fallback_name", index + 1);
                _settlementNames.Add($"[{index}] {name}");
            }
            _info.Text = UiStrings.Format("settlement.found_count", _filteredIndices.Count);
            _settlementSelector.SelectedIndex = _settlementNames.Count > 0 ? 0 : -1;
        }
        catch
        {
            _info.Text = UiStrings.Get("settlement.load_failed");
        }
        finally
        {
            _loading = false;
        }
        LoadSelectedSettlement();
    }

    public void SaveData(JsonObject saveData) => SaveSelectedSettlement(saveData);

    public void ApplyUiLocalisation()
    {
        _title.Text = UiStrings.Get("settlement.title");
        _deleteButton.Content = UiStrings.Get("settlement.delete");
        _exportButton.Content = UiStrings.Get("common.export");
        _importButton.Content = UiStrings.Get("common.import");
        _warning.Text = UiStrings.Get("settlement.delete_warning");
        _statsTab.Header = UiStrings.Get("settlement.tab_stats_perks");
        _productionTab.Header = UiStrings.Get("settlement.tab_production");
        _buildingTab.Header = UiStrings.Get("settlement.tab_building_states");
        _editorTab.Header = UiStrings.Get("settlement.tab_building_editor");
        _productionHeader.Text = UiStrings.Get("settlement.production_header");
        _productionNameColumn.Header = UiStrings.Get("settlement.col_name");
        _productionIdColumn.Header = UiStrings.Get("settlement.col_element_id");
        _productionAmountColumn.Header = UiStrings.Get("settlement.col_amount");
        _productionEditColumn.Header = UiStrings.Get("settlement.col_edit");
        _buildingWarning.Text = UiStrings.Get("settlement.experimental_warning");
        _editorWarning.Text = UiStrings.Get("settlement.experimental_warning");
        _generateSeed.Content = UiStrings.Get("common.generate");
        _generateMissionSeed.Content = UiStrings.Get("common.generate");
        _editorApply.Content = UiStrings.Get("settlement.bs_editor_apply");
        for (int index = 0; index < _editorInitial.Length; index++)
            _editorInitial[index].Content = $"Phase {index + 1}";
        for (int index = 0; index < _editorUpgrade.Length; index++)
            _editorUpgrade[index].Content = $"Sub {index + 1}";
        string[] tierNames =
        [
            UiStrings.Get("settlement.bs_editor_tier_b"),
            UiStrings.Get("settlement.bs_editor_tier_a"),
            UiStrings.Get("settlement.bs_editor_tier_s"),
        ];
        for (int group = 0; group < 3; group++)
        {
            _editorTier[group * 2].Content = $"{tierNames[group]} Awaiting Upgrade";
            _editorTier[group * 2 + 1].Content = $"{tierNames[group]} Complete";
        }
        string[] flagKeys =
        [
            "settlement.bs_editor_class_active", "settlement.bs_editor_b_arrived",
            "settlement.bs_editor_a_arrived", "settlement.bs_editor_s_arrived",
        ];
        for (int index = 0; index < _editorFlags.Length; index++)
            _editorFlags[index].Content = UiStrings.Get(flagKeys[index]);
        foreach ((TextBlock label, string key) in _localised)
            label.Text = UiStrings.Get(key);
        for (int index = 0; index < _perkLabels.Length; index++)
        {
            _perkLabels[index].Text = UiStrings.Format("settlement.perk", index + 1);
            _perkGenerateButtons[index].Content = UiStrings.Get("common.generate");
        }
        ToolTip.SetTip(_goAllButton, UiStrings.Format("goto_json.tooltip_section", _title.Text));
        ToolTip.SetTip(_goSelectedButton,
            UiStrings.Format("goto_json.tooltip_section", _title.Text + " " + UiStrings.Get("goto_json.nav_details")));
        if (_settlements is null)
            _info.Text = UiStrings.Get("settlement.info_placeholder");
        RefreshRaceChoices();
        RefreshDecisionChoices();
        RefreshPerkChoices();
        RefreshBuildingChoices();
        UpdateEditorDescription(ConvertToInt(_editorRaw.NumericValue));
    }

    private Control CreateStatsAndPerksView()
    {
        var left = new StackPanel { Spacing = 5 };
        AddRow(left, "settlement.name", _nameField);
        _nameField.LostFocus += (_, _) => UpdateSelectedName();
        _seedField.LostFocus += (_, _) => RaiseModified();
        _generateSeed.Click += (_, _) =>
        {
            byte[] bytes = new byte[8];
            _random.NextBytes(bytes);
            _seedField.Text = "0x" + Convert.ToHexString(bytes);
            RaiseModified();
        };
        AddRow(left, "settlement.seed", WithButton(_seedField, _generateSeed));
        _raceField.SelectionChanged += (_, _) => RaiseModified();
        AddRow(left, "settlement.race", _raceField);

        string[] statKeys =
        [
            "settlement.max_population", "settlement.happiness", "settlement.productivity",
            "settlement.upkeep", "settlement.sentinels", "settlement.debt",
            "settlement.alert", "settlement.bug_attack",
        ];
        for (int index = 0; index < _statFields.Length; index++)
        {
            int captured = index;
            _statFields[index] = new InvariantNumericTextBox
            {
                Minimum = int.MinValue,
                Maximum = int.MaxValue,
                MinWidth = 170,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            _statWarnings[index] = CreateLimitWarning();
            _statFields[index].NumericValueChanged += (_, _) =>
            {
                UpdateStatWarning(captured);
                RaiseModified();
            };
            AddRow(left, statKeys[index], WithWarning(_statFields[index], _statWarnings[index]));
            if (index == 0)
            {
                _populationField.Minimum = 0;
                _populationField.Maximum = SettlementLogic.PopulationMax;
                _populationField.NumericValueChanged += (_, _) =>
                {
                    UpdatePopulationWarning();
                    RaiseModified();
                };
                AddRow(left, "settlement.population", WithWarning(_populationField, _populationWarning));
                AddTimestampRow(left, "settlement.last_population_time", _lastPopulation);
            }
            else if (index == 3)
                AddTimestampRow(left, "settlement.last_upkeep_time", _lastUpkeep);
            else if (index == 5)
                AddTimestampRow(left, "settlement.last_debt_time", _lastDebt);
            else if (index == 6)
                AddTimestampRow(left, "settlement.last_alert_time", _lastAlert);
            else if (index == 7)
                AddTimestampRow(left, "settlement.last_bug_attack_time", _lastBugAttack);
        }

        _decisionType.SelectionChanged += (_, _) => RaiseModified();
        AddRow(left, "settlement.decision_type", _decisionType);
        AddTimestampRow(left, "settlement.last_decision", _lastDecision);
        _missionSeed.LostFocus += (_, _) => RaiseModified();
        _generateMissionSeed.Click += (_, _) =>
        {
            _missionSeed.Text = _random.Next(0, int.MaxValue).ToString(CultureInfo.InvariantCulture);
            RaiseModified();
        };
        AddRow(left, "settlement.mission_seed", WithButton(_missionSeed, _generateMissionSeed));
        AddTimestampRow(left, "settlement.mini_mission_start_time", _missionStart);

        var right = new StackPanel { Spacing = 5 };
        for (int index = 0; index < PerkSlotCount; index++)
        {
            int slot = index;
            _perkCombos[index] = new ComboBox { MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Stretch };
            _perkCombos[index].SelectionChanged += (_, _) => OnPerkChanged(slot);
            _perkSeeds[index] = new TextBox { Width = 120 };
            _perkSeeds[index].LostFocus += (_, _) => RaiseModified();
            _perkGenerateButtons[index] = new Button();
            _perkGenerateButtons[index].Click += (_, _) =>
            {
                _perkSeeds[slot].Text = _random.Next(0, int.MaxValue).ToString(CultureInfo.InvariantCulture);
                RaiseModified();
            };
            _perkSeedPanels[index] = WithButton(_perkSeeds[index], _perkGenerateButtons[index]);
            _perkSeedPanels[index].IsVisible = false;
            var remove = new Button { Content = "×", Width = 30 };
            remove.Click += (_, _) => _perkCombos[slot].SelectedIndex = 0;
            var fields = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                ColumnSpacing = 4,
                Children = { _perkCombos[index], _perkSeedPanels[index], remove },
            };
            Grid.SetColumn(_perkSeedPanels[index], 1);
            Grid.SetColumn(remove, 2);
            _perkLabels[index] = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            AddRow(right, _perkLabels[index], fields);
        }

        var layout = new Grid
        {
            MinWidth = 1180,
            ColumnDefinitions = new ColumnDefinitions("560,*"),
            ColumnSpacing = 12,
            Children = { left, right },
        };
        Grid.SetColumn(right, 1);
        return new ScrollViewer
        {
            Padding = new Thickness(6),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = layout,
        };
    }

    private (TabItem, DataGrid, TextBlock, DataGridTextColumn, DataGridTextColumn, DataGridTextColumn, DataGridTemplateColumn)
        CreateProductionView()
    {
        var header = new TextBlock
        {
            FontFamily = Services.FontManager.NmsFont,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
        };
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = _productionRows,
            CanUserSortColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        };
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Width = new DataGridLength(42),
            CellTemplate = new FuncDataTemplate<ProductionRow>((row, _) => new Image
            {
                Source = row?.Icon,
                Width = 28,
                Height = 28,
                Stretch = Stretch.Uniform,
            }),
        });
        var name = new DataGridTextColumn
        {
            Binding = new Binding(nameof(ProductionRow.ItemName)),
            IsReadOnly = true,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        };
        var id = new DataGridTextColumn
        {
            Binding = new Binding(nameof(ProductionRow.ElementId)),
            IsReadOnly = true,
            Width = new DataGridLength(180),
        };
        var amount = new DataGridTextColumn
        {
            Binding = new Binding(nameof(ProductionRow.Amount)) { Mode = BindingMode.TwoWay },
            Width = new DataGridLength(100),
        };
        var edit = new DataGridTemplateColumn
        {
            Width = new DataGridLength(100),
            CellTemplate = new FuncDataTemplate<ProductionRow>((row, _) =>
            {
                var button = new Button { Content = UiStrings.Get("settlement.col_edit") };
                if (row is not null)
                    button.Click += async (_, _) => await ChangeProductionElementAsync(row);
                return button;
            }),
        };
        grid.Columns.Add(name);
        grid.Columns.Add(id);
        grid.Columns.Add(amount);
        grid.Columns.Add(edit);
        var content = new Grid
        {
            Margin = new Thickness(6),
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 6,
            Children = { header, grid },
        };
        Grid.SetRow(grid, 1);
        return (new TabItem { Content = content }, grid, header, name, id, amount, edit);
    }

    private Control CreateBuildingStatesView()
    {
        var rows = new StackPanel { Spacing = 4, Children = { _buildingWarning } };
        for (int index = 0; index < SettlementLogic.BuildingStateSlotCount; index++)
        {
            int slot = index;
            _buildingCombos[index] = new ComboBox { MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Stretch };
            _buildingValues[index] = new InvariantNumericTextBox { Minimum = int.MinValue, Maximum = int.MaxValue, Width = 130 };
            _buildingDescriptions[index] = new TextBlock { MinWidth = 260, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };
            _buildingCombos[index].SelectionChanged += (_, _) => OnBuildingComboChanged(slot);
            _buildingValues[index].NumericValueChanged += (_, _) => OnBuildingValueChanged(slot);
            var row = new Grid
            {
                MinWidth = 980,
                ColumnDefinitions = new ColumnDefinitions("60,1.35*,130,1.65*"),
                ColumnSpacing = 6,
                Children =
                {
                    new TextBlock { Text = (index + 1).ToString(CultureInfo.InvariantCulture), VerticalAlignment = VerticalAlignment.Center },
                    _buildingCombos[index], _buildingValues[index], _buildingDescriptions[index],
                },
            };
            Grid.SetColumn(_buildingCombos[index], 1);
            Grid.SetColumn(_buildingValues[index], 2);
            Grid.SetColumn(_buildingDescriptions[index], 3);
            rows.Children.Add(row);
        }
        return new ScrollViewer
        {
            Padding = new Thickness(6),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = rows,
        };
    }

    private Control CreateBuildingEditorView()
    {
        var content = new StackPanel { Spacing = 7, Children = { _editorWarning } };
        _editorSlot.NumericValueChanged += (_, _) => LoadEditorFromSlot();
        _editorRaw.NumericValueChanged += (_, _) => OnEditorRawChanged();
        _editorApply.Click += (_, _) => ApplyEditorToSlot();
        AddRow(content, new TextBlock { Text = "Slot:", VerticalAlignment = VerticalAlignment.Center }, _editorSlot);
        AddRow(content, "settlement.bs_editor_raw_value", WithButton(_editorRaw, _editorApply));
        AddRow(content, "settlement.bs_editor_class", _editorClass);
        AddRow(content, "settlement.bs_editor_state", _editorState);
        content.Children.Add(CreateBitGroup("settlement.bs_editor_init_phases", _editorInitial, 0));
        content.Children.Add(CreateBitGroup("settlement.bs_editor_upgrade_phases", _editorUpgrade, 10));
        content.Children.Add(CreateBitGroup("settlement.bs_editor_tier_heading", _editorTier, 20));
        content.Children.Add(CreateBitGroup("settlement.bs_editor_flags_heading", _editorFlags, 26));
        return new ScrollViewer
        {
            Padding = new Thickness(8),
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = content,
        };
    }

    private Control CreateBitGroup(string titleKey, CheckBox[] boxes, int startBit)
    {
        var title = CreateLocalisedLabel(titleKey);
        title.FontFamily = Services.FontManager.NmsFont;
        title.FontWeight = FontWeight.SemiBold;
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        for (int index = 0; index < boxes.Length; index++)
        {
            boxes[index] = new CheckBox { Content = $"Bit {startBit + index}", Margin = new Thickness(0, 0, 10, 0) };
            boxes[index].IsCheckedChanged += (_, _) => OnEditorBitsChanged();
            wrap.Children.Add(boxes[index]);
        }
        return new StackPanel { Spacing = 3, Children = { title, wrap } };
    }

    private void LoadSelectedSettlement()
    {
        _loading = true;
        try
        {
            ClearFields();
            JsonObject? settlement = GetSelectedSettlement();
            if (settlement is null)
                return;
            SettlementLogic.SettlementData data = SettlementLogic.LoadSettlementData(settlement);
            _nameField.Text = data.Name;
            _seedField.Text = data.SeedValue;
            _rawStats = (int[])data.RawStats.Clone();
            for (int index = 0; index < _statFields.Length; index++)
            {
                _statFields[index].NumericValue = data.Stats[index];
                UpdateStatWarning(index);
            }
            _hasPopulation = data.HasPopulationKey;
            _populationField.IsEnabled = data.HasPopulationKey;
            _populationField.NumericValue = data.HasPopulationKey ? data.Population : 0;
            _rawPopulation = data.HasPopulationKey ? data.RawPopulation : null;
            UpdatePopulationWarning();
            SelectRace(data.AlienRace);
            _decisionType.SelectedIndex = data.DecisionTypeIndex;
            _lastDecision.SetLocalDateTime(data.LastDecisionTime);
            _lastBugAttack.SetUnixSeconds(data.LastBugAttackChangeTime);
            _lastAlert.SetUnixSeconds(data.LastAlertChangeTime);
            _lastDebt.SetUnixSeconds(data.LastDebtChangeTime);
            _lastUpkeep.SetUnixSeconds(data.LastUpkeepDebtCheckTime);
            _lastPopulation.SetUnixSeconds(data.LastPopulationChangeTime);
            _missionSeed.Text = data.MiniMissionSeed == 0 ? string.Empty : data.MiniMissionSeed.ToString(CultureInfo.InvariantCulture);
            _missionStart.SetUnixSeconds(data.MiniMissionStartTime);
            LoadPerks(settlement.GetArray("Perks"));
            LoadProduction(settlement.GetArray("ProductionState"));
            _hasBuildingStates = data.HasBuildingStates;
            _rawBuildingStates = (int[])data.RawBuildingStates.Clone();
            for (int index = 0; index < _buildingValues.Length; index++)
                SetBuildingValue(index, data.BuildingStates[index]);
            LoadEditorFromSlot();
        }
        finally
        {
            _loading = false;
        }
    }

    private void SaveSelectedSettlement(JsonObject saveData)
    {
        JsonObject? settlement = GetSelectedSettlement(saveData);
        if (settlement is null)
            return;
        foreach (InvariantNumericTextBox field in _statFields.Append(_populationField).Concat(_buildingValues))
            field.TryCommit();
        var values = new SettlementLogic.SettlementSaveValues
        {
            Name = _nameField.Text ?? string.Empty,
            SeedValue = _seedField.Text ?? string.Empty,
            RawStats = _rawStats,
            HasPopulationKey = _hasPopulation,
            Population = ConvertToInt(_populationField.NumericValue),
            RawPopulation = _rawPopulation,
            DecisionTypeIndex = _decisionType.SelectedIndex,
            LastDecisionTime = _lastDecision.GetLocalDateTime(),
            AlienRace = (_raceField.SelectedItem as RaceChoice)?.Id ?? "None",
            LastBugAttackChangeTime = _lastBugAttack.GetUnixSeconds(),
            LastAlertChangeTime = _lastAlert.GetUnixSeconds(),
            LastDebtChangeTime = _lastDebt.GetUnixSeconds(),
            LastUpkeepDebtCheckTime = _lastUpkeep.GetUnixSeconds(),
            LastPopulationChangeTime = _lastPopulation.GetUnixSeconds(),
            MiniMissionStartTime = _missionStart.GetUnixSeconds(),
            HasBuildingStates = _hasBuildingStates,
            RawBuildingStates = _rawBuildingStates,
        };
        for (int index = 0; index < values.Stats.Length; index++)
            values.Stats[index] = ConvertToInt(_statFields[index].NumericValue);
        if (int.TryParse(_missionSeed.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int missionSeed))
            values.MiniMissionSeed = missionSeed;
        for (int index = 0; index < values.BuildingStates.Length; index++)
            values.BuildingStates[index] = ConvertToInt(_buildingValues[index].NumericValue);
        SettlementLogic.SaveSettlementData(settlement, values);
        SavePerks(settlement);
        SaveProduction(settlement);
    }

    private JsonObject? GetSelectedSettlement(JsonObject? saveData = null)
    {
        JsonArray? settlements = saveData?.GetObject("PlayerStateData")?.GetArray("SettlementStatesV2") ?? _settlements;
        int selectorIndex = _settlementSelector.SelectedIndex;
        if (settlements is null || selectorIndex < 0 || selectorIndex >= _filteredIndices.Count)
            return null;
        int dataIndex = _filteredIndices[selectorIndex];
        return dataIndex >= 0 && dataIndex < settlements.Length ? settlements.GetObject(dataIndex) : null;
    }

    private void OnSettlementSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
            LoadSelectedSettlement();
    }

    private async void OnDelete(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_settlements is null || _settlementSelector.SelectedIndex < 0)
            return;
        if (!await _desktop.ConfirmAsync(UiStrings.Get("settlement.delete_title"), UiStrings.Get("settlement.delete_confirm")))
            return;
        int selector = _settlementSelector.SelectedIndex;
        int dataIndex = _filteredIndices[selector];
        SettlementLogic.RemoveSettlement(_settlements, dataIndex);
        _filteredIndices.RemoveAt(selector);
        for (int index = 0; index < _filteredIndices.Count; index++)
            if (_filteredIndices[index] > dataIndex) _filteredIndices[index]--;
        _settlementNames.RemoveAt(selector);
        _settlementSelector.SelectedIndex = _settlementNames.Count == 0 ? -1 : Math.Min(selector, _settlementNames.Count - 1);
        RaiseModified();
    }

    private async void OnExport(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        JsonObject? settlement = GetSelectedSettlement();
        if (settlement is null)
            return;
        ExportConfig config = ExportConfig.Instance;
        string fileName = ExportConfig.BuildFileName(config.SettlementTemplate, config.SettlementExt, new()
        {
            ["settlement_name"] = _nameField.Text ?? string.Empty,
            ["seed"] = _seedField.Text ?? string.Empty,
        });
        string? path = await _desktop.PickSaveFileAsync(new(
            UiStrings.Get("common.export"), fileName, config.SettlementExt, ".json"));
        if (string.IsNullOrWhiteSpace(path))
            return;
        try { settlement.ExportToFile(path); }
        catch (Exception ex)
        {
            await _desktop.ShowErrorAsync(UiStrings.Get("common.error"), UiStrings.Format("settlement.export_failed", ex.Message));
        }
    }

    private async void OnImport(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_settlements is null)
            return;
        ExportConfig config = ExportConfig.Instance;
        string? path = await _desktop.PickOpenFileAsync(new(
            UiStrings.Get("common.import"), string.Empty, config.SettlementExt, ".stl", ".json"));
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            JsonObject imported = InventoryImportHelper.UnwrapNomNom(JsonObject.ImportFromFile(path), "Settlement");
            int selectedData = _settlementSelector.SelectedIndex >= 0 ? _filteredIndices[_settlementSelector.SelectedIndex] : -1;
            int target = SettlementLogic.FindImportTargetIndex(_settlements, selectedData);
            if (target == -2)
            {
                if (TopLevel.GetTopLevel(this) is not Window owner)
                    return;
                target = await new SettlementSlotPickerDialog(_settlements).ShowDialog<int>(owner);
                if (target < 0)
                    return;
            }
            if (target == -1)
            {
                _settlements.Add(imported);
                int newIndex = _settlements.Length - 1;
                _filteredIndices.Add(newIndex);
                _settlementNames.Add($"[{newIndex}] {imported.GetString("Name") ?? UiStrings.Format("settlement.fallback_name", newIndex + 1)}");
                _settlementSelector.SelectedIndex = _settlementNames.Count - 1;
            }
            else
            {
                JsonObject targetSettlement = _settlements.GetObject(target);
                foreach (string name in imported.Names())
                    targetSettlement.Set(name, imported.Get(name));
                int selectorIndex = _filteredIndices.IndexOf(target);
                if (selectorIndex >= 0)
                    _settlementSelector.SelectedIndex = selectorIndex;
                LoadSelectedSettlement();
            }
            RaiseModified();
        }
        catch (Exception ex)
        {
            await _desktop.ShowErrorAsync(UiStrings.Get("common.error"), UiStrings.Format("settlement.import_failed", ex.Message));
        }
    }

    private void LoadPerks(JsonArray? perks)
    {
        for (int slot = 0; slot < PerkSlotCount; slot++)
        {
            string raw = perks is not null && slot < perks.Length ? perks.GetString(slot) ?? string.Empty : string.Empty;
            string id = raw;
            string seed = string.Empty;
            int hash = raw.IndexOf('#');
            if (hash >= 0)
            {
                id = raw[..hash];
                seed = raw[(hash + 1)..];
            }
            PerkChoice? choice = (_perkCombos[slot].ItemsSource as IEnumerable<PerkChoice>)?
                .FirstOrDefault(item => string.Equals(item.Perk?.Id, id, StringComparison.Ordinal));
            _perkCombos[slot].SelectedItem = choice ?? (_perkCombos[slot].ItemsSource as IEnumerable<PerkChoice>)?.FirstOrDefault();
            _perkSeeds[slot].Text = seed;
        }
    }

    private void SavePerks(JsonObject settlement)
    {
        JsonArray perks = settlement.GetArray("Perks") ?? new JsonArray();
        if (settlement.GetArray("Perks") is null)
            settlement.Set("Perks", perks);
        for (int slot = 0; slot < PerkSlotCount; slot++)
        {
            SettlementPerk? perk = (_perkCombos[slot].SelectedItem as PerkChoice)?.Perk;
            string value = "^";
            if (perk is not null)
            {
                value = perk.Id;
                string seed = _perkSeeds[slot].Text?.Trim() ?? string.Empty;
                if (perk.Procedural && int.TryParse(seed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    value += "#" + seed;
            }
            if (slot < perks.Length) perks.Set(slot, value); else perks.Add(value);
        }
        while (perks.Length > 0 && perks.GetString(perks.Length - 1) == "^")
            perks.RemoveAt(perks.Length - 1);
    }

    private void OnPerkChanged(int slot)
    {
        SettlementPerk? perk = (_perkCombos[slot].SelectedItem as PerkChoice)?.Perk;
        _perkSeedPanels[slot].IsVisible = perk?.Procedural == true;
        if (perk?.Procedural != true)
            _perkSeeds[slot].Text = string.Empty;
        RaiseModified();
    }

    private void LoadProduction(JsonArray? production)
    {
        _productionRows.Clear();
        if (production is null)
            return;
        for (int index = 0; index < production.Length; index++)
        {
            JsonObject item = production.GetObject(index);
            string elementId = item.GetString("ElementId") ?? item.Get("ElementId")?.ToString() ?? string.Empty;
            string lookup = elementId.TrimStart('^');
            int amount = 0;
            try { amount = item.GetInt("Amount"); } catch { }
            var row = new ProductionRow(
                _resources.Items.GetItem(lookup)?.Name ?? lookup,
                elementId,
                amount,
                _resources.Icons?.GetIconForItem(lookup, _resources.Items));
            row.Changed += (_, _) => RaiseModified();
            _productionRows.Add(row);
        }
    }

    private void SaveProduction(JsonObject settlement)
    {
        JsonArray? production = settlement.GetArray("ProductionState");
        if (production is null)
            return;
        for (int index = 0; index < _productionRows.Count && index < production.Length; index++)
        {
            ProductionRow row = _productionRows[index];
            JsonObject value = production.GetObject(index);
            value.Set("ElementId", row.ElementId);
            RawNumberGuard.SetInt(value, "Amount", row.Amount == row.RawAmount
                ? row.RawAmount
                : Math.Clamp(row.Amount, 0, SettlementLogic.ProductionMaxAmount));
        }
    }

    private async Task ChangeProductionElementAsync(ProductionRow row)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;
        string? id = await new SettlementProductionPickerDialog(_resources, _allowedProduction).ShowDialog<string?>(owner);
        if (string.IsNullOrWhiteSpace(id))
            return;
        row.ElementId = id.StartsWith('^') ? id : "^" + id;
        row.ItemName = _resources.Items.GetItem(id)?.Name ?? id;
        row.Icon = _resources.Icons?.GetIconForItem(id, _resources.Items);
    }

    private void OnBuildingComboChanged(int slot)
    {
        if (_editorUpdating || _buildingCombos[slot].SelectedItem is not BuildingChoice choice)
            return;
        if (!choice.Value.HasValue)
            return;
        SetBuildingValue(slot, choice.Value.Value);
        RaiseModified();
    }

    private void OnBuildingValueChanged(int slot)
    {
        if (_editorUpdating)
            return;
        int value = ConvertToInt(_buildingValues[slot].NumericValue);
        SetBuildingValue(slot, value);
        if (ConvertToInt(_editorSlot.NumericValue) - 1 == slot)
            LoadEditorFromSlot();
        RaiseModified();
    }

    private void SetBuildingValue(int slot, int value)
    {
        _editorUpdating = true;
        try
        {
            _buildingValues[slot].NumericValue = value;
            _buildingDescriptions[slot].Text = SettlementLogic.SettlementBuildingState.GetBuildingSlotDescription(value);
            _buildingCombos[slot].SelectedItem = (_buildingCombos[slot].ItemsSource as IEnumerable<BuildingChoice>)?
                .FirstOrDefault(choice => choice.Value == value)
                ?? (_buildingCombos[slot].ItemsSource as IEnumerable<BuildingChoice>)?.LastOrDefault();
        }
        finally { _editorUpdating = false; }
    }

    private void LoadEditorFromSlot()
    {
        int slot = ConvertToInt(_editorSlot.NumericValue) - 1;
        if (slot < 0 || slot >= _buildingValues.Length)
            return;
        int value = ConvertToInt(_buildingValues[slot].NumericValue);
        _editorUpdating = true;
        try
        {
            _editorRaw.NumericValue = value;
            UpdateEditorBits(value);
            UpdateEditorDescription(value);
        }
        finally { _editorUpdating = false; }
    }

    private void OnEditorRawChanged()
    {
        if (_editorUpdating)
            return;
        int value = ConvertToInt(_editorRaw.NumericValue);
        _editorUpdating = true;
        try
        {
            UpdateEditorBits(value);
            UpdateEditorDescription(value);
        }
        finally { _editorUpdating = false; }
    }

    private void OnEditorBitsChanged()
    {
        if (_editorUpdating)
            return;
        int value = ComputeEditorValue();
        _editorUpdating = true;
        try
        {
            _editorRaw.NumericValue = value;
            UpdateEditorDescription(value);
        }
        finally { _editorUpdating = false; }
    }

    private void ApplyEditorToSlot()
    {
        int slot = ConvertToInt(_editorSlot.NumericValue) - 1;
        if (slot < 0 || slot >= _buildingValues.Length)
            return;
        SetBuildingValue(slot, ComputeEditorValue());
        RaiseModified();
    }

    private int ComputeEditorValue()
    {
        int value = 0;
        SetBits(_editorInitial, 0);
        SetBits(_editorUpgrade, 10);
        SetBits(_editorTier, 20);
        SetBits(_editorFlags, 26);
        return value;

        void SetBits(CheckBox[] boxes, int start)
        {
            for (int index = 0; index < boxes.Length; index++)
                if (boxes[index].IsChecked == true) value |= 1 << (start + index);
        }
    }

    private void UpdateEditorBits(int value)
    {
        Apply(_editorInitial, 0);
        Apply(_editorUpgrade, 10);
        Apply(_editorTier, 20);
        Apply(_editorFlags, 26);
        void Apply(CheckBox[] boxes, int start)
        {
            for (int index = 0; index < boxes.Length; index++)
                boxes[index].IsChecked = ((value >> (start + index)) & 1) != 0;
        }
    }

    private void UpdateEditorDescription(int value)
    {
        if (SettlementLogic.SettlementBuildingState.IsEmpty(value))
        {
            _editorClass.Text = "-";
            _editorState.Text = UiStrings.Get("settlement.bs_empty");
            return;
        }
        (string classKey, string stateKey) = SettlementLogic.SettlementBuildingState.DetermineClassAndState(value);
        _editorClass.Text = UiStrings.Get(classKey);
        _editorState.Text = UiStrings.Get(stateKey);
    }

    private void RefreshRaceChoices()
    {
        string? selected = (_raceField.SelectedItem as RaceChoice)?.Id;
        var choices = SettlementLogic.AlienRaces.Select(id => new RaceChoice(
            id,
            SettlementLogic.AlienRaceLocKeys.TryGetValue(id, out string? key)
                ? UiStrings.Get(key)
                : SettlementLogic.AlienRaceDisplayNames.GetValueOrDefault(id, id))).ToList();
        _raceField.ItemsSource = choices;
        SelectRace(selected ?? "None");
    }

    private void SelectRace(string id) => _raceField.SelectedItem =
        (_raceField.ItemsSource as IEnumerable<RaceChoice>)?.FirstOrDefault(choice =>
            string.Equals(choice.Id, id, StringComparison.OrdinalIgnoreCase));

    private void RefreshDecisionChoices()
    {
        int selected = _decisionType.SelectedIndex;
        _decisionType.ItemsSource = SettlementLogic.DecisionTypes.Select(type =>
            SettlementLogic.DecisionTypeLocKeys.TryGetValue(type, out string? key) ? UiStrings.Get(key) : type).ToList();
        _decisionType.SelectedIndex = selected >= 0 ? selected : 0;
    }

    private void RefreshPerkChoices()
    {
        List<PerkChoice> choices = [new(null), .. SettlementDatabase.Perks.Select(perk => new PerkChoice(perk))];
        for (int slot = 0; slot < _perkCombos.Length; slot++)
        {
            string? selected = (_perkCombos[slot].SelectedItem as PerkChoice)?.Perk?.Id;
            _perkCombos[slot].ItemsSource = choices;
            _perkCombos[slot].SelectedItem = choices.FirstOrDefault(choice => choice.Perk?.Id == selected) ?? choices[0];
        }
    }

    private void RefreshBuildingChoices()
    {
        List<BuildingChoice> choices =
        [
            .. SettlementDatabase.KnownMilestones.Select(item => new BuildingChoice(
                item.Value, $"{item.Value} - {UiStrings.Get(item.LocKey)}")),
            new(null, UiStrings.Get("settlement.bs_custom")),
        ];
        for (int slot = 0; slot < _buildingCombos.Length; slot++)
        {
            int value = ConvertToInt(_buildingValues[slot].NumericValue);
            _buildingCombos[slot].ItemsSource = choices;
            _buildingCombos[slot].SelectedItem = choices.FirstOrDefault(choice => choice.Value == value) ?? choices[^1];
        }
    }

    private void ClearFields()
    {
        _nameField.Text = string.Empty;
        _seedField.Text = string.Empty;
        _rawStats = null;
        foreach (InvariantNumericTextBox stat in _statFields.Where(field => field is not null)) stat.NumericValue = 0;
        _populationField.NumericValue = 0;
        _populationField.IsEnabled = false;
        _hasPopulation = false;
        _rawPopulation = null;
        _decisionType.SelectedIndex = -1;
        foreach (OptionalTimestampEditor timestamp in new[]
                 { _lastDecision, _lastBugAttack, _lastAlert, _lastDebt, _lastUpkeep, _lastPopulation, _missionStart })
            timestamp.Clear();
        _missionSeed.Text = string.Empty;
        for (int slot = 0; slot < _perkCombos.Length; slot++)
        {
            if (_perkCombos[slot] is not null) _perkCombos[slot].SelectedIndex = 0;
            if (_perkSeeds[slot] is not null) _perkSeeds[slot].Text = string.Empty;
        }
        _productionRows.Clear();
        _rawBuildingStates = null;
        _hasBuildingStates = false;
        for (int slot = 0; slot < _buildingValues.Length; slot++)
            if (_buildingValues[slot] is not null) SetBuildingValue(slot, 0);
    }

    private void UpdateSelectedName()
    {
        int selector = _settlementSelector.SelectedIndex;
        if (selector < 0 || selector >= _filteredIndices.Count)
            return;
        int data = _filteredIndices[selector];
        string name = string.IsNullOrWhiteSpace(_nameField.Text)
            ? UiStrings.Format("settlement.fallback_name", data + 1)
            : _nameField.Text;
        _settlementNames[selector] = $"[{data}] {name}";
        RaiseModified();
    }

    private void UpdateStatWarning(int index)
    {
        int value = ConvertToInt(_statFields[index].NumericValue);
        _statWarnings[index].IsVisible = value < SettlementLogic.StatMinValues[index] || value > SettlementLogic.StatMaxValues[index];
        _statWarnings[index].Foreground = value < SettlementLogic.StatMinValues[index] ? Brushes.Crimson : Brushes.Tomato;
    }

    private void UpdatePopulationWarning()
    {
        int value = ConvertToInt(_populationField.NumericValue);
        _populationWarning.IsVisible = value < 0 || value > SettlementLogic.PopulationSoftMax;
        _populationWarning.Foreground = value < 0 ? Brushes.Crimson : Brushes.Tomato;
    }

    private void GoToSelectedJson()
    {
        int selector = _settlementSelector.SelectedIndex;
        if (selector >= 0 && selector < _filteredIndices.Count)
            GoToJsonRequested?.Invoke(this,
                new GoToJsonRequestEventArgs($"PlayerStateData.SettlementStatesV2[{_filteredIndices[selector]}]"));
    }

    private void AddTimestampRow(Panel panel, string key, OptionalTimestampEditor editor)
    {
        editor.ValueChanged += (_, _) => RaiseModified();
        AddRow(panel, key, editor);
    }

    private void AddRow(Panel panel, string labelKey, Control field) => AddRow(panel, CreateLocalisedLabel(labelKey), field);

    private static void AddRow(Panel panel, Control label, Control field)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*"), ColumnSpacing = 8 };
        row.Children.Add(label);
        row.Children.Add(field);
        Grid.SetColumn(field, 1);
        panel.Children.Add(row);
    }

    private TextBlock CreateLocalisedLabel(string key)
    {
        var label = new TextBlock { Text = UiStrings.Get(key), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        _localised.Add((label, key));
        return label;
    }

    private static Grid WithButton(Control field, Button button)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 5, Children = { field, button } };
        Grid.SetColumn(button, 1);
        return grid;
    }

    private static Grid WithWarning(Control field, TextBlock warning)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 5, Children = { field, warning } };
        Grid.SetColumn(warning, 1);
        return grid;
    }

    private static TextBlock CreateLimitWarning() => new()
    {
        Text = "⚠",
        IsVisible = false,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock CreateWarningLabel() => new()
    {
        Foreground = Brushes.DarkOrange,
        FontWeight = FontWeight.SemiBold,
        TextWrapping = TextWrapping.Wrap,
    };

    private void RaiseModified()
    {
        if (!_loading)
            DataModified?.Invoke(this, EventArgs.Empty);
    }

    private static int ConvertToInt(double? value) => value switch
    {
        null => 0,
        <= int.MinValue => int.MinValue,
        >= int.MaxValue => int.MaxValue,
        _ => (int)value.Value,
    };

    private sealed record PerkChoice(SettlementPerk? Perk)
    {
        public override string ToString() => Perk is null ? "(None)" : $"{Perk.Name} ({Perk.StatEffectSummary})";
    }

    private sealed record RaceChoice(string Id, string Name)
    {
        public override string ToString() => $"{Name} ({Id})";
    }

    private sealed record BuildingChoice(int? Value, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed class ProductionRow : INotifyPropertyChanged
    {
        private string _itemName;
        private string _elementId;
        private int _amount;
        private Bitmap? _icon;

        internal ProductionRow(string itemName, string elementId, int amount, Bitmap? icon)
        {
            _itemName = itemName;
            _elementId = elementId;
            _amount = amount;
            RawAmount = amount;
            _icon = icon;
        }

        public string ItemName { get => _itemName; set => SetField(ref _itemName, value); }
        public string ElementId { get => _elementId; set => SetField(ref _elementId, value); }
        public int Amount { get => _amount; set => SetField(ref _amount, value); }
        public int RawAmount { get; }
        public Bitmap? Icon { get => _icon; set => SetField(ref _icon, value); }
        public event PropertyChangedEventHandler? PropertyChanged;
        internal event EventHandler? Changed;

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
