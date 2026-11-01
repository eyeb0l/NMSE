using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Config;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Contracts;
using NMSE.UI.Avalonia.Controls;

namespace NMSE.UI.Avalonia.Panels.Companions;

/// <summary>
/// Complete editor for pet, egg, accessory, and pet-battle save state. Values are written
/// directly to the loaded tree, matching the established editor's selection-safe behaviour.
/// </summary>
public sealed partial class CompanionPanel : UserControl, ISaveEditorPanel, IGoToJsonSource
{
    private const string BuilderUrl = "https://creature.nmscd.com/#/builder";
    private static readonly string[] ClassItems = ["C", "B", "A", "S"];
    private static readonly string[] RestrictedMoveIds = ["REVIVE"];

    private readonly CompanionDesktopActions _desktop;
    private readonly ObservableCollection<CompanionSlot> _entries = [];
    private readonly ObservableCollection<CompanionSlot> _petEntries = [];
    private readonly ObservableCollection<CompanionSlot> _eggEntries = [];
    private readonly ListBox _eggList = new() { MinWidth = 230, SelectionMode = SelectionMode.Multiple };
    private readonly CheckBox _showLockedSlots = new() { IsChecked = AppConfig.Instance.CompanionShowLockedSlots };
    private readonly TextBlock _petSummary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _eggSummary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _unlockSlot = new();
    private readonly TextBlock _lockedHint = new() { TextWrapping = TextWrapping.Wrap };
    private readonly List<(Button Button, string Key)> _listButtons = [];
    private bool _activeIsPet = true;
    private readonly Random _random = new();
    private readonly Dictionary<string, TextBlock> _labels = new(StringComparer.Ordinal);
    private readonly List<Button> _seedButtons = [];
    private readonly List<DescriptorEditor> _descriptorEditors = [];
    private readonly List<PetBattleMoveEntry>[] _allowedMoves = new List<PetBattleMoveEntry>[5];

    private readonly TextBlock _title;
    private readonly ListBox _companionList;
    private readonly Grid _details;
    private readonly Button _deleteButton;
    private readonly Button _importButton;
    private readonly Button _exportButton;
    private readonly Button _builderButton;
    private readonly Button _goPetsButton;
    private readonly Button _goSelectedPetButton;
    private readonly Button _goEggsButton;
    private readonly Button _goSelectedEggButton;
    private readonly Button _goBattleTeamButton;
    private readonly TabControl _tabs;
    private readonly TabItem _statsTab;
    private readonly TabItem _battleTab;

    private readonly ComboBox _type = new();
    private readonly TextBox _name = new();
    private readonly TextBox _creatureSeed = new();
    private readonly TextBox _secondarySeed = new();
    private readonly TextBox _speciesSeed = new();
    private readonly TextBox _genusSeed = new();
    private readonly ComboBox _biome = new();
    private readonly ComboBox _creatureType = new();
    private readonly CheckBox _predator = new();
    private readonly CheckBox _hasFur = new();
    private readonly InvariantNumericTextBox _scale = new();
    private readonly InvariantNumericTextBox _trust = new();
    private readonly TextBox _boneScaleSeed = new();
    private readonly TextBox _colourBaseSeed = new();
    private readonly InvariantNumericTextBox[] _traits = [new(), new(), new()];
    private readonly InvariantNumericTextBox[] _moods = [new(), new()];
    private readonly CheckBox _unlocked = new();
    private readonly TextBox _birthTime = new();
    private readonly TextBox _lastEggTime = new();
    private readonly TextBox _lastTrustIncrease = new();
    private readonly TextBox _lastTrustDecrease = new();
    private readonly TextBox _customSpeciesName = new();
    private readonly CheckBox _eggModified = new();
    private readonly CheckBox _summoned = new();
    private readonly CheckBox _allowReroll = new();
    private readonly TextBox _ua = new();
    private readonly CheckBox _uaHex = new();
    private readonly StackPanel _descriptorPanel = new() { Spacing = 4 };
    private readonly TextBlock _noDescriptorData = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _regenerateDescriptor = new();
    private readonly Button _induceEgg = new();
    private readonly Button _placeEgg = new();
    private readonly Button _makeHatchable = new();

    private readonly Grid _accessoryGrid;
    private readonly TextBlock _noAccessories = new() { Foreground = Brushes.Gray };
    private readonly TextBlock[] _accessorySlotLabels = new TextBlock[3];
    private readonly ComboBox[] _accessoryCombos = new ComboBox[3];
    private readonly TextBlock[] _accessoryDescriptors = new TextBlock[3];
    private readonly Border[] _primaryColours = new Border[3];
    private readonly Border[] _alternateColours = new Border[3];
    private readonly InvariantNumericTextBox[] _accessoryScales = new InvariantNumericTextBox[3];
    private readonly TextBlock[] _accessoryScaleLabels = new TextBlock[3];
    private readonly Button[] _accessoryResets = new Button[3];
    private AccessorySlot[] _accessoryLayout = [];

    private readonly ComboBox[] _battleTeam = new ComboBox[3];
    private readonly CheckBox _overrideClasses = new();
    private readonly ComboBox[] _classes = [new(), new(), new()];
    private readonly TextBlock _averageClass = new();
    private readonly InvariantNumericTextBox _victories = new() { Minimum = 0, Maximum = 999999 };
    private readonly InvariantNumericTextBox _mutationProgress = new();
    private readonly InvariantNumericTextBox _genesAvailable = new() { Minimum = 0, Maximum = 1000 };
    private readonly InvariantNumericTextBox[] _treats =
    [
        new() { Minimum = 0, Maximum = 10 },
        new() { Minimum = 0, Maximum = 10 },
        new() { Minimum = 0, Maximum = 10 },
    ];
    private readonly TextBlock _genesLevel = new();
    private readonly TextBlock _affinity = new();
    private readonly TextBlock _weak = new();
    private readonly TextBlock _strong = new();
    private readonly ComboBox[] _moveCombos = new ComboBox[5];
    private readonly TextBlock[] _moveSets = new TextBlock[5];
    private readonly TextBlock[] _moveDetails = new TextBlock[5];

    private JsonObject? _saveData;
    private JsonObject? _playerState;
    private bool _loading;
    private bool _movesInitialised;

