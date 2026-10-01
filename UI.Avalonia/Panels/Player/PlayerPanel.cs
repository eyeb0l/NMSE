using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.IO;
using NMSE.Models;
using NMSE.UI.Avalonia.Contracts;
using NMSE.UI.Avalonia.Controls;
using NMSE.UI.Avalonia.Platform;
using NMSE.UI.Avalonia.Services;
using PlayerFontManager = NMSE.UI.Avalonia.Services.FontManager;

namespace NMSE.UI.Avalonia.Panels.Player;

/// <summary>
/// Player, location, save-utility, outfit, guide and title editor. Account data is
/// intentionally loaded and committed separately from the selected save.
/// </summary>
public sealed partial class PlayerPanel : UserControl, ISaveEditorPanel, IGoToJsonSource
{
    private static readonly string[] DifficultyPresets =
        ["Invalid", "Custom", "Normal", "Creative", "Relaxed", "Survival", "Permadeath"];

    private static readonly string[] DifficultyPresetLocKeys =
    [
        "player.preset_invalid", "player.preset_custom", "player.preset_normal",
        "player.preset_creative", "player.preset_relaxed", "player.preset_survival",
        "player.preset_permadeath",
    ];

    private static readonly string[] GuideCategories =
    [
        "Survival Basics", "Getting Around", "Making Discoveries", "Upgrades & Crafting",
        "Construction", "Making Money", "Alien Lifeforms", "Combat",
    ];

    private readonly IDesktopServices? _desktopServices;
    private AvaloniaIconProvider? _iconProvider;
    private readonly TabControl _tabs = new() { FontSize = 13 };
    private readonly TabItem _generalTab = new();
    private readonly TabItem _guidesTab = new();
    private readonly TabItem _titlesTab = new();
    private readonly TabItem _hotkeysTab = new() { Name = "HotkeysTab" };
    private readonly HotkeysPanel _hotkeys;
    private readonly Dictionary<string, TextBlock> _labels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _sections = new(StringComparer.Ordinal);

    private readonly InvariantNumericTextBox _healthField = Number(0, 999999);
    private readonly InvariantNumericTextBox _shieldField = Number(0, 999999);
    private readonly InvariantNumericTextBox _energyField = Number(0, 999999);
    private readonly InvariantNumericTextBox _unitsField = Number(0, uint.MaxValue);
    private readonly InvariantNumericTextBox _nanitesField = Number(0, uint.MaxValue);
    private readonly InvariantNumericTextBox _quicksilverField = Number(0, uint.MaxValue);
    private readonly TextBox _saveNameField = Field();
    private readonly TextBox _saveSummaryField = Field();
    private readonly TextBox _playTimeField = Field(readOnly: true);
    private readonly CheckBox _thirdPersonCharacterCamera = new();
    private readonly TextBlock _lastSaveDate = ValueText();
    private readonly ComboBox _currentPreset = Combo();
    private readonly ComboBox _easiestPreset = Combo();
    private readonly ComboBox _hardestPreset = Combo();
    private readonly TextBox _expeditionNumber = Field(readOnly: true);
    private readonly TextBlock _accountName = ValueText();
    private readonly ComboBox _outfitSelector = Combo(230);
    private readonly Button _outfitExport = new();
    private readonly Button _outfitImport = new();
    private readonly Button _outfitCopy = new();

    private readonly TextBlock _galaxy = ValueText();
    private readonly Border _galaxyCoreDot = new() { Width = 12, Height = 12, CornerRadius = new CornerRadius(6) };
    private readonly TextBlock _portalHex = ValueText();
    private readonly TextBlock _portalDecimal = ValueText();
    private readonly GlyphRenderer _portalGlyphs = new() { GlyphSize = 21, GlyphSpacing = 1 };
    private readonly TextBlock _signalBooster = ValueText();
    private readonly ComboBox _playerState = Combo(230);
    private readonly TextBlock _distanceToCenter = ValueText();
    private readonly TextBlock _jumpsToCenter = ValueText();
    private readonly TextBlock _freighterInSystem = ValueText();
    private readonly TextBlock _nexusInSystem = ValueText();
    private readonly TextBlock _planetsInSystem = ValueText();
    private readonly CheckBox _portalInterference = new();
    private readonly CheckBox _purpleWarpEnabled = new();
    private readonly InvariantNumericTextBox _warpsToNextBattle = Number(0, 999);
    private readonly TextBlock _timeToNextBattle = ValueText();
    private readonly Button _triggerBattle = new();

    private readonly InvariantNumericTextBox _galaxyNumber = Number(1, 257);
    private readonly InvariantNumericTextBox _voxelX = Number(-2048, 2047);
    private readonly InvariantNumericTextBox _voxelY = Number(-128, 127);
    private readonly InvariantNumericTextBox _voxelZ = Number(-2048, 2047);
    private readonly InvariantNumericTextBox _solarSystem = Number(0, 4095);
    private readonly InvariantNumericTextBox _planet = Number(0, 15);
    private readonly TextBox _portalInput = new() { Width = 190, MaxLength = 12 };
    private readonly Button _applyCoordinates = new();
    private readonly Button _convertPortal = new();
    private readonly Button _coordinateRoulette = new();
    private readonly WrapPanel _glyphButtons = new() { Orientation = Orientation.Horizontal, MaxWidth = 240 };

    private readonly ComboBox _sourceSlot = Combo(110);
    private readonly ComboBox _destinationSlot = Combo(110);
    private readonly ComboBox _destinationPlatform = Combo(180);
    private readonly Button _copySlot = new();
    private readonly Button _moveSlot = new();
    private readonly Button _swapSlots = new();
    private readonly Button _deleteSlot = new();
    private readonly Button _transferPlatform = new();
    private readonly TextBlock _saveUtilityWarning = new() { Foreground = Brushes.OrangeRed, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };

    private readonly TextBox _guideFilter = new() { Width = 280 };
    private readonly Button _guideUnlockAll = new();
    private readonly Button _guideLockAll = new();
    private readonly StackPanel _guideContent = new() { Spacing = 8 };
    private readonly List<GuideRow> _guideRows = [];
    private readonly Dictionary<string, ObservableCollection<GuideRow>> _guideViews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DataGrid> _guideGrids = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _guideCategoryHeaders = new(StringComparer.Ordinal);

    private readonly ObservableCollection<TitleRow> _titleRows = [];
    private readonly DataGrid _titlesGrid;
    private readonly Button _titlesUnlockAll = new();
    private readonly Button _titlesLockAll = new();

    private JsonObject? _saveData;
    private JsonObject? _accountData;
    private string? _saveFilePath;
    private Dictionary<string, decimal>? _rawStatValues;
    private Dictionary<string, int>? _rawCoordinateValues;
    private bool _loading;