    public CompanionPanel(CompanionDesktopActions desktop)
    {
        _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
        _title = new TextBlock
        {
            FontFamily = Services.FontManager.NmsFont,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _companionList = new ListBox { ItemsSource = _petEntries, MinWidth = 230, SelectionMode = SelectionMode.Multiple };
        _eggList.ItemsSource = _eggEntries;
        _companionList.SelectionChanged += (_, e) => SelectList(true, e);
        _eggList.SelectionChanged += (_, e) => SelectList(false, e);
        _showLockedSlots.IsCheckedChanged += (_, _) =>
        {
            AppConfig.Instance.CompanionShowLockedSlots = _showLockedSlots.IsChecked == true;
            AppConfig.Instance.Save(); RebuildSlotLabels(); LoadSelectedCompanion();
        };
        _unlockSlot.Click += (_, _) =>
        {
            if (_playerState == null || SelectedEntry is not { Source: "Pet" } entry) return;
            CompanionLogic.SetSlotUnlocked(_playerState, entry.OriginalIndex, true);
            RebuildSlotLabels(); LoadBattleTeam(); LoadSelectedCompanion(); Modified();
        };
        _deleteButton = ActionButton(OnDeleteAsync);
        _importButton = ActionButton(OnImportAsync);
        _exportButton = ActionButton(OnExportAsync);
        _builderButton = ActionButton(async () => await _desktop.OpenExternalUrlAsync(BuilderUrl));
        _goPetsButton = JsonButton("PlayerStateData.Pets");
        _goSelectedPetButton = JsonButton(() => SelectedEntry is { Source: "Pet" } item
            ? $"PlayerStateData.Pets[{item.OriginalIndex}]" : null);
        _goEggsButton = JsonButton("PlayerStateData.Eggs");
        _goSelectedEggButton = JsonButton(() => SelectedEntry is { Source: "Egg" } item
            ? $"PlayerStateData.Eggs[{item.OriginalIndex}]" : null);
        _goBattleTeamButton = JsonButton("PlayerStateData.PetBattleTeam");

        _statsTab = new TabItem();
        _battleTab = new TabItem();
        _accessoryGrid = BuildAccessoryGrid();
        _statsTab.Content = BuildStatsTab();
        _battleTab.Content = BuildBattleTab();
        _tabs = new TabControl
        {
            MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Items = { _statsTab, _battleTab },
        };
        _details = new Grid { IsVisible = false, MinHeight = 0, ClipToBounds = true, Children = { _tabs } };

        var headerButtons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _goPetsButton, _goSelectedPetButton, _goEggsButton, _goSelectedEggButton, _goBattleTeamButton },
        };
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { _title, headerButtons },
        };
        Grid.SetColumn(headerButtons, 1);

        var listActions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _exportButton, _importButton, _deleteButton },
        };
        var left = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto,*,Auto,Auto,Auto"),
            RowSpacing = 6,
            Children = { _petSummary, _showLockedSlots, _companionList, BuildListActions(true), _eggSummary, _eggList, BuildListActions(false), listActions, new StackPanel { Children = { _lockedHint, _unlockSlot } } },
        };
        for (int i = 0; i < left.Children.Count; i++) Grid.SetRow(left.Children[i], i);
        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("240,*"),
            ColumnSpacing = 10,
            MinHeight = 0,
            ClipToBounds = true,
            Children = { left, _details },
        };
        Grid.SetColumn(_details, 1);
        Content = new Grid
        {
            Margin = new Thickness(10),
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 8,
            Children = { header, body },
        };
        Grid.SetRow(body, 1);

        WireGeneralEditors();
        ApplyUiLocalisation();
    }

    public event EventHandler? DataModified;
    public event EventHandler<GoToJsonRequestEventArgs>? GoToJsonRequested;
    public event EventHandler<CompanionExosuitCargoModifiedEventArgs>? ExosuitCargoModified;

    public ListBox CompanionList => _companionList;
    public ListBox EggList => _eggList;
    public TabControl EditorTabs => _tabs;
    public TabItem StatsTab => _statsTab;
    public TabItem BattleTab => _battleTab;

    private static TextBlock Label(string text = "") => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
    };

    private TextBlock LocalLabel(string key)
    {
        var label = Label();
        _labels[key] = label;
        return label;
    }

    private static TextBlock Heading(string text = "") => new()
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 8, 0, 3),
    };

    private static Button ActionButton(Func<Task> action)
    {
        var button = new Button { Margin = new Thickness(2) };
        button.Click += async (_, _) => await action();
        return button;
    }

    private Button JsonButton(string path) => JsonButton(() => path);

    private Button JsonButton(Func<string?> path)
    {
        var button = new Button { Content = "{}", Margin = new Thickness(2), MinWidth = 34 };
        button.Click += (_, _) =>
        {
            string? target = path();
            if (!string.IsNullOrEmpty(target))
                GoToJsonRequested?.Invoke(this, new GoToJsonRequestEventArgs(target));
        };
        return button;
    }

    private static void AddRow(Grid grid, int row, Control label, Control editor)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(label);
        grid.Children.Add(editor);
        Grid.SetRow(label, row);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);
    }

    private static Grid FormGrid() => new()
    {
        ColumnDefinitions = new ColumnDefinitions("190,*"),
        ColumnSpacing = 8,
        RowSpacing = 5,
    };

    private static void WireCommit(TextBox box, Action commit)
    {
        box.LostFocus += (_, _) => commit();
        box.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            commit();
            args.Handled = true;
        };
    }

    private Grid SeedEditor(TextBox box, Action commit)
    {
        var generate = new Button { MinWidth = 44 };
        generate.Click += (_, _) =>
        {
            Span<byte> bytes = stackalloc byte[8];
            _random.NextBytes(bytes);
            box.Text = "0x" + Convert.ToHexString(bytes);
            commit();
        };
        _seedButtons.Add(generate);
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 5,
            Children = { box, generate },
        };
        Grid.SetColumn(generate, 1);
        return grid;
    }

    private Control BuildStatsTab()
    {
        _type.ItemsSource = CompanionDatabase.Entries;
        _biome.ItemsSource = CompanionDatabase.BiomeTypes;
        _creatureType.ItemsSource = CompanionDatabase.CreatureTypes;
        _uaHex.Content = "Hex";

        var left = FormGrid();
        int row = 0;
        AddRow(left, row++, LocalLabel("slot_unlocked"), _unlocked);
        AddRow(left, row++, LocalLabel("species"), _type);
        AddRow(left, row++, LocalLabel("name"), _name);
        AddRow(left, row++, LocalLabel("biome"), _biome);
        AddRow(left, row++, LocalLabel("type"), _creatureType);
        AddRow(left, row++, LocalLabel("predator"), _predator);
        AddRow(left, row++, LocalLabel("has_fur"), _hasFur);
        AddRow(left, row++, LocalLabel("scale"), _scale);
        AddRow(left, row++, LocalLabel("trust"), _trust);
        AddRow(left, row++, LocalLabel("birth_time"), _birthTime);
        AddRow(left, row++, LocalLabel("last_egg_time"), _lastEggTime);
        AddRow(left, row++, LocalLabel("custom_species_name"), _customSpeciesName);
        AddRow(left, row++, LocalLabel("egg_modified"), _eggModified);
        AddRow(left, row++, LocalLabel("summoned"), _summoned);
        AddRow(left, row++, LocalLabel("allow_reroll"), _allowReroll);
        var uaRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { _ua, _uaHex } };
        Grid.SetColumn(_uaHex, 1);
        AddRow(left, row++, LocalLabel("ua"), uaRow);

        var right = FormGrid();
        row = 0;
        AddRow(right, row++, LocalLabel("creature_seed"), SeedEditor(_creatureSeed, WriteCreatureSeed));
        AddRow(right, row++, LocalLabel("secondary_seed"), SeedEditor(_secondarySeed, WriteSecondarySeed));
        AddRow(right, row++, LocalLabel("species_seed"), SeedEditor(_speciesSeed, WriteSpeciesSeed));
        AddRow(right, row++, LocalLabel("genus_seed"), SeedEditor(_genusSeed, WriteGenusSeed));
        AddRow(right, row++, LocalLabel("bone_scale_seed"), SeedEditor(_boneScaleSeed, WriteBoneScaleSeed));
        AddRow(right, row++, LocalLabel("colour_base_seed"), SeedEditor(_colourBaseSeed, WriteColourBaseSeed));
        AddRow(right, row++, LocalLabel("helpfulness"), _traits[0]);
        AddRow(right, row++, LocalLabel("aggression"), _traits[1]);
        AddRow(right, row++, LocalLabel("independence"), _traits[2]);
        AddRow(right, row++, LocalLabel("hungry"), _moods[0]);
        AddRow(right, row++, LocalLabel("lonely"), _moods[1]);
        AddRow(right, row++, LocalLabel("trust_increase"), _lastTrustIncrease);
        AddRow(right, row++, LocalLabel("trust_decrease"), _lastTrustDecrease);

        var columns = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 18,
            Children = { left, right },
        };
        Grid.SetColumn(right, 1);

        var descriptorHeader = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { LocalLabel("descriptors"), _builderButton, _regenerateDescriptor },
        };
        _labels["descriptors"].FontSize = 15;
        _labels["descriptors"].FontWeight = FontWeight.SemiBold;
        _descriptorPanel.Children.Add(_noDescriptorData);

        var accessorySection = new StackPanel { Spacing = 4 };
        var accessoryHeading = LocalLabel("accessory_customisation");
        accessoryHeading.FontSize = 15;
        accessoryHeading.FontWeight = FontWeight.SemiBold;
        accessorySection.Children.Add(accessoryHeading);
        accessorySection.Children.Add(_noAccessories);
        accessorySection.Children.Add(_accessoryGrid);

        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _induceEgg, _makeHatchable, _placeEgg },
        };
        var content = new StackPanel { Spacing = 7 };
        content.Children.Add(LocalLabel("stats_warning"));
        content.Children.Add(actions);
        content.Children.Add(columns);
        content.Children.Add(descriptorHeader);
        content.Children.Add(_descriptorPanel);
        content.Children.Add(accessorySection);
        return new ScrollViewer
        {
            MinHeight = 0,
            Padding = new Thickness(8, 8, 18, 30),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ClipToBounds = true,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = content,
        };
    }

    private Grid BuildAccessoryGrid()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,2*,Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 6,
            RowSpacing = 4,
        };
        for (int index = 0; index < 3; index++)
        {
            int slot = index;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            _accessorySlotLabels[index] = Label();
            _accessoryCombos[index] = new ComboBox { MinWidth = 210 };
            _accessoryDescriptors[index] = new TextBlock
            {
                Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _primaryColours[index] = ColourSwatch();
            _alternateColours[index] = ColourSwatch();
            _accessoryScales[index] = new InvariantNumericTextBox { MinWidth = 80 };
            _accessoryScaleLabels[index] = Label();
            _accessoryResets[index] = new Button();
            _accessoryCombos[index].SelectionChanged += (_, _) =>
            {
                if (!_loading) WriteAccessory(slot);
            };
            _primaryColours[index].PointerPressed += async (_, _) => await PickAccessoryColourAsync(slot, 0);
            _alternateColours[index].PointerPressed += async (_, _) => await PickAccessoryColourAsync(slot, 1);
            _accessoryScales[index].NumericValueChanged += (_, _) =>
            {
                if (!_loading) WriteAccessoryScale(slot);
            };
            _accessoryResets[index].Click += (_, _) => ResetAccessory(slot);

            Control[] controls =
            [
                _accessorySlotLabels[index], _accessoryCombos[index], _accessoryDescriptors[index],
                LabeledSwatch("P:", _primaryColours[index]), LabeledSwatch("A:", _alternateColours[index]),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 3,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { _accessoryScaleLabels[index], _accessoryScales[index] },
                },
                _accessoryResets[index],
            ];
            for (int column = 0; column < controls.Length; column++)
            {
                grid.Children.Add(controls[column]);
                Grid.SetRow(controls[column], index);
                Grid.SetColumn(controls[column], column);
            }
        }
        return grid;
    }

    private static Border ColourSwatch() => new()
    {
        Width = 22,
        Height = 22,
        BorderBrush = Brushes.Gray,
        BorderThickness = new Thickness(1),
        Background = Brushes.Transparent,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Control LabeledSwatch(string text, Border swatch) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 3,
        VerticalAlignment = VerticalAlignment.Center,
        Children = { Label(text), swatch },
    };

    private Control BuildBattleTab()
    {
        var content = new StackPanel { Spacing = 8 };
        var teamHeading = LocalLabel("battle_team");
        teamHeading.FontSize = 15;
        teamHeading.FontWeight = FontWeight.SemiBold;
        content.Children.Add(teamHeading);
        var team = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*,Auto,*"),
            ColumnSpacing = 5,
        };
        for (int index = 0; index < 3; index++)
        {
            int slot = index;
            _battleTeam[index] = new ComboBox { MinWidth = 150 };
            _battleTeam[index].SelectionChanged += (_, _) =>
            {
                if (!_loading) WriteBattleTeam(slot);
            };
            var label = LocalLabel($"battle_team_slot_{index}");
            team.Children.Add(label);
            team.Children.Add(_battleTeam[index]);
            Grid.SetColumn(label, index * 2);
            Grid.SetColumn(_battleTeam[index], index * 2 + 1);
        }
        content.Children.Add(team);
        _searchBattleSeed.Content = Get("companion.battle_search_seed", "Search Seed…");
        _resetBattleRecord.Content = Get("companion.battle_reset_record", "Reset battle record");
        _searchBattleSeed.Click += async (_, _) => await SearchBattleSeedAsync();
        _resetBattleRecord.Click += async (_, _) => await ResetBattleRecordAsync();
        content.Children.Add(_predictedBattleStats);
        content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _searchBattleSeed, _resetBattleRecord } });

        var classForm = FormGrid();
        int row = 0;
        AddRow(classForm, row++, LocalLabel("battle_override_classes"), _overrideClasses);
        AddRow(classForm, row++, LocalLabel("battle_average_class"), _averageClass);
        for (int index = 0; index < 3; index++)
        {
            _classes[index].ItemsSource = ClassItems;
            int capture = index;
            _classes[index].SelectionChanged += (_, _) =>
            {
                if (!_loading) WriteClasses();
            };
            AddRow(classForm, row++, LocalLabel($"battle_class_{capture}"), _classes[index]);
        }
        AddRow(classForm, row++, LocalLabel("battle_victories"), _victories);

        var genetics = FormGrid();
        row = 0;
        AddRow(genetics, row++, LocalLabel("battle_genes_level"), _genesLevel);
        AddRow(genetics, row++, LocalLabel("battle_mutation_progress"), _mutationProgress);
        AddRow(genetics, row++, LocalLabel("battle_genes_available"), _genesAvailable);
        AddRow(genetics, row++, LocalLabel("battle_treat_health"), _treats[0]);
        AddRow(genetics, row++, LocalLabel("battle_treat_agility"), _treats[1]);
        AddRow(genetics, row++, LocalLabel("battle_treat_combat"), _treats[2]);
        var top = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 18,
            Children = { classForm, genetics },
        };
        Grid.SetColumn(genetics, 1);
        content.Children.Add(top);

        var affinityRow = new WrapPanel { Orientation = Orientation.Horizontal };
        affinityRow.Children.Add(LocalLabel("battle_affinity"));
        affinityRow.Children.Add(_affinity);
        affinityRow.Children.Add(LocalLabel("battle_weak"));
        affinityRow.Children.Add(_weak);
        affinityRow.Children.Add(LocalLabel("battle_strong"));
        affinityRow.Children.Add(_strong);
        content.Children.Add(affinityRow);

        for (int index = 0; index < 5; index++)
        {
            int slot = index;
            _moveCombos[index] = new ComboBox { MinWidth = 340 };
            _moveSets[index] = new TextBlock { Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap };
            _moveDetails[index] = new TextBlock { TextWrapping = TextWrapping.Wrap };
            _moveCombos[index].SelectionChanged += (_, _) =>
            {
                if (!_loading) WriteMove(slot);
            };
            var moveRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("190,340,*"),
                ColumnSpacing = 8,
                Children = { LocalLabel($"move_slot_{index}"), _moveCombos[index], _moveDetails[index] },
            };
            Grid.SetColumn(_moveCombos[index], 1);
            Grid.SetColumn(_moveDetails[index], 2);
            var moveBlock = new StackPanel { Spacing = 2, Children = { moveRow, _moveSets[index] } };
            content.Children.Add(moveBlock);
        }

        return new ScrollViewer
        {
            MinHeight = 0,
            Padding = new Thickness(8, 8, 18, 30),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ClipToBounds = true,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = content,
        };
    }

    private void WireGeneralEditors()
    {
        _type.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            WriteType();
            RegenerateDescriptorTree(clearExisting: true);
            if (SelectedEntry is { } entry)
            {
                bool old = _loading;
                _loading = true;
                try { LoadAccessories(entry); }
                finally { _loading = old; }
            }
        };
        _biome.SelectionChanged += (_, _) => WriteNestedSelection("Biome", "Biome", _biome);
        _creatureType.SelectionChanged += (_, _) => WriteNestedSelection("CreatureType", "CreatureType", _creatureType);
        _predator.IsCheckedChanged += (_, _) => WriteBoolean("Predator", _predator);
        _hasFur.IsCheckedChanged += (_, _) => WriteBoolean("HasFur", _hasFur);
        _eggModified.IsCheckedChanged += (_, _) => WriteBoolean("EggModified", _eggModified);
        _summoned.IsCheckedChanged += (_, _) => WriteBoolean("HasBeenSummoned", _summoned);
        _allowReroll.IsCheckedChanged += (_, _) => WriteBoolean("AllowUnmodifiedReroll", _allowReroll);
        _unlocked.IsCheckedChanged += (_, _) => WriteUnlocked();
        _uaHex.IsCheckedChanged += (_, _) => ToggleUaMode();
        _scale.NumericValueChanged += (_, _) => WriteRawDouble("Scale", _scale);
        _trust.NumericValueChanged += (_, _) => WriteRawDouble("Trust", _trust);
        for (int index = 0; index < 3; index++)
        {
            int item = index;
            _traits[index].NumericValueChanged += (_, _) => WriteArrayDouble("Traits", item, _traits[item]);
        }
        for (int index = 0; index < 2; index++)
        {
            int item = index;
            _moods[index].NumericValueChanged += (_, _) => WriteArrayDouble("Moods", item, _moods[item]);
        }
        WireCommit(_name, WriteName);
        WireCommit(_creatureSeed, WriteCreatureSeed);
        WireCommit(_secondarySeed, WriteSecondarySeed);
        WireCommit(_speciesSeed, WriteSpeciesSeed);
        WireCommit(_genusSeed, WriteGenusSeed);
        WireCommit(_boneScaleSeed, WriteBoneScaleSeed);
        WireCommit(_colourBaseSeed, WriteColourBaseSeed);
        WireCommit(_customSpeciesName, WriteCustomSpeciesName);
        WireCommit(_ua, WriteUa);
        WireCommit(_birthTime, () => WriteTimestamp("BirthTime", _birthTime));
        WireCommit(_lastEggTime, () => WriteTimestamp("LastEggTime", _lastEggTime));
        WireCommit(_lastTrustIncrease, () => WriteTimestamp("LastTrustIncreaseTime", _lastTrustIncrease));
        WireCommit(_lastTrustDecrease, () => WriteTimestamp("LastTrustDecreaseTime", _lastTrustDecrease));
        _regenerateDescriptor.Click += (_, _) => WriteDescriptors(regenerateId: true);
        _makeHatchable.Click += (_, _) => MakeHatchable();
        _induceEgg.Click += async (_, _) => await InduceEggAsync();
        _placeEgg.Click += async (_, _) => await PlaceSelectedEggAsync();
        _overrideClasses.IsCheckedChanged += (_, _) => WriteOverrideClasses();
        _victories.NumericValueChanged += (_, _) => WriteInt("PetBattlerVictories", _victories);
        _mutationProgress.NumericValueChanged += (_, _) => WriteRawDouble("PetBattleProgressToTreat", _mutationProgress);
        _genesAvailable.NumericValueChanged += (_, _) => WriteInt("PetBattlerTreatsAvailable", _genesAvailable);
        foreach (var treat in _treats)
            treat.NumericValueChanged += (_, _) => WriteTreats();
    }

    public void RefreshSpeciesList()
    {
        _type.ItemsSource = null;
        _type.ItemsSource = CompanionDatabase.Entries;
    }

    public void LoadData(JsonObject saveData)
    {
        ArgumentNullException.ThrowIfNull(saveData);
        _saveData = saveData;
        _playerState = saveData.GetObject("PlayerStateData");
        _loading = true;
        try
        {
            _entries.Clear(); _petEntries.Clear(); _eggEntries.Clear();
            _details.IsVisible = false;
            if (_playerState is null) return;
            AddSlots(_playerState.GetArray("Pets"), "Pet");
            AddSlots(_playerState.GetArray("Eggs"), "Egg");
            LoadBattleTeam();
            _activeIsPet = _petEntries.Count > 0;
            if (_activeIsPet) _companionList.SelectedIndex = 0;
            else if (_eggEntries.Count > 0) _eggList.SelectedIndex = 0;
        }
        finally
        {
            _loading = false;
        }
        LoadSelectedCompanion();
    }

    public void SaveData(JsonObject saveData)
    {
        ArgumentNullException.ThrowIfNull(saveData);
        if (!ReferenceEquals(saveData, _saveData))
            return;
        CommitCurrentEditor();
    }

    public void ApplyUiLocalisation()
    {
        _title.Text = UiStrings.Get("companion.title");
        _showLockedSlots.Content = UiStrings.Get("companion.show_locked_slots");
        _unlockSlot.Content = UiStrings.Get("companion.slot_unlocked");
        foreach (var (button, key) in _listButtons) button.Content = UiStrings.Get(key);
        _statsTab.Header = Get("companion.tab_stats", "Stats");
        _battleTab.Header = Get("companion.tab_battle", "Battle");
        Set("slot_unlocked", "companion.slot_unlocked", "Slot Unlocked:");
        Set("species", "companion.species", "Species:");
        Set("name", "companion.name", "Name:");
        Set("biome", "companion.biome", "Biome:");
        Set("type", "companion.type", "Type:");
        Set("predator", "companion.predator", "Predator:");
        Set("has_fur", "companion.has_fur", "Has Fur:");
        Set("scale", "companion.scale", "Scale:");
        Set("trust", "companion.trust", "Trust:");
        Set("birth_time", "companion.birth_time", "Birth Time:");
        Set("last_egg_time", "companion.last_egg_time", "Last Egg Time:");
        Set("custom_species_name", "companion.custom_species_name", "Custom Species Name:");
        Set("egg_modified", "companion.egg_modified", "Egg Modified:");
        Set("summoned", "companion.summoned", "Summoned:");
        Set("allow_reroll", "companion.allow_reroll", "Allow Reroll:");
        Set("ua", "companion.ua", "UA:");
        Set("creature_seed", "companion.creature_seed", "Creature Seed:");
        Set("secondary_seed", "companion.secondary_seed", "Secondary Seed:");
        Set("species_seed", "companion.species_seed", "Species Seed:");
        Set("genus_seed", "companion.genus_seed", "Genus Seed:");
        Set("bone_scale_seed", "companion.bone_scale_seed", "Bone Scale Seed:");
        Set("colour_base_seed", "companion.colour_base_seed", "Colour Base Seed:");
        Set("helpfulness", "companion.helpfulness", "Helpfulness:");
        Set("aggression", "companion.aggression", "Aggression:");
        Set("independence", "companion.independence", "Independence:");
        Set("hungry", "companion.hungry", "Hungry:");
        Set("lonely", "companion.lonely", "Lonely:");
        Set("trust_increase", "companion.trust_increase", "Trust Increase:");
        Set("trust_decrease", "companion.trust_decrease", "Trust Decrease:");
        Set("descriptors", "companion.descriptors", "Descriptors (Parts)");
        Set("accessory_customisation", "companion.accessory_customisation", "Accessory Customisation");
        Set("stats_warning", "companion.stats_warning", "Changing some values can change the procedurally generated companion name.");
        Set("battle_team", "companion.battle_team", "Pet Battle Team");
        for (int i = 0; i < 3; i++)
            _labels[$"battle_team_slot_{i}"].Text = UiStrings.Format("companion.battle_team_slot", i + 1);
        Set("battle_override_classes", "companion.battle_override_classes", "Override Pet Classes:");
        Set("battle_average_class", "companion.battle_average_class", "Average Class:");
        Set("battle_class_0", "companion.battle_health", "Health:");
        Set("battle_class_1", "companion.battle_agility", "Agility:");
        Set("battle_class_2", "companion.battle_combat_effectiveness", "Combat Effectiveness:");
        Set("battle_victories", "companion.battle_victories", "Holo-Arena Victories:");
        Set("battle_genes_level", "companion.battle_genes_level", "Genes Improved:");
        Set("battle_mutation_progress", "companion.battle_mutation_progress", "Mutation Progress:");
        Set("battle_genes_available", "companion.battle_genes_available", "Gene Edits Available:");
        Set("battle_treat_health", "companion.battle_treats_health", "Health:");
        Set("battle_treat_agility", "companion.battle_treats_agility", "Agility:");
        Set("battle_treat_combat", "companion.battle_treats_combat", "Combat:");
        Set("battle_affinity", "companion.battle_affinity", "Affinity: ");
        Set("battle_weak", "companion.battle_weak", "  Weak: ");
        Set("battle_strong", "companion.battle_strong", "  Strong: ");
        for (int i = 0; i < 5; i++)
            _labels[$"move_slot_{i}"].Text = UiStrings.Format("companion.battle_move_slot", i + 1);
        _deleteButton.Content = UiStrings.Get("common.delete");
        _importButton.Content = UiStrings.Get("common.import");
        _exportButton.Content = UiStrings.Get("common.export");
        _builderButton.Content = Get("companion.creature_builder", "Creature Builder (Web)");
        _regenerateDescriptor.Content = Get("companion.regen_descriptor_id", "Regenerate ID");
        _induceEgg.Content = Get("companion.induce_egg", "Induce Egg");
        _makeHatchable.Content = Get("companion.make_hatchable", "Make Hatchable");
        _placeEgg.Content = Get("companion.place_egg_in_exosuit", "Place Egg in Exosuit");
        _noAccessories.Text = Get("companion.no_accessories", "This companion cannot use accessories.");
        foreach (Button button in _seedButtons)
            button.Content = UiStrings.Get("companion.gen");
        for (int i = 0; i < 3; i++)
        {
            _accessoryResets[i].Content = Get("companion.accessory_reset", "Reset");
            _accessoryScaleLabels[i].Text = Get("companion.accessory_scale", "Scale:");
        }
        RefreshDynamicLocalisation();
    }

    private static string Get(string key, string fallback) => UiStrings.GetOrNull(key) ?? fallback;

    private void Set(string label, string key, string fallback)
    {
        if (_labels.TryGetValue(label, out TextBlock? block))
            block.Text = Get(key, fallback);
    }

    private void RefreshDynamicLocalisation()
    {
        if (_playerState is null) return;
        RebuildSlotLabels(); LoadSelectedCompanion();
    }

    private void AddSlots(JsonArray? array, string source)
    {
        if (array is null) return;
        for (int index = 0; index < array.Length; index++)
        {
            JsonObject companion;
            try { companion = array.GetObject(index); }
            catch { companion = new JsonObject(); }
            _entries.Add(new CompanionSlot(companion, source, index, !IsOccupied(companion), string.Empty));
        }
        RebuildSlotLabels();
    }

    private void RebuildSlotLabels()
    {
        var selected = (_activeIsPet ? _companionList : _eggList).SelectedItems!.OfType<CompanionSlot>().Select(item => (item.Source, item.OriginalIndex)).ToHashSet();
        bool old = _loading; _loading = true;
        try
        {
            _petEntries.Clear(); _eggEntries.Clear();
            for (int index = 0; index < _entries.Count; index++)
            {
                var entry = _entries[index];
                bool empty = !IsOccupied(entry.Companion);
                bool locked = entry.Source == "Pet" && !IsUnlocked(entry.OriginalIndex);
                string source = UiStrings.Get(entry.Source == "Pet" ? "companion.slot_pet" : "companion.slot_egg");
                string name = entry.Companion.GetString("CustomName") ?? "";
                string label = empty
                    ? UiStrings.Format("companion.slot_label_state", source, entry.OriginalIndex, UiStrings.Get(locked ? "common.locked" : "common.empty"))
                    : UiStrings.Format("companion.slot_label_named", source, entry.OriginalIndex, name.TrimStart('^'));
                if (locked && !empty) label += " (" + UiStrings.Get("common.locked") + ")";
                var updated = entry with { IsEmpty = empty, Label = label };
                _entries[index] = updated;
                if (entry.Source == "Egg") _eggEntries.Add(updated);
                else if (!locked || !empty || _showLockedSlots.IsChecked == true) _petEntries.Add(updated);
            }
            var active = _activeIsPet ? _companionList : _eggList;
            foreach (var item in active.Items.OfType<CompanionSlot>().Where(item => selected.Contains((item.Source, item.OriginalIndex)))) active.SelectedItems!.Add(item);
            if (active.SelectedItem == null && active.ItemCount > 0) active.SelectedIndex = 0;
            var pets = _playerState?.GetArray("Pets"); var eggs = _playerState?.GetArray("Eggs");
            int used = _entries.Count(item => item.Source == "Pet" && !item.IsEmpty);
            int unlocked = pets == null ? 0 : Enumerable.Range(0, pets.Length).Count(IsUnlocked);
            _petSummary.Text = UiStrings.Format("companion.pets_summary", used, unlocked, pets?.Length ?? 0);
            _eggSummary.Text = UiStrings.Format("companion.eggs_summary", _eggEntries.Count(item => !item.IsEmpty), eggs?.Length ?? 0);
        }
        finally { _loading = old; }
    }

    private CompanionSlot? SelectedEntry => (_activeIsPet ? _companionList : _eggList).SelectedItem as CompanionSlot;
    private JsonObject? SelectedCompanion => SelectedEntry?.Companion;
    private static bool IsOccupied(JsonObject companion) => CompanionLogic.IsSlotOccupied(companion);

    private void SelectList(bool pets, SelectionChangedEventArgs e)
    {
        if (_loading || e.AddedItems.Count == 0) return;
        _loading = true;
        try { _activeIsPet = pets; (pets ? _eggList : _companionList).SelectedItems!.Clear(); }
        finally { _loading = false; }
        LoadSelectedCompanion();
    }

    private Control BuildListActions(bool pets)
    {
        var actions = new WrapPanel();
        foreach (var (key, operation) in new (string, Action)[]
        {
            ("companion.sort_az", () => ReorderList(pets, false, 0)),
            ("companion.sort_za", () => ReorderList(pets, true, 0)),
            ("companion.move_up", () => ReorderList(pets, false, -1)),
            ("companion.move_down", () => ReorderList(pets, false, 1))
        })
        {
            var button = new Button(); button.Click += (_, _) => operation();
            _listButtons.Add((button, key)); actions.Children.Add(button);
        }
        return actions;
    }

    private void ReorderList(bool pets, bool descending, int direction)
    {
        if (_playerState == null || _saveData == null) return;
        CommitCurrentEditor();
        var selected = (pets ? _companionList : _eggList).SelectedItems!.OfType<CompanionSlot>().ToArray();
        var indices = selected.Select(item => item.OriginalIndex).Order().ToArray();
        var eggs = _playerState.GetArray("Eggs");
        bool changed = direction == 0
            ? pets ? CompanionSortLogic.SortPets(_playerState, descending) : eggs != null && CompanionSortLogic.SortEggs(eggs, descending)
            : pets ? CompanionSortLogic.MovePets(_playerState, indices, direction) : eggs != null && CompanionSortLogic.MoveEggs(eggs, indices, direction);
        if (!changed) return;
        LoadData(_saveData); _activeIsPet = pets;
        var list = pets ? _companionList : _eggList;
        list.SelectedItems!.Clear();
        foreach (var item in list.Items.OfType<CompanionSlot>().Where(item => selected.Any(before => ReferenceEquals(before.Companion, item.Companion)))) list.SelectedItems.Add(item);
        LoadSelectedCompanion(); Modified();
    }

    private bool IsUnlocked(int index)
    {
        try
        {
            JsonArray? unlocked = _playerState?.GetArray("UnlockedPetSlots");
            return unlocked is not null && index < unlocked.Length && unlocked.GetBool(index);
        }
        catch { return false; }
    }

    private void LoadSelectedCompanion()
    {
        CompanionSlot? entry = SelectedEntry;
        if (entry is null)
        {
            _details.IsVisible = false; _unlockSlot.IsVisible = _lockedHint.IsVisible = false;
            _deleteButton.IsEnabled = _exportButton.IsEnabled = false;
            return;
        }
        _loading = true;
        try
        {
            JsonObject companion = entry.Companion;
            _details.IsVisible = true;
            bool locked = entry.Source == "Pet" && !IsUnlocked(entry.OriginalIndex);
            _details.IsEnabled = !locked;
            _unlockSlot.IsVisible = locked;
            _lockedHint.IsVisible = locked;
            _lockedHint.Text = UiStrings.Get(entry.IsEmpty ? "companion.locked_unlock_hint" : "companion.pet_locked_warning");
            _deleteButton.IsEnabled = _exportButton.IsEnabled = !entry.IsEmpty;
            SelectCreature(companion.GetString("CreatureID") ?? string.Empty);
            _name.Text = companion.GetString("CustomName") ?? string.Empty;
            SetSeedText(_creatureSeed, companion, "CreatureSeed");
            SetSeedText(_secondarySeed, companion, "CreatureSecondarySeed");
            _speciesSeed.Text = companion.GetString("SpeciesSeed") ?? string.Empty;
            _genusSeed.Text = companion.GetString("GenusSeed") ?? string.Empty;
            SelectValue(_biome, companion.GetObject("Biome")?.GetString("Biome"));
            SelectValue(_creatureType, companion.GetObject("CreatureType")?.GetString("CreatureType"));
            _predator.IsChecked = GetBool(companion, "Predator");
            _hasFur.IsChecked = GetBool(companion, "HasFur");
            SetDouble(_scale, companion, "Scale");
            SetDouble(_trust, companion, "Trust");
            SetSeedText(_boneScaleSeed, companion, "BoneScaleSeed");
            SetSeedText(_colourBaseSeed, companion, "ColourBaseSeed");
            SetArrayDoubles(_traits, companion.GetArray("Traits"));
            SetArrayDoubles(_moods, companion.GetArray("Moods"));
            _unlocked.IsChecked = entry.Source == "Pet" && IsUnlocked(entry.OriginalIndex);
            _unlocked.IsEnabled = entry.Source == "Pet";
            SetTimestamp(_birthTime, companion, "BirthTime");
            SetTimestamp(_lastEggTime, companion, "LastEggTime");
            SetTimestamp(_lastTrustIncrease, companion, "LastTrustIncreaseTime");
            SetTimestamp(_lastTrustDecrease, companion, "LastTrustDecreaseTime");
            string species = companion.GetString("CustomSpeciesName") ?? string.Empty;
            _customSpeciesName.Text = species == "^" ? string.Empty : species.TrimStart('^');
            _eggModified.IsChecked = GetBool(companion, "EggModified");
            _summoned.IsChecked = GetBool(companion, "HasBeenSummoned");
            _allowReroll.IsChecked = GetBool(companion, "AllowUnmodifiedReroll");
            LoadUa(companion);
            LoadDescriptors(companion);
            LoadAccessories(entry);
            LoadBattle(companion);
            UpdateBattlePrediction();
            bool editablePet = entry.Source == "Pet" && !entry.IsEmpty;
            SetAccessoriesEnabled(editablePet);
            SetBattleEnabled(editablePet);
            _induceEgg.IsVisible = editablePet;
            _makeHatchable.IsVisible = entry.Source == "Egg" && !entry.IsEmpty;
            _placeEgg.IsVisible = entry.Source == "Egg" && !entry.IsEmpty;
            _goSelectedPetButton.IsEnabled = entry.Source == "Pet";
            _goSelectedEggButton.IsEnabled = entry.Source == "Egg";
        }
        finally
        {
            _loading = false;
        }
    }

    private void SelectCreature(string id)
    {
        CompanionEntry? selected = CompanionDatabase.Entries.FirstOrDefault(entry =>
            string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));
        if (selected is null && !string.IsNullOrEmpty(id) && id != "^")
            selected = new CompanionEntry { Id = id, Species = UiStrings.Format("companion.unrecognised_species", id.TrimStart('^')) };
        if (selected is not null && !CompanionDatabase.Entries.Contains(selected))
            _type.ItemsSource = new[] { selected }.Concat(CompanionDatabase.Entries).ToArray();
        else
            _type.ItemsSource = CompanionDatabase.Entries;
        _type.SelectedItem = selected;
    }

    private static void SelectValue(ComboBox box, string? value)
    {
        box.SelectedItem = box.ItemsView.Cast<object?>().FirstOrDefault(item =>
            string.Equals(item?.ToString(), value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool GetBool(JsonObject obj, string key)
    {
        try { return obj.GetBool(key); }
        catch { return false; }
    }

    private static void SetSeedText(TextBox box, JsonObject companion, string key)
    {
        try
        {
            JsonArray? seed = companion.GetArray(key);
            box.Text = seed is { Length: >= 2 } ? seed.GetString(1) ?? string.Empty : string.Empty;
        }
        catch { box.Text = string.Empty; }
    }

    private static void SetDouble(InvariantNumericTextBox box, JsonObject obj, string key)
    {
        try { box.SetValueWithText(obj.GetDouble(key), obj.GetDoubleText(key)); }
        catch { box.NumericValue = null; }
    }

    private static void SetArrayDoubles(InvariantNumericTextBox[] boxes, JsonArray? values)
    {
        for (int i = 0; i < boxes.Length; i++)
        {
            try
            {
                if (values is not null && i < values.Length)
                    boxes[i].SetValueWithText(values.GetDouble(i), values.GetDoubleText(i));
                else boxes[i].NumericValue = 0;
            }
            catch { boxes[i].NumericValue = 0; }
        }
    }

    private static void SetTimestamp(TextBox box, JsonObject obj, string key)
    {
        try
        {
            box.Text = DateTimeOffset.FromUnixTimeSeconds(obj.GetLong(key)).ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        catch { box.Text = string.Empty; }
    }

    private void LoadUa(JsonObject companion)
    {
        try
        {
            object? raw = companion.Get("UA");
            if (raw is string text && text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                _uaHex.IsChecked = true;
                _ua.Text = text;
            }
            else
            {
                _uaHex.IsChecked = false;
                _ua.Text = companion.GetLong("UA").ToString(CultureInfo.InvariantCulture);
            }
        }
        catch
        {
            _uaHex.IsChecked = false;
            _ua.Text = "0";
        }
    }

    private void CommitCurrentEditor()
    {
        if (SelectedEntry is not { IsEmpty: false } entry || entry.Source == "Pet" && !IsUnlocked(entry.OriginalIndex)) return;
        WriteName();
        WriteCreatureSeed();
        WriteSecondarySeed();
        WriteSpeciesSeed();
        WriteGenusSeed();
        WriteBoneScaleSeed();
        WriteColourBaseSeed();
        WriteCustomSpeciesName();
        WriteUa();
        WriteTimestamp("BirthTime", _birthTime);
        WriteTimestamp("LastEggTime", _lastEggTime);
        WriteTimestamp("LastTrustIncreaseTime", _lastTrustIncrease);
        WriteTimestamp("LastTrustDecreaseTime", _lastTrustDecrease);
        foreach (InvariantNumericTextBox field in _traits.Concat(_moods).Append(_scale).Append(_trust)
                     .Append(_victories).Append(_mutationProgress).Append(_genesAvailable).Concat(_treats)
                     .Concat(_accessoryScales))
            field.TryCommit();
    }

    private void ActivateSelected()
    {
        CompanionSlot? entry = SelectedEntry;
        if (entry is null || !entry.IsEmpty || entry.Source == "Pet" && !IsUnlocked(entry.OriginalIndex)) return;
        JsonArray? seed = entry.Companion.GetArray("CreatureSeed");
        if (seed is { Length: >= 2 }) seed.Set(0, true);
        RebuildSlotLabels();
        _deleteButton.IsEnabled = true;
    }

    private void Modified()
    {
        if (!_loading) { UpdateBattlePrediction(); DataModified?.Invoke(this, EventArgs.Empty); }
    }

    private void WriteName()
    {
        if (_loading || SelectedCompanion is not { } obj) return;
        obj.Set("CustomName", _name.Text ?? string.Empty);
        RebuildSlotLabels();
        Modified();
    }

    private void WriteType()
    {
        if (SelectedCompanion is not { } obj || _type.SelectedItem is not CompanionEntry entry) return;
        obj.Set("CreatureID", entry.Id);
        ActivateSelected();
        Modified();
    }

    private void WriteNestedSelection(string objectKey, string valueKey, ComboBox box)
    {
        if (_loading || SelectedCompanion is not { } obj || box.SelectedItem is not { } selection) return;
        obj.GetObject(objectKey)?.Set(valueKey, selection.ToString() ?? string.Empty);
        if (objectKey == "Biome") LoadAffinity(obj);
        Modified();
    }

    private void WriteBoolean(string key, CheckBox box)
    {
        if (_loading || SelectedCompanion is not { } obj) return;
        obj.Set(key, box.IsChecked == true);
        Modified();
    }

    private void WriteRawDouble(string key, InvariantNumericTextBox field)
    {
        if (_loading || SelectedCompanion is not { } obj || !field.UserModified || field.NumericValue is not double value) return;
        obj.Set(key, new RawDouble(value, field.DisplayText));
        Modified();
    }

    private void WriteArrayDouble(string key, int index, InvariantNumericTextBox field)
    {
        if (_loading || SelectedCompanion is not { } obj || !field.UserModified || field.NumericValue is not double value) return;
        JsonArray? array = obj.GetArray(key);
        if (array is null || index >= array.Length) return;
        array.Set(index, new RawDouble(value, field.DisplayText));
        Modified();
    }

    private void WriteUnlocked()
    {
        if (_loading || _playerState is null || SelectedEntry is not { Source: "Pet" } entry) return;
        CompanionLogic.SetSlotUnlocked(_playerState, entry.OriginalIndex, _unlocked.IsChecked == true);
        RebuildSlotLabels(); LoadBattleTeam(); LoadSelectedCompanion();
        Modified();
    }

    private void WriteCreatureSeed() => WriteSeedArray("CreatureSeed", _creatureSeed, activate: true);
    private void WriteSecondarySeed() => WriteSeedArray("CreatureSecondarySeed", _secondarySeed, activate: false);
    private void WriteBoneScaleSeed() => WriteSeedArray("BoneScaleSeed", _boneScaleSeed, activate: false, zeroIsEmpty: true);
    private void WriteColourBaseSeed() => WriteSeedArray("ColourBaseSeed", _colourBaseSeed, activate: false, zeroIsEmpty: true);

    private void WriteSeedArray(string key, TextBox field, bool activate, bool zeroIsEmpty = false)
    {
        if (_loading || SelectedCompanion is not { } obj) return;
        string? normalized = SeedHelper.NormalizeSeed(field.Text ?? string.Empty);
        if (key == "CreatureSeed" && normalized is null) return;
        JsonArray? array = obj.GetArray(key);
        if (array is null || array.Length < 2) return;
        bool hasValue = normalized is not null && (!zeroIsEmpty || normalized != "0x0");
        array.Set(0, key == "CreatureSeed" || hasValue);
        array.Set(1, hasValue ? normalized! : "0x0");
        if (activate) ActivateSelected();
        Modified();
    }

    private void WriteSpeciesSeed() => WriteIntegerSeed("SpeciesSeed", _speciesSeed);
    private void WriteGenusSeed() => WriteIntegerSeed("GenusSeed", _genusSeed);

    private void WriteIntegerSeed(string key, TextBox field)
    {
        if (_loading || SelectedCompanion is not { } obj) return;
        string? normalized = SeedHelper.NormalizeSeedOrInteger(field.Text ?? string.Empty);
        if (normalized is null) return;
        obj.Set(key, normalized);
        Modified();
    }

    private void WriteCustomSpeciesName()
    {
        if (_loading || SelectedCompanion is not { } obj) return;
        string text = _customSpeciesName.Text ?? string.Empty;
        obj.Set("CustomSpeciesName", string.IsNullOrEmpty(text) ? "^" : $"^{text.TrimStart('^')}");
        Modified();
    }

    private void WriteTimestamp(string key, TextBox field)
    {
        if (_loading || SelectedCompanion is not { } obj) return;
        if (!DateTime.TryParseExact(field.Text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out DateTime local)) return;
        obj.Set(key, new DateTimeOffset(local).ToUniversalTime().ToUnixTimeSeconds());
        Modified();
    }

    private void ToggleUaMode()
    {
        if (_loading) return;
        string text = (_ua.Text ?? string.Empty).Trim();
        if (_uaHex.IsChecked == true && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number))
            _ua.Text = "0x" + number.ToString("X", CultureInfo.InvariantCulture);
        else if (_uaHex.IsChecked != true)
        {
            string hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
            if (long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out number))
                _ua.Text = number.ToString(CultureInfo.InvariantCulture);
        }
        WriteUa();
    }

    private void WriteUa()
    {
        if (_loading || SelectedCompanion is not { } obj) return;
        string text = (_ua.Text ?? string.Empty).Trim();
        if (_uaHex.IsChecked == true)
        {
            string hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
            if (long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
                obj.Set("UA", "0x" + hex.ToUpperInvariant());
            else return;
        }
        else if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number))
            obj.Set("UA", number);
        else return;
        Modified();
    }

    private void RegenerateDescriptorTree(bool clearExisting)
    {
        if (SelectedCompanion is not { } companion) return;
        if (clearExisting && companion.GetArray("Descriptors") is { } values)
        {
            Clear(values);
            values.Add($"^{CreaturePartDatabase.NewDescriptorId()}");
            Modified();
        }
        LoadDescriptors(companion);
    }

    private void LoadDescriptors(JsonObject companion)
    {
        _descriptorEditors.Clear();
        _descriptorPanel.Children.Clear();
        var selected = new List<string>();
        JsonArray? descriptorArray = companion.GetArray("Descriptors");
        if (descriptorArray is not null)
            for (int i = 0; i < descriptorArray.Length; i++)
                selected.Add((descriptorArray.GetString(i) ?? string.Empty).TrimStart('^'));
        CreaturePartEntry? part = CreaturePartDatabase.GetForCreatureId(companion.GetString("CreatureID"));
        if (part is null || part.Details.Count == 0)
        {
            _noDescriptorData.Text = selected.Count > 0
                ? UiStrings.Format("companion.raw_descriptors", selected.Count) + string.Join(", ", selected)
                : Get("companion.no_part_data", "No creature part data is available.");
            _descriptorPanel.Children.Add(_noDescriptorData);
            return;
        }
        foreach (DescriptorGroup group in CreaturePartDatabase.GetFlatGroups(part, selected))
        {
            var none = new DescriptorChoice(null, Get("companion.none", "(none)"));
            var choices = new List<DescriptorChoice> { none };
            choices.AddRange(group.Descriptors.Select(option => new DescriptorChoice(option, option.ToString())));
            var combo = new ComboBox { ItemsSource = choices, MinWidth = 210 };
            combo.SelectedItem = choices.FirstOrDefault(choice => choice.Option is not null &&
                selected.Contains(choice.Option.Id, StringComparer.OrdinalIgnoreCase)) ?? none;
            var editor = new DescriptorEditor(group.GroupId, combo);
            _descriptorEditors.Add(editor);
            combo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                WriteDescriptors(regenerateId: true);
                bool wasLoading = _loading;
                _loading = true;
                try { LoadDescriptors(companion); }
                finally { _loading = wasLoading; }
            };
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("190,*"),
                ColumnSpacing = 8,
                Children = { Label(group.GroupId.Trim('_') + ":"), combo },
            };
            Grid.SetColumn(combo, 1);
            _descriptorPanel.Children.Add(row);
        }
    }

    private void WriteDescriptors(bool regenerateId)
    {
        if (SelectedCompanion?.GetArray("Descriptors") is not { } array) return;
        string? oldId = null;
        if (!regenerateId && array.Length > 0)
        {
            string candidate = (array.GetString(array.Length - 1) ?? string.Empty).TrimStart('^');
            if (candidate.Length == 10 && candidate.All(char.IsDigit)) oldId = candidate;
        }
        var ids = _descriptorEditors
            .Select(editor => (editor.Combo.SelectedItem as DescriptorChoice)?.Option?.Id)
            .Where(id => !string.IsNullOrEmpty(id))
            .Cast<string>()
            .ToArray();
        Clear(array);
        foreach (string id in ids) array.Add($"^{id}");
        array.Add($"^{oldId ?? CreaturePartDatabase.NewDescriptorId()}");
        Modified();
    }

    private JsonObject? GetAccessoryEntry(int petIndex)
    {
        try { return _playerState?.GetArray("PetAccessoryCustomisation")?.GetObject(petIndex); }
        catch { return null; }
    }

    private static JsonObject? GetAccessorySlot(JsonObject entry, int index)
    {
        try { return entry.GetArray("Data")?.GetObject(index); }
        catch { return null; }
    }

    private void LoadAccessories(CompanionSlot entry)
    {
        var descriptors = new List<string>();
        if (entry.Companion.GetArray("Descriptors") is { } array)
            for (int i = 0; i < array.Length; i++)
                descriptors.Add((array.GetString(i) ?? string.Empty).TrimStart('^'));
        _accessoryLayout = entry.Source == "Pet" && !entry.IsEmpty
            ? CompanionAccessoryDatabase.GetSlotLayoutForCreature(entry.Companion.GetString("CreatureID"), descriptors)
            : [];
        _accessoryGrid.IsVisible = _accessoryLayout.Length > 0;
        _noAccessories.IsVisible = _accessoryLayout.Length == 0;
        JsonObject? accessory = GetAccessoryEntry(entry.OriginalIndex);
        for (int index = 0; index < 3; index++)
        {
            bool active = index < _accessoryLayout.Length;
            _accessoryGrid.RowDefinitions[index].Height = active ? GridLength.Auto : new GridLength(0);
            foreach (Control control in new Control[]
                     { _accessorySlotLabels[index], _accessoryCombos[index], _accessoryDescriptors[index], _primaryColours[index], _alternateColours[index], _accessoryScales[index], _accessoryResets[index] })
                control.IsVisible = active;
            if (!active) continue;
            AccessorySlot slot = _accessoryLayout[index];
            _accessorySlotLabels[index].Text = Get(CompanionAccessoryDatabase.GetSlotLabelLocKey(slot), slot + ":");
            var none = new AccessoryChoice(null, Get("companion.accessory_none", "None"));
            var choices = new List<AccessoryChoice> { none };
            choices.AddRange(CompanionAccessoryDatabase.GetEntriesForSlot(slot)
                .Where(item => item.Id != "PET_ACC_NULL")
                .Select(item => new AccessoryChoice(item, item.ToString())));
            _accessoryCombos[index].ItemsSource = choices;
            _accessoryCombos[index].SelectedItem = none;
            _accessoryDescriptors[index].Text = string.Empty;
            _primaryColours[index].Background = Brushes.Transparent;
            _alternateColours[index].Background = Brushes.Transparent;
            _accessoryScales[index].NumericValue = 1;
            JsonObject? data = accessory is null ? null : GetAccessorySlot(accessory, index);
            if (data?.GetString("SelectedPreset") == "^DEFAULT_PET") continue;
            JsonObject? custom = data?.GetObject("CustomData");
            string id = (custom?.GetArray("DescriptorGroups")?.GetString(0) ?? string.Empty).TrimStart('^');
            AccessoryChoice? selected = choices.FirstOrDefault(choice =>
                string.Equals(choice.Entry?.Id, id, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                _accessoryCombos[index].SelectedItem = selected;
                _accessoryDescriptors[index].Text = selected.Entry?.Descriptor ?? string.Empty;
            }
            JsonArray? colours = custom?.GetArray("Colours");
            if (colours is not null)
            {
                _primaryColours[index].Background = new SolidColorBrush(ReadColour(colours, 0));
                _alternateColours[index].Background = new SolidColorBrush(ReadColour(colours, 1));
            }
            if (custom is not null) SetDouble(_accessoryScales[index], custom, "Scale");
        }
    }

    private void WriteAccessory(int index)
    {
        if (SelectedEntry is not { Source: "Pet" } entry || index >= _accessoryLayout.Length) return;
        JsonObject? slot = GetAccessoryEntry(entry.OriginalIndex) is { } root ? GetAccessorySlot(root, index) : null;
        if (slot is null) return;
        JsonObject? custom = slot.GetObject("CustomData");
        if (_accessoryCombos[index].SelectedItem is not AccessoryChoice { Entry: { } selected })
        {
            slot.Set("SelectedPreset", "^DEFAULT_PET");
            if (custom is not null)
            {
                Clear(custom.GetArray("DescriptorGroups"));
                Clear(custom.GetArray("Colours"));
                custom.Set("Scale", 1.0);
            }
            _accessoryDescriptors[index].Text = string.Empty;
            _primaryColours[index].Background = Brushes.Transparent;
            _alternateColours[index].Background = Brushes.Transparent;
            _accessoryScales[index].NumericValue = 1;
        }
        else
        {
            slot.Set("SelectedPreset", "^");
            JsonArray? groups = custom?.GetArray("DescriptorGroups");
            string? decal = groups is { Length: > 1 } ? groups.GetString(1) : null;
            Clear(groups);
            groups?.Add($"^{selected.Id}");
            if (!string.IsNullOrEmpty(decal)) groups?.Add(decal);
            _accessoryDescriptors[index].Text = selected.Descriptor ?? string.Empty;
            if (custom is not null) EnsureAccessoryColourData(custom);
        }
        Modified();
    }

    private void EnsureAccessoryColourData(JsonObject custom)
    {
        JsonArray? colours = custom.GetArray("Colours");
        if (colours is null || colours.Length >= 2) return;
        JsonArray? template = null;
        JsonArray? allAccessories = _playerState?.GetArray("PetAccessoryCustomisation");
        if (allAccessories is not null)
            for (int pet = 0; pet < allAccessories.Length && template is null; pet++)
            {
                JsonArray? slots = allAccessories.GetObject(pet)?.GetArray("Data");
                if (slots is null) continue;
                for (int slot = 0; slot < slots.Length; slot++)
                {
                    JsonArray? candidate = slots.GetObject(slot)?.GetObject("CustomData")?.GetArray("Colours");
                    if (candidate is { Length: >= 2 }) { template = candidate; break; }
                }
            }
        if (template is not null)
        {
            custom.Set("Colours", template.DeepClone());
            return;
        }
        while (colours.Length < 2)
        {
            var entry = new JsonObject();
            var rgba = new JsonArray();
            rgba.Add(1.0); rgba.Add(1.0); rgba.Add(1.0); rgba.Add(1.0);
            entry.Add("Colour", rgba);
            colours.Add(entry);
        }
    }

    private async Task PickAccessoryColourAsync(int index, int colourIndex)
    {
        if (_loading || SelectedEntry is not { Source: "Pet" } entry || index >= _accessoryLayout.Length) return;
        JsonObject? slot = GetAccessoryEntry(entry.OriginalIndex) is { } root ? GetAccessorySlot(root, index) : null;
        JsonArray? colours = slot?.GetObject("CustomData")?.GetArray("Colours");
        if (colours is null || colourIndex >= colours.Length) return;
        Color current = ReadColour(colours, colourIndex);
        Color? selected = await _desktop.PickColourAsync(current);
        if (selected is null) return;
        JsonArray? rgba = colours.GetObject(colourIndex)?.GetArray("Colour");
        if (rgba is null || rgba.Length < 4) return;
        rgba.Set(0, selected.Value.R / 255d);
        rgba.Set(1, selected.Value.G / 255d);
        rgba.Set(2, selected.Value.B / 255d);
        rgba.Set(3, selected.Value.A / 255d);
        (colourIndex == 0 ? _primaryColours[index] : _alternateColours[index]).Background = new SolidColorBrush(selected.Value);
        Modified();
    }

    private static Color ReadColour(JsonArray colours, int index)
    {
        try
        {
            JsonArray? value = colours.GetObject(index)?.GetArray("Colour");
            if (value is null || value.Length < 3) return Colors.Transparent;
            return Color.FromArgb(
                value.Length > 3 ? (byte)Math.Clamp(value.GetDouble(3) * 255, 0, 255) : (byte)255,
                (byte)Math.Clamp(value.GetDouble(0) * 255, 0, 255),
                (byte)Math.Clamp(value.GetDouble(1) * 255, 0, 255),
                (byte)Math.Clamp(value.GetDouble(2) * 255, 0, 255));
        }
        catch { return Colors.Transparent; }
    }

    private void WriteAccessoryScale(int index)
    {
        if (_loading || !_accessoryScales[index].UserModified || SelectedEntry is not { Source: "Pet" } entry) return;
        JsonObject? custom = GetAccessoryEntry(entry.OriginalIndex) is { } root
            ? GetAccessorySlot(root, index)?.GetObject("CustomData") : null;
        if (custom is null || _accessoryScales[index].NumericValue is not double value) return;
        custom.Set("Scale", new RawDouble(value, _accessoryScales[index].DisplayText));
        Modified();
    }

    private void ResetAccessory(int index)
    {
        if (SelectedEntry is not { Source: "Pet" } entry) return;
        JsonObject? slot = GetAccessoryEntry(entry.OriginalIndex) is { } root ? GetAccessorySlot(root, index) : null;
        if (slot is null) return;
        slot.Set("SelectedPreset", "^DEFAULT_PET");
        if (slot.GetObject("CustomData") is { } custom)
        {
            Clear(custom.GetArray("DescriptorGroups"));
            Clear(custom.GetArray("Colours"));
            custom.Set("Scale", 1.0);
        }
        bool old = _loading;
        _loading = true;
        try { LoadAccessories(entry); }
        finally { _loading = old; }
        Modified();
    }

    private void SetAccessoriesEnabled(bool enabled)
    {
        for (int i = 0; i < 3; i++)
        {
            bool active = enabled && i < _accessoryLayout.Length;
            _accessoryCombos[i].IsEnabled = active;
            _primaryColours[i].IsEnabled = active;
            _alternateColours[i].IsEnabled = active;
            _accessoryScales[i].IsEnabled = active;
            _accessoryResets[i].IsEnabled = active;
        }
    }

    private static void Clear(JsonArray? array)
    {
        if (array is null) return;
        for (int i = array.Length - 1; i >= 0; i--) array.RemoveAt(i);
    }

    private void InitialiseMoves()
    {
        for (int index = 0; index < 5; index++)
        {
            int number = index + 1;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _allowedMoves[index] = PetBattleMovesetDatabase.Movesets
                .SelectMany(set => set.Slots.Where(slot => slot.SlotNumber == number))
                .SelectMany(slot => slot.Options)
                .Where(option => seen.Add(option.Template))
                .Select(option => PetBattleMoveDatabase.ById.GetValueOrDefault(option.Template))
                .Where(move => move is not null && !RestrictedMoveIds.Contains(move.Id, StringComparer.OrdinalIgnoreCase))
                .Cast<PetBattleMoveEntry>()
                .ToList();
        }
        _movesInitialised = true;
    }

    private void LoadBattle(JsonObject companion)
    {
        if (!_movesInitialised) InitialiseMoves();
        LoadAffinity(companion);
        _overrideClasses.IsChecked = GetBool(companion, "PetBattlerUseCoreStatClassOverrides");
        JsonArray? overrides = companion.GetArray("PetBattlerCoreStatClassOverrides");
        for (int i = 0; i < 3; i++)
            _classes[i].SelectedItem = ReadClass(overrides, i);
        UpdateClassControls();
        SetDouble(_victories, companion, "PetBattlerVictories");
        SetDouble(_mutationProgress, companion, "PetBattleProgressToTreat");
        SetDouble(_genesAvailable, companion, "PetBattlerTreatsAvailable");
        JsonArray? treats = companion.GetArray("PetBattlerTreatsEaten");
        for (int i = 0; i < 3; i++)
        {
            try { _treats[i].SetValueWithText(treats?.GetInt(i) ?? 0, (treats?.GetInt(i) ?? 0).ToString(CultureInfo.InvariantCulture)); }
            catch { _treats[i].NumericValue = 0; }
        }
        UpdateGenesLevel();
        LoadMoves(companion);
    }

    private void LoadAffinity(JsonObject companion)
    {
        string creature = companion.GetString("CreatureID") ?? string.Empty;
        string biome = companion.GetObject("Biome")?.GetString("Biome") ?? string.Empty;
        string resolved = PetBiomeAffinityMap.ResolveAffinity(creature, biome);
        _affinity.Text = PetBiomeAffinityMap.GetAffinityDisplayName(resolved);
        var matchup = PetBiomeAffinityMap.GetAffinityMatchup(PetBiomeAffinityMap.GetAffinityGameName(resolved));
        _weak.Text = matchup is null ? Get("common.na", "N/A") : PetBiomeAffinityMap.FormatAffinityList(matchup.Value.Weak);
        _strong.Text = matchup is null ? Get("common.na", "N/A") : PetBiomeAffinityMap.FormatAffinityList(matchup.Value.Strong);
    }

    private void LoadBattleTeam()
    {
        if (_playerState is null) return;
        var choices = new List<BattleTeamChoice> { new(-1, Get("companion.battle_team_none", "None")) };
        JsonArray? pets = _playerState.GetArray("Pets");
        if (pets is not null)
            for (int i = 0; i < pets.Length; i++)
            {
                JsonObject pet = pets.GetObject(i);
                if (!IsOccupied(pet) || !IsUnlocked(i)) continue;
                string name = pet.GetString("CustomName") ?? string.Empty;
                choices.Add(new BattleTeamChoice(i, string.IsNullOrEmpty(name) ? $"Pet {i}" : $"Pet {i} - {name}"));
            }
        JsonArray? members = _playerState.GetObject("PetBattleTeam")?.GetArray("TeamMembers");
        for (int i = 0; i < 3; i++)
        {
            int selected = -1;
            try { selected = members?.GetObject(i)?.GetInt("PetIndex") ?? -1; } catch { }
            _battleTeam[i].ItemsSource = choices;
            _battleTeam[i].SelectedItem = choices.FirstOrDefault(choice => choice.PetIndex == selected) ?? choices[0];
        }
    }

    private void WriteBattleTeam(int slot)
    {
        JsonArray? members = _playerState?.GetObject("PetBattleTeam")?.GetArray("TeamMembers");
        if (members is null || slot >= members.Length || _battleTeam[slot].SelectedItem is not BattleTeamChoice selected) return;
        bool old = _loading;
        _loading = true;
        try
        {
            if (selected.PetIndex >= 0)
                for (int i = 0; i < 3; i++)
                {
                    if (i == slot || _battleTeam[i].SelectedItem is not BattleTeamChoice other || other.PetIndex != selected.PetIndex) continue;
                    BattleTeamChoice none = ((IEnumerable<BattleTeamChoice>)_battleTeam[i].ItemsSource!).First();
                    _battleTeam[i].SelectedItem = none;
                    members.GetObject(i)?.Set("PetIndex", -1);
                }
            members.GetObject(slot)?.Set("PetIndex", selected.PetIndex);
        }
        finally { _loading = old; }
        Modified();
    }

    private void LoadMoves(JsonObject companion)
    {
        JsonArray? moves = companion.GetArray("PetBattlerMoves");
        for (int index = 0; index < 5; index++)
        {
            string id = index < (moves?.Length ?? 0) ? (moves?.GetString(index) ?? string.Empty).TrimStart('^') : string.Empty;
            var choices = new List<MoveChoice> { new(null, Get("companion.battle_move_none", "None"), false) };
            choices.AddRange(_allowedMoves[index].Select(move => new MoveChoice(move, move.ToString(), false)));
            if (RestrictedMoveIds.Contains(id, StringComparer.OrdinalIgnoreCase) && PetBattleMoveDatabase.ById.TryGetValue(id, out var restricted))
                choices.Add(new MoveChoice(restricted, restricted.ToString(), true));
            _moveCombos[index].ItemsSource = choices;
            _moveCombos[index].SelectedItem = choices.FirstOrDefault(choice =>
                string.Equals(choice.Move?.Id, id, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
            UpdateMoveDetail(index, id);
        }
    }

    private void UpdateMoveDetail(int index, string id)
    {
        if (string.IsNullOrEmpty(id) || !PetBattleMoveDatabase.ById.TryGetValue(id, out PetBattleMoveEntry? move))
        {
            _moveSets[index].Text = string.Empty;
            _moveDetails[index].Text = string.Empty;
            return;
        }
        _moveSets[index].Text = string.Join(", ", PetBattleMovesetDatabase.FindMovesetsContainingMove(id).Select(set => set.DisplayName));
        string yes = Get("companion.battle_move_detail_yes", "Yes");
        string no = Get("companion.battle_move_detail_no", "No");
        var details = new List<string>
        {
            $"{Get("companion.battle_move_detail_type", "Type:")} {move.IconStyleDisplay}",
            $"{Get("companion.battle_move_detail_target", "Target:")} {move.TargetDisplay}",
            $"{Get("companion.battle_move_detail_multiturn", "Multi-Turn:")} {(move.MultiTurnMove ? yes : no)}",
            $"{Get("companion.battle_move_detail_basic", "Basic Move:")} {(move.BasicMove ? yes : no)}",
        };
        if (!string.IsNullOrEmpty(move.LocIDToDescribeStat))
            details.Add($"{Get("companion.battle_move_detail_stat", "Stat Affected:")} " +
                StringHelper.NormalizeDisplayString(move.LocIDToDescribeStat.Replace("UI_PB_STAT_", string.Empty)));
        for (int phase = 0; phase < move.Phases.Count; phase++)
        {
            PetBattleMovePhase item = move.Phases[phase];
            string prefix = move.Phases.Count > 1 ? UiStrings.Format("companion.battle_move_detail_phase", phase + 1) + " " : string.Empty;
            details.Add($"{prefix}{Get("companion.battle_move_detail_effect", "Effect:")} {item.EffectEmoji} {item.EffectDisplay}");
            details.Add($"{prefix}{Get("companion.battle_move_detail_strength", "Strength:")} {item.StrengthDisplay}");
        }
        _moveDetails[index].Text = string.Join(Environment.NewLine, details);
    }

    private void WriteMove(int index)
    {
        if (SelectedCompanion is not { } companion || _moveCombos[index].SelectedItem is not MoveChoice choice) return;
        if (choice.Restricted)
        {
            bool old = _loading;
            _loading = true;
            try { _moveCombos[index].SelectedIndex = 0; }
            finally { _loading = old; }
            choice = (MoveChoice)_moveCombos[index].SelectedItem!;
        }
        string id = choice.Move?.Id ?? string.Empty;
        JsonArray? moves = companion.GetArray("PetBattlerMoves");
        if (moves is null || index >= moves.Length) return;
        moves.Set(index, string.IsNullOrEmpty(id) ? "^" : $"^{id}");
        UpdateMoveDetail(index, id);
        Modified();
    }

    private static string ReadClass(JsonArray? overrides, int index)
    {
        try { return overrides?.GetObject(index)?.GetString("InventoryClass") ?? "C"; }
        catch { return "C"; }
    }

    private static int ClassValue(string? value) => value switch { "S" => 3, "A" => 2, "B" => 1, _ => 0 };
    private static string ValueClass(int value) => value switch { >= 3 => "S", 2 => "A", 1 => "B", _ => "C" };

    private void WriteOverrideClasses()
    {
        if (_loading || SelectedCompanion is not { } companion) return;
        companion.Set("PetBattlerUseCoreStatClassOverrides", _overrideClasses.IsChecked == true);
        UpdateClassControls();
        Modified();
    }

    private void WriteClasses()
    {
        JsonArray? overrides = SelectedCompanion?.GetArray("PetBattlerCoreStatClassOverrides");
        if (_loading || overrides is null) return;
        for (int i = 0; i < 3 && i < overrides.Length; i++)
            overrides.GetObject(i)?.Set("InventoryClass", _classes[i].SelectedItem?.ToString() ?? "C");
        UpdateClassControls();
        Modified();
    }

    private void UpdateClassControls()
    {
        bool enabled = _overrideClasses.IsEnabled && _overrideClasses.IsChecked == true;
        foreach (ComboBox box in _classes) box.IsEnabled = enabled;
        _averageClass.Text = _overrideClasses.IsChecked == true
            ? ValueClass(_classes.Sum(box => ClassValue(box.SelectedItem?.ToString())) / 3)
            : Get("common.procedural", "Procedural");
    }

    private void WriteInt(string key, InvariantNumericTextBox field)
    {
        if (_loading || !field.UserModified || SelectedCompanion is not { } companion || field.NumericValue is not double value) return;
        companion.Set(key, (int)value);
        Modified();
    }

    private void WriteTreats()
    {
        if (_loading || SelectedCompanion?.GetArray("PetBattlerTreatsEaten") is not { } values) return;
        bool changed = false;
        for (int i = 0; i < 3 && i < values.Length; i++)
        {
            if (!_treats[i].UserModified || _treats[i].NumericValue is not double value) continue;
            values.Set(i, (int)value);
            changed = true;
        }
        UpdateGenesLevel();
        if (changed) Modified();
    }

    private void UpdateGenesLevel()
    {
        _genesLevel.Text = $"{_treats.Sum(field => (int)(field.NumericValue ?? 0))} / 30";
    }

    private void SetBattleEnabled(bool enabled)
    {
        _searchBattleSeed.IsEnabled = _resetBattleRecord.IsEnabled = enabled;
        _overrideClasses.IsEnabled = enabled;
        _victories.IsEnabled = enabled;
        _mutationProgress.IsEnabled = enabled;
        _genesAvailable.IsEnabled = enabled;
        foreach (InvariantNumericTextBox field in _treats) field.IsEnabled = enabled;
        foreach (ComboBox box in _moveCombos) box.IsEnabled = enabled;
        UpdateClassControls();
    }

    private async Task OnDeleteAsync()
    {
        var selected = (_activeIsPet ? _companionList : _eggList).SelectedItems!.OfType<CompanionSlot>().Where(item => !item.IsEmpty).ToArray();
        if (selected.Length == 0) return;
        string message = selected.Length > 1 ? UiStrings.Format("companion.delete_multi_confirm", selected.Length) : UiStrings.Get("companion.delete_confirm");
        if (!await _desktop.ConfirmAsync(Get("companion.delete_title", "Delete Companion"), message)) return;
        foreach (var entry in selected)
        {
            CompanionLogic.DeleteCompanion(entry.Companion);
            if (entry.Source == "Pet" && _playerState != null)
                CompanionLogic.SetSlotUnlocked(_playerState, entry.OriginalIndex, false);
        }
        RebuildSlotLabels(); LoadBattleTeam(); LoadSelectedCompanion(); Modified();
    }

    private async Task OnExportAsync()
    {
        if (SelectedEntry is not { IsEmpty: false } entry) return;
        string extension = ExportConfig.Instance.CompanionExt;
        string species = (_type.SelectedItem as CompanionEntry)?.Species ?? string.Empty;
        var variables = new Dictionary<string, string>
        {
            ["name"] = _name.Text ?? string.Empty,
            ["species"] = species,
            ["creature_seed"] = _creatureSeed.Text ?? string.Empty,
        };
        string fileName = ExportConfig.BuildFileName(ExportConfig.Instance.CompanionTemplate, extension, variables);
        string? path = await _desktop.PickSaveFileAsync(new CompanionFileRequest(
            Get("companion.export_title", "Export Companion"), fileName, extension));
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            JsonArray? accessories = entry.Source == "Pet"
                ? GetAccessoryEntry(entry.OriginalIndex)?.GetArray("Data") : null;
            CompanionLogic.ExportCompanion(entry.Companion.DeepClone(), path, accessories?.DeepClone());
        }
        catch (Exception exception)
        {
            await _desktop.ShowErrorAsync(UiStrings.Get("common.error"), UiStrings.Format("companion.export_failed", exception.Message));
        }
    }

    private async Task OnImportAsync()
    {
        if (_playerState is null) return;
        string extension = ExportConfig.Instance.CompanionExt;
        string? path = await _desktop.PickOpenFileAsync(new CompanionFileRequest(
            Get("companion.import_title", "Import Companion"), string.Empty, extension, ".pet", ".cmp"));
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            JsonArray? pets = _playerState.GetArray("Pets");
            JsonArray? eggs = _playerState.GetArray("Eggs");
            JsonArray? accessories = _playerState.GetArray("PetAccessoryCustomisation");
            int imported;
            bool isPet;
            var targetArray = _activeIsPet ? pets : eggs;
            var fallbackArray = _activeIsPet ? eggs : pets;
            try
            {
                if (targetArray == null) throw new InvalidOperationException();
                imported = CompanionLogic.ImportCompanion(targetArray, path, ReferenceEquals(targetArray, pets) ? accessories : null);
                isPet = ReferenceEquals(targetArray, pets);
            }
            catch (InvalidOperationException)
            {
                if (fallbackArray == null) throw new InvalidOperationException("No empty companion slot available.");
                imported = CompanionLogic.ImportCompanion(fallbackArray, path, ReferenceEquals(fallbackArray, pets) ? accessories : null);
                isPet = ReferenceEquals(fallbackArray, pets);
            }
            if (isPet) CompanionLogic.SetSlotUnlocked(_playerState, imported, true);
            LoadData(_saveData!);
            _activeIsPet = isPet;
            var list = isPet ? _companionList : _eggList;
            list.SelectedItem = list.Items.OfType<CompanionSlot>().FirstOrDefault(item => item.OriginalIndex == imported);
            LoadSelectedCompanion();
            Modified();
        }
        catch (Exception exception)
        {
            await _desktop.ShowErrorAsync(UiStrings.Get("common.error"), UiStrings.Format("companion.import_failed", exception.Message));
        }
    }

    private void MakeHatchable()
    {
        if (SelectedEntry is not { Source: "Egg", IsEmpty: false } entry) return;
        try
        {
            long time = entry.Companion.GetLong("BirthTime") - 86400;
            entry.Companion.Set("BirthTime", time);
            SetTimestamp(_birthTime, entry.Companion, "BirthTime");
            Modified();
        }
        catch
        {
            _ = _desktop.ShowErrorAsync(Get("companion.make_hatchable", "Make Hatchable"),
                Get("companion.make_hatchable_error", "Could not modify birth time."));
        }
    }

    private async Task InduceEggAsync()
    {
        if (_playerState is null || SelectedEntry is not { Source: "Pet", IsEmpty: false } entry) return;
        JsonArray? eggs = _playerState.GetArray("Eggs");
        if (eggs is null || eggs.Length == 0)
        {
            await _desktop.ShowErrorAsync(Get("companion.induce_egg", "Induce Egg"),
                Get("companion.induce_egg_no_slots", "No egg slots were found."));
            return;
        }
        int target = Enumerable.Range(0, eggs.Length).FirstOrDefault(index => !IsOccupied(eggs.GetObject(index)), -1);
        if (target < 0)
        {
            var choices = Enumerable.Range(0, eggs.Length).Select(index =>
            {
                JsonObject egg = eggs.GetObject(index);
                string name = egg.GetString("CustomName") ?? CompanionLogic.LookupSpeciesName(egg.GetString("CreatureID") ?? string.Empty);
                return $"Egg {index + 1}: {name}";
            }).ToArray();
            int? selection = await _desktop.SelectItemAsync(
                Get("companion.induce_egg_select_title", "Egg slots full, replace which egg?"), choices);
            if (selection is null || selection < 0 || selection >= eggs.Length) return;
            target = selection.Value;
            string message = string.Format(CultureInfo.CurrentCulture,
                Get("companion.induce_egg_replace_confirm", "Replace {0} with the new egg?"), choices[target]);
            if (!await _desktop.ConfirmAsync(Get("companion.induce_egg", "Induce Egg"), message)) return;
        }
        try { CopyPetToEgg(entry.Companion, eggs.GetObject(target)); }
        catch
        {
            await _desktop.ShowErrorAsync(Get("companion.induce_egg", "Induce Egg"),
                Get("companion.induce_egg_error", "Failed to create egg."));
            return;
        }
        LoadData(_saveData!);
        Modified();
        if (await _desktop.ConfirmAsync(Get("companion.induce_egg", "Induce Egg"),
                Get("companion.induce_egg_place_prompt", "Egg created. Place it in Exosuit cargo?")))
            await PlaceEggAsync(target);
    }

    private async Task PlaceSelectedEggAsync()
    {
        if (SelectedEntry is { Source: "Egg", IsEmpty: false } entry)
            await PlaceEggAsync(entry.OriginalIndex);
    }

    private async Task PlaceEggAsync(int eggIndex)
    {
        JsonObject? cargo = _playerState?.GetObject(ExosuitLogic.CargoInventoryKey);
        JsonArray? slots = cargo?.GetArray("Slots");
        JsonArray? valid = cargo?.GetArray("ValidSlotIndices");
        string title = Get("companion.place_egg_in_exosuit", "Place Egg in Exosuit");
        if (cargo is null || slots is null || valid is null)
        {
            await _desktop.ShowErrorAsync(title, Get("companion.place_egg_no_inventory", "Exosuit cargo inventory was not found."));
            return;
        }
        var occupied = new HashSet<(int X, int Y)>();
        for (int i = 0; i < slots.Length; i++)
        {
            JsonObject? location = slots.GetObject(i)?.GetObject("Index");
            if (location is not null) occupied.Add((location.GetInt("X"), location.GetInt("Y")));
        }
        (int X, int Y)? target = null;
        for (int i = 0; i < valid.Length; i++)
        {
            JsonObject point = valid.GetObject(i);
            var candidate = (point.GetInt("X"), point.GetInt("Y"));
            if (!occupied.Contains(candidate)) { target = candidate; break; }
        }
        if (target is null)
        {
            await _desktop.ShowInfoAsync(title, Get("companion.place_egg_full", "Exosuit cargo is full."));
            return;
        }
        var newSlot = new JsonObject();
        var type = new JsonObject();
        type.Add("InventoryType", "Product");
        newSlot.Add("Type", type);
        newSlot.Add("Id", $"^EGG{eggIndex + 1}");
        newSlot.Add("Amount", 1);
        newSlot.Add("MaxAmount", 1);
        newSlot.Add("DamageFactor", 0.0);
        newSlot.Add("FullyInstalled", true);
        newSlot.Add("AddedAutomatically", false);
        var targetLocation = new JsonObject();
        targetLocation.Add("X", target.Value.X);
        targetLocation.Add("Y", target.Value.Y);
        newSlot.Add("Index", targetLocation);
        slots.Add(newSlot);
        ExosuitCargoModified?.Invoke(this,
            new CompanionExosuitCargoModifiedEventArgs(cargo, eggIndex, target.Value.X, target.Value.Y));
        Modified();
        await _desktop.ShowInfoAsync(title, string.Format(CultureInfo.CurrentCulture,
            Get("companion.place_egg_success", "Egg placed at position ({0}, {1})."), target.Value.X, target.Value.Y));
    }

    private static void CopyPetToEgg(JsonObject pet, JsonObject egg)
    {
        string[] scalarKeys =
        [
            "Scale", "CreatureID", "CustomName", "CustomSpeciesName", "Predator", "UA",
            "AllowUnmodifiedReroll", "HasFur", "EggModified", "Trust",
            "PetBattlerUseCoreStatClassOverrides", "PetBattlerTreatsAvailable",
            "PetBattleProgressToTreat", "PetBattlerVictories",
        ];
        foreach (string key in scalarKeys)
        {
            object? value = pet.Get(key);
            if (value is not null) egg.Set(key, value);
        }
        string[] arrayKeys =
        [
            "Descriptors", "CreatureSeed", "CreatureSecondarySeed", "ColourBaseSeed", "BoneScaleSeed",
            "Traits",
        ];
        foreach (string key in arrayKeys)
            if (pet.GetArray(key) is { } array) egg.Set(key, array.DeepClone());
        if (pet.GetArray("PetBattlerMoves") is { } currentMoves)
            egg.Set("PetBattlerMoves", currentMoves.DeepClone());
        egg.Set("SpeciesSeed", pet.GetString("SpeciesSeed") ?? "0x0");
        egg.Set("GenusSeed", pet.GetString("GenusSeed") ?? "0x0");
        egg.GetObject("Biome")?.Set("Biome", pet.GetObject("Biome")?.GetString("Biome") ?? "Lush");
        egg.GetObject("CreatureType")?.Set("CreatureType", pet.GetObject("CreatureType")?.GetString("CreatureType") ?? "None");
        foreach (string key in new[] { "LastTrustIncreaseTime", "LastTrustDecreaseTime" })
            egg.Set(key, pet.GetLong(key));
        egg.Set("BirthTime", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        egg.Set("LastEggTime", pet.GetLong("BirthTime"));
        egg.Set("HasBeenSummoned", false);
        JsonArray? moods = egg.GetArray("Moods");
        if (moods is { Length: >= 2 }) { moods.Set(0, 0.01); moods.Set(1, 0.02); }
        foreach (string key in new[] { "PetBattlerCoreStatClassOverrides", "PetBattlerTreatsEaten", "PetBattlerMoveList" })
        {
            JsonArray? source = pet.GetArray(key);
            JsonArray? target = egg.GetArray(key);
            if (source is null || target is null) continue;
            for (int i = 0; i < Math.Min(source.Length, target.Length); i++)
            {
                object? value = source.Get(i);
                if (value is not null) target.Set(i, value);
            }
        }
        JsonObject? fromSender = pet.GetObject("SenderData");
        JsonObject? toSender = egg.GetObject("SenderData");
        if (fromSender is not null && toSender is not null)
        {
            foreach (string key in new[] { "LID", "UID", "USN", "PTK" })
                toSender.Set(key, fromSender.GetString(key) ?? string.Empty);
            toSender.Set("TS", fromSender.GetLong("TS"));
        }
    }

    private sealed record CompanionSlot(JsonObject Companion, string Source, int OriginalIndex, bool IsEmpty, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record DescriptorEditor(string GroupId, ComboBox Combo);
    private sealed record DescriptorChoice(DescriptorOption? Option, string Label)
    {
        public override string ToString() => Label;
    }
    private sealed record AccessoryChoice(CompanionAccessoryEntry? Entry, string Label)
    {
        public override string ToString() => Label;
    }
    private sealed record BattleTeamChoice(int PetIndex, string Label)
    {
        public override string ToString() => Label;
    }
    private sealed record MoveChoice(PetBattleMoveEntry? Move, string Label, bool Restricted)
    {
        public override string ToString() => Label;
    }
}