    public PlayerPanel(IDesktopServices? desktopServices = null, AvaloniaIconProvider? iconProvider = null)
    {
        _desktopServices = desktopServices;
        _hotkeys = new HotkeysPanel(desktopServices);
        _hotkeysTab.Content = _hotkeys;
        _hotkeys.DataModified += (_, _) => DataModified?.Invoke(this, EventArgs.Empty);
        _iconProvider = iconProvider;
        _titlesGrid = CreateTitlesGrid();
        BuildGeneralTab();
        BuildGuidesTab();
        BuildTitlesTab();

        _tabs.Items.Add(_generalTab);
        _tabs.Items.Add(_guidesTab);
        _tabs.Items.Add(_titlesTab);
        _tabs.Items.Add(_hotkeysTab);
        _tabs.SelectedIndex = 0;
        Content = new Border
        {
            Margin = new Thickness(8),
            BorderBrush = new SolidColorBrush(Color.FromArgb(72, 128, 128, 128)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(1),
            Child = _tabs,
        };

        WireEditEvents();
        WireCommands();
        ApplyUiLocalisation();
    }

    public string PlayerName { get; private set; } = string.Empty;
    public void SetLocalisationService(LocalisationService service) => _hotkeys.Localisation = service;

    public event EventHandler? DataModified;
    public event EventHandler? ReloadRequested;
    public event EventHandler<GoToJsonRequestEventArgs>? GoToJsonRequested;
    public event EventHandler<PlayerStateChangedEventArgs>? PlayerStateChanged;
    public event EventHandler<SaveUtilityCompletedEventArgs>? SaveUtilityCompleted;

    public void SetIconProvider(AvaloniaIconProvider? iconProvider)
    {
        _iconProvider = iconProvider;
        if (_accountData is not null)
            LoadGuides(_accountData);
    }

    public void SetSaveFilePath(string? path)
    {
        _saveFilePath = path;
        try
        {
            _lastSaveDate.Text = path is null ? string.Empty : File.GetLastWriteTime(path).ToString("g", CultureInfo.CurrentCulture);
        }
        catch
        {
            _lastSaveDate.Text = string.Empty;
        }
    }

    public void RefreshGlyphButtonImages()
    {
        _portalGlyphs.InvalidateVisual();
        foreach (Control child in _glyphButtons.Children)
            child.InvalidateVisual();
    }

    private void BuildGeneralTab()
    {
        var left = new StackPanel { Spacing = 5 };
        left.Children.Add(Section("stats", "Player Statistics"));
        left.Children.Add(JsonButton("PlayerStateData"));
        left.Children.Add(Row("health", "Health:", _healthField));
        left.Children.Add(Row("shield", "Shield:", _shieldField));
        left.Children.Add(Row("energy", "Energy:", _energyField));
        left.Children.Add(Row("units", "Units:", WithButton(_unitsField, MaxButton(_unitsField))));
        left.Children.Add(Row("nanites", "Nanites:", WithButton(_nanitesField, MaxButton(_nanitesField))));
        left.Children.Add(Row("quicksilver", "Quicksilver:", WithButton(_quicksilverField, MaxButton(_quicksilverField))));
        left.Children.Add(Section("save_info", "Save Info"));
        left.Children.Add(Row("save_name", "Save Name:", _saveNameField));
        left.Children.Add(Row("save_summary", "Save Summary:", _saveSummaryField));
        left.Children.Add(Row("play_time", "Total Play Time:", _playTimeField));
        left.Children.Add(_thirdPersonCharacterCamera);
        left.Children.Add(Row("last_save", "Last Save Date:", _lastSaveDate));
        left.Children.Add(Row("current_preset", "Current Preset:", _currentPreset));
        left.Children.Add(Row("easiest_preset", "Easiest Used:", _easiestPreset));
        left.Children.Add(Row("hardest_preset", "Hardest Used:", _hardestPreset));
        left.Children.Add(Row("expedition", "Expedition:", _expeditionNumber));
        left.Children.Add(Row("account_name", "Account Name:", _accountName));
        left.Children.Add(Section("outfits", "Player Outfits"));
        left.Children.Add(Row("outfit", "Outfits:", _outfitSelector));
        left.Children.Add(Actions(_outfitExport, _outfitImport, _outfitCopy));
        left.Children.Add(JsonButton("PlayerStateData.Outfits"));

        var middle = new StackPanel { Spacing = 5 };
        middle.Children.Add(Section("coordinates", "Current Coordinates"));
        middle.Children.Add(Row("galaxy", "Galaxy:", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _galaxy, _galaxyCoreDot } }));
        middle.Children.Add(Row("portal_hex", "Portal Code (Hex):", _portalHex));
        middle.Children.Add(Row("portal_dec", "Portal Code (Dec):", _portalDecimal));
        middle.Children.Add(Row("portal_glyphs", "Portal Glyphs:", _portalGlyphs));
        middle.Children.Add(Row("signal_booster", "Signal Booster:", _signalBooster));
        middle.Children.Add(Row("player_state", "Player State:", _playerState));
        middle.Children.Add(Row("distance", "Distance to Center:", _distanceToCenter));
        middle.Children.Add(Row("jumps", "Jumps to Center:", _jumpsToCenter));
        middle.Children.Add(Row("freighter_here", "Freighter in System:", _freighterInSystem));
        middle.Children.Add(Row("nexus_here", "Nexus in System:", _nexusInSystem));
        middle.Children.Add(Row("planets", "Planets in System:", _planetsInSystem));
        middle.Children.Add(_portalInterference);
        middle.Children.Add(_purpleWarpEnabled);
        middle.Children.Add(JsonButton("PlayerStateData.UniverseAddress"));
        middle.Children.Add(Section("space_battle", "Space Battle"));
        middle.Children.Add(Row("warps_next", "Warps to Next:", _warpsToNextBattle));
        middle.Children.Add(Row("time_next", "Time to Next:", _timeToNextBattle));
        middle.Children.Add(_triggerBattle);

        var right = new StackPanel { Spacing = 5 };
        right.Children.Add(Section("change_coordinates", "Change Coordinates"));
        right.Children.Add(Row("galaxy_range", "Galaxy (1-257):", _galaxyNumber));
        right.Children.Add(Row("voxel_x", "Voxel X (-2048-2047):", _voxelX));
        right.Children.Add(Row("voxel_y", "Voxel Y (-128-127):", _voxelY));
        right.Children.Add(Row("voxel_z", "Voxel Z (-2048-2047):", _voxelZ));
        right.Children.Add(Row("solar_system", "Solar System (0-4095):", _solarSystem));
        right.Children.Add(Row("planet", "Planet (0-15):", _planet));
        right.Children.Add(_applyCoordinates);
        right.Children.Add(Section("portal_converter", "Portal Code to Coordinates"));
        for (int glyph = 0; glyph < 16; glyph++)
        {
            char value = "0123456789ABCDEF"[glyph];
            var button = new Button
            {
                Content = new TextBlock { Text = value.ToString(), FontFamily = PlayerFontManager.GlyphFont, FontSize = 18 },
                Tag = value,
                Width = 28,
                Height = 28,
                Padding = new Thickness(2),
            };
            button.Click += OnGlyphClicked;
            _glyphButtons.Children.Add(button);
        }
        right.Children.Add(Row("glyphs", "Glyphs:", _glyphButtons));
        right.Children.Add(Row("portal_input", "Portal Code (Hex):", _portalInput));
        right.Children.Add(Actions(_convertPortal, _coordinateRoulette));

        var columns = new Grid
        {
            MinWidth = 1120,
            ColumnDefinitions = new ColumnDefinitions("0.95*,1.15*,0.9*"),
            ColumnSpacing = 8,
            Children = { Card(left), Card(middle), Card(right) },
        };
        Grid.SetColumn(columns.Children[1], 1);
        Grid.SetColumn(columns.Children[2], 2);
        var page = new StackPanel { Spacing = 12, Margin = new Thickness(10), Children = { columns, BuildSaveUtilities() } };
        _generalTab.Content = new ScrollViewer
        {
            Content = page,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
    }

    private Control BuildSaveUtilities()
    {
        for (int slot = 1; slot <= 15; slot++)
        {
            string label = $"Slot {slot}";
            _sourceSlot.Items.Add(label);
            _destinationSlot.Items.Add(label);
        }
        _sourceSlot.SelectedIndex = 0;
        _destinationSlot.SelectedIndex = 1;

        var panel = new StackPanel { Spacing = 7 };
        panel.Children.Add(Section("save_utilities", "Advanced Save Utilities"));
        panel.Children.Add(_saveUtilityWarning);
        panel.Children.Add(new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                InlineLabel("source", "Source:"), _sourceSlot,
                InlineLabel("destination", "Dest:"), _destinationSlot,
                _copySlot, _moveSlot, _swapSlots, _deleteSlot,
            },
        });
        panel.Children.Add(new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { InlineLabel("destination_platform", "Dest Platform:"), _destinationPlatform, _transferPlatform },
        });
        return Card(panel);
    }

    private void BuildGuidesTab()
    {
        TextBlock title = Section("guide_title", "Wiki / Guide Topics");
        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { InlineLabel("guide_filter", "Filter:"), _guideFilter, _guideUnlockAll, _guideLockAll },
        };
        foreach (string category in GuideCategories)
        {
            var header = new TextBlock { FontFamily = PlayerFontManager.NmsFont, FontSize = 14, FontWeight = FontWeight.SemiBold };
            var view = new ObservableCollection<GuideRow>();
            var grid = CreateGuideGrid(view);
            _guideCategoryHeaders[category] = header;
            _guideViews[category] = view;
            _guideGrids[category] = grid;
            _guideContent.Children.Add(header);
            _guideContent.Children.Add(grid);
        }

        var layout = new Grid
        {
            Margin = new Thickness(10),
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 8,
            Children = { title, actions, new ScrollViewer { Content = _guideContent } },
        };
        Grid.SetRow(actions, 1);
        Grid.SetRow(layout.Children[2], 2);
        _guidesTab.Content = layout;
    }

    private void BuildTitlesTab()
    {
        TextBlock title = Section("titles_title", "Player Titles");
        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _titlesUnlockAll, _titlesLockAll },
        };
        var layout = new Grid
        {
            Margin = new Thickness(10),
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 8,
            Children = { title, actions, _titlesGrid },
        };
        Grid.SetRow(actions, 1);
        Grid.SetRow(_titlesGrid, 2);
        _titlesTab.Content = layout;
    }

    private DataGrid CreateGuideGrid(ObservableCollection<GuideRow> rows)
    {
        var grid = new DataGrid
        {
            ItemsSource = rows,
            AutoGenerateColumns = false,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            MinHeight = 90,
            MaxHeight = 420,
        };
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = string.Empty,
            Width = new DataGridLength(44),
            CellTemplate = new FuncDataTemplate<GuideRow>((row, _) => new Image
            {
                Source = row?.Icon,
                Width = 28,
                Height = 28,
                Stretch = Stretch.Uniform,
            }),
        });
        grid.Columns.Add(new DataGridTextColumn { Header = "Topic ID", Binding = OneWay(nameof(GuideRow.TopicId)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = OneWay(nameof(GuideRow.TopicName)), Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Seen", Binding = TwoWay(nameof(GuideRow.Seen)), Width = new DataGridLength(90) });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Unlocked", Binding = TwoWay(nameof(GuideRow.Unlocked)), Width = new DataGridLength(110) });
        return grid;
    }

    private DataGrid CreateTitlesGrid()
    {
        var grid = new DataGrid
        {
            ItemsSource = _titleRows,
            AutoGenerateColumns = false,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "Title ID", Binding = OneWay(nameof(TitleRow.TitleId)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Title", Binding = OneWay(nameof(TitleRow.TitleName)), Width = new DataGridLength(1.4, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Description", Binding = OneWay(nameof(TitleRow.Description)), Width = new DataGridLength(2.2, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Unlocked", Binding = TwoWay(nameof(TitleRow.Unlocked)), Width = new DataGridLength(110) });
        return grid;
    }

    private void WireEditEvents()
    {
        foreach (InvariantNumericTextBox field in new[]
                 { _healthField, _shieldField, _energyField, _unitsField, _nanitesField, _quicksilverField, _warpsToNextBattle })
            field.NumericValueChanged += (_, _) => RaiseDataModified();
        foreach (TextBox field in new[] { _saveNameField, _saveSummaryField })
            field.TextChanged += (_, _) => RaiseDataModified();
        foreach (CheckBox field in new[] { _thirdPersonCharacterCamera, _portalInterference, _purpleWarpEnabled })
            field.IsCheckedChanged += (_, _) => RaiseDataModified();
        foreach (ComboBox field in new[] { _currentPreset, _easiestPreset, _hardestPreset, _outfitSelector })
            field.SelectionChanged += (_, _) => RaiseDataModified();
        _playerState.SelectionChanged += (_, _) =>
        {
            RaiseDataModified();
            if (!_loading && _playerState.SelectedIndex is >= 0 and < 10)
                PlayerStateChanged?.Invoke(this, new PlayerStateChangedEventArgs(CoordinateHelper.PlayerStates[_playerState.SelectedIndex]));
        };
        _guideFilter.TextChanged += (_, _) => RefreshGuideFilter();
    }

    private static InvariantNumericTextBox Number(double min, double max) =>
        new() { Minimum = min, Maximum = max, Width = 160 };

    private static TextBox Field(bool readOnly = false) => new() { MinWidth = 180, IsReadOnly = readOnly, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static ComboBox Combo(double width = 180) => new() { MinWidth = Math.Min(width, 180), HorizontalAlignment = HorizontalAlignment.Stretch };
    private static TextBlock ValueText() => new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };

    private TextBlock Section(string id, string text)
    {
        var block = new TextBlock
        {
            Text = text,
            FontFamily = PlayerFontManager.NmsFont,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 8, 0, 2),
        };
        _sections[id] = block;
        return block;
    }

    private Control Row(string id, string text, Control control)
    {
        var label = InlineLabel(id, text);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("132,*"), ColumnSpacing = 6, Children = { label, control } };
        Grid.SetColumn(control, 1);
        return row;
    }

    private TextBlock InlineLabel(string id, string text)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 5, 3) };
        _labels[id] = label;
        return label;
    }

    private static Control Card(Control child) => new Border
    {
        BorderBrush = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(10),
        Child = child,
    };

    private static Control WithButton(Control field, Button button) =>
        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { field, button } };

    private static WrapPanel Actions(params Control[] controls)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (Control control in controls)
            panel.Children.Add(control);
        return panel;
    }

    private Button MaxButton(InvariantNumericTextBox field)
    {
        var button = new Button { Content = "Max" };
        button.Click += (_, _) =>
        {
            field.NumericValue = field.Maximum;
            RaiseDataModified();
        };
        return button;
    }

    private Button JsonButton(string path)
    {
        var button = new Button { Content = "JSON", Tag = path, HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) => GoToJsonRequested?.Invoke(this, new GoToJsonRequestEventArgs(path));
        return button;
    }

    private static Binding OneWay(string path) => new(path) { Mode = BindingMode.OneWay };
    private static Binding TwoWay(string path) => new(path) { Mode = BindingMode.TwoWay };

    private void RaiseDataModified()
    {
        if (!_loading)
            DataModified?.Invoke(this, EventArgs.Empty);
    }
}
