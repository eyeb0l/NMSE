using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Core;
using NMSE.Data;
using NMSE.UI.Avalonia.Dialogs;
using NMSE.UI.Avalonia.Services;

namespace NMSE.UI.Avalonia.Panels.ExportSettings;

/// <summary>Edits and persists the application-wide import/export naming configuration.</summary>
public sealed class ExportSettingsPanel : UserControl
{
    private static readonly FieldDefinition[] ExtensionDefinitions =
    [
        new("Exosuit", null, c => c.ExosuitExt, (c, v) => c.ExosuitExt = v),
        new("Multi-tool", null, c => c.MultitoolExt, (c, v) => c.MultitoolExt = v),
        new("Starship", null, c => c.StarshipExt, (c, v) => c.StarshipExt = v),
        new("Corvette", null, c => c.CorvetteExt, (c, v) => c.CorvetteExt = v),
        new("Corvette Snapshot", null, c => c.CorvetteSnapshotExt, (c, v) => c.CorvetteSnapshotExt = v),
        new("Starship Cargo", null, c => c.StarshipCargoExt, (c, v) => c.StarshipCargoExt = v),
        new("Starship Tech", null, c => c.StarshipTechExt, (c, v) => c.StarshipTechExt = v),
        new("Freighter", null, c => c.FreighterExt, (c, v) => c.FreighterExt = v),
        new("Freighter Cargo", null, c => c.FreighterCargoExt, (c, v) => c.FreighterCargoExt = v),
        new("Freighter Tech", null, c => c.FreighterTechExt, (c, v) => c.FreighterTechExt = v),
        new("Frigate", null, c => c.FrigateExt, (c, v) => c.FrigateExt = v),
        new("Squadron", null, c => c.SquadronExt, (c, v) => c.SquadronExt = v),
        new("Exocraft", null, c => c.ExocraftExt, (c, v) => c.ExocraftExt = v),
        new("Exocraft Cargo", null, c => c.ExocraftCargoExt, (c, v) => c.ExocraftCargoExt = v),
        new("Exocraft Tech", null, c => c.ExocraftTechExt, (c, v) => c.ExocraftTechExt = v),
        new("Companion", null, c => c.CompanionExt, (c, v) => c.CompanionExt = v),
        new("Base", null, c => c.BaseExt, (c, v) => c.BaseExt = v),
        new("Chest", null, c => c.ChestExt, (c, v) => c.ChestExt = v),
        new("Storage", null, c => c.StorageExt, (c, v) => c.StorageExt = v),
        new("Discovery", null, c => c.DiscoveryExt, (c, v) => c.DiscoveryExt = v),
        new("Settlement", null, c => c.SettlementExt, (c, v) => c.SettlementExt = v),
        new("ByteBeat", null, c => c.ByteBeatExt, (c, v) => c.ByteBeatExt = v),
        new("Outfit", null, c => c.OutfitExt, (c, v) => c.OutfitExt = v),
        new("Hotkeys", null, c => c.HotkeysExt, (c, v) => c.HotkeysExt = v),
    ];

    private static readonly FieldDefinition[] TemplateDefinitions =
    [
        new("Exosuit Cargo", "export_config.template_exosuit_cargo", c => c.ExosuitCargoTemplate, (c, v) => c.ExosuitCargoTemplate = v),
        new("Exosuit Tech", "export_config.template_exosuit_tech", c => c.ExosuitTechTemplate, (c, v) => c.ExosuitTechTemplate = v),
        new("Multi-tool", "export_config.template_multitool", c => c.MultitoolTemplate, (c, v) => c.MultitoolTemplate = v),
        new("Starship", "export_config.template_starship", c => c.StarshipTemplate, (c, v) => c.StarshipTemplate = v),
        new("Corvette", "export_config.template_corvette", c => c.CorvetteTemplate, (c, v) => c.CorvetteTemplate = v),
        new("Corvette Snapshot", "export_config.template_corvette_snapshot", c => c.CorvetteSnapshotTemplate, (c, v) => c.CorvetteSnapshotTemplate = v),
        new("Starship Cargo", "export_config.template_starship_cargo", c => c.StarshipCargoTemplate, (c, v) => c.StarshipCargoTemplate = v),
        new("Starship Tech", "export_config.template_starship_tech", c => c.StarshipTechTemplate, (c, v) => c.StarshipTechTemplate = v),
        new("Freighter", "export_config.template_freighter", c => c.FreighterTemplate, (c, v) => c.FreighterTemplate = v),
        new("Freighter Cargo", "export_config.template_freighter_cargo", c => c.FreighterCargoTemplate, (c, v) => c.FreighterCargoTemplate = v),
        new("Freighter Tech", "export_config.template_freighter_tech", c => c.FreighterTechTemplate, (c, v) => c.FreighterTechTemplate = v),
        new("Frigate", "export_config.template_frigate", c => c.FrigateTemplate, (c, v) => c.FrigateTemplate = v),
        new("Squadron", "export_config.template_squadron", c => c.SquadronTemplate, (c, v) => c.SquadronTemplate = v),
        new("Exocraft", "export_config.template_exocraft", c => c.ExocraftTemplate, (c, v) => c.ExocraftTemplate = v),
        new("Exocraft Cargo", "export_config.template_exocraft_cargo", c => c.ExocraftCargoTemplate, (c, v) => c.ExocraftCargoTemplate = v),
        new("Exocraft Tech", "export_config.template_exocraft_tech", c => c.ExocraftTechTemplate, (c, v) => c.ExocraftTechTemplate = v),
        new("Companion", "export_config.template_companion", c => c.CompanionTemplate, (c, v) => c.CompanionTemplate = v),
        new("Base", "export_config.template_base", c => c.BaseTemplate, (c, v) => c.BaseTemplate = v),
        new("Chest", "export_config.template_chest", c => c.ChestTemplate, (c, v) => c.ChestTemplate = v),
        new("Storage", "export_config.template_storage", c => c.StorageTemplate, (c, v) => c.StorageTemplate = v),
        new("Discovery", "export_config.template_discovery", c => c.DiscoveryTemplate, (c, v) => c.DiscoveryTemplate = v),
        new("Settlement", "export_config.template_settlement", c => c.SettlementTemplate, (c, v) => c.SettlementTemplate = v),
        new("ByteBeat", "export_config.template_bytebeat", c => c.ByteBeatTemplate, (c, v) => c.ByteBeatTemplate = v),
        new("Outfit", "export_config.template_outfit", c => c.OutfitTemplate, (c, v) => c.OutfitTemplate = v),
        new("Hotkeys", "export_config.template_hotkeys", c => c.HotkeysTemplate, (c, v) => c.HotkeysTemplate = v),
    ];

    private static readonly string[] HelpVariableKeys =
    [
        "export_config.help_var_player_name", "export_config.help_var_ship_name",
        "export_config.help_var_multitool_name", "export_config.help_var_type",
        "export_config.help_var_class", "export_config.help_var_race",
        "export_config.help_var_rank", "export_config.help_var_seed",
        "export_config.help_var_name", "export_config.help_var_species",
        "export_config.help_var_creature_seed", "export_config.help_var_vehicle_name",
        "export_config.help_var_vehicle_type", "export_config.help_var_base_name",
        "export_config.help_var_chest_number", "export_config.help_var_timestamp",
        "export_config.help_var_frigate_name", "export_config.help_var_freighter_name",
        "export_config.help_var_settlement_name",
    ];

    private readonly Dictionary<string, TextBox> _extensionFields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBox> _templateFields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _templateLabels = new(StringComparer.Ordinal);
    private readonly string _configFilePath;
    private readonly TextBlock _titleLabel;
    private readonly TextBlock _headerLabel;
    private readonly TabItem _extensionsTab;
    private readonly TabItem _templatesTab;
    private readonly TabItem _helpTab;
    private readonly TextBlock _helpHeading;
    private readonly TextBox _helpText;
    private readonly Button _saveButton;
    private readonly Button _resetButton;
    private readonly TextBlock _statusLabel;

    public ExportSettingsPanel(string? configFilePath = null)
    {
        _configFilePath = configFilePath ?? ApplicationPaths.Current.ExportConfigurationFilePath;

        _titleLabel = new TextBlock
        {
            FontFamily = Services.FontManager.NmsFont,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
        };
        _headerLabel = new TextBlock { TextWrapping = TextWrapping.Wrap };

        _extensionsTab = new TabItem { Content = CreateFieldsView(ExtensionDefinitions, _extensionFields, null) };
        _templatesTab = new TabItem { Content = CreateFieldsView(TemplateDefinitions, _templateFields, _templateLabels) };

        _helpHeading = new TextBlock
        {
            FontFamily = Services.FontManager.NmsFont,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
        };
        _helpText = new TextBox
        {
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = FontFamily.Parse("monospace"),
            MinHeight = 320,
        };
        _helpTab = new TabItem
        {
            Content = new ScrollViewer
            {
                Padding = new Thickness(8),
                Content = new StackPanel { Spacing = 8, Children = { _helpHeading, _helpText } },
            },
        };

        var tabs = new TabControl { Items = { _extensionsTab, _templatesTab, _helpTab } };

        _saveButton = new Button();
        _saveButton.Click += OnSave;
        _resetButton = new Button();
        _resetButton.Click += OnReset;
        _statusLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { _saveButton, _resetButton, _statusLabel },
        };

        Content = new Grid
        {
            Margin = new Thickness(12),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 8,
            Children = { _titleLabel, _headerLabel, tabs, actions },
        };
        Grid.SetRow(_headerLabel, 1);
        Grid.SetRow(tabs, 2);
        Grid.SetRow(actions, 3);

        ApplyUiLocalisation();
        LoadConfig();
    }

    public string ConfigFilePath => _configFilePath;

    public void LoadConfig()
    {
        ExportConfig config = ExportConfig.Instance;
        foreach (FieldDefinition definition in ExtensionDefinitions)
            _extensionFields[definition.Key].Text = definition.Get(config);
        foreach (FieldDefinition definition in TemplateDefinitions)
            _templateFields[definition.Key].Text = definition.Get(config);
        SetStatus(string.Empty, Brushes.Transparent);
    }

    public void ApplyConfig()
    {
        ExportConfig config = ExportConfig.Instance;
        ApplyFields(config, ExtensionDefinitions, _extensionFields);
        ApplyFields(config, TemplateDefinitions, _templateFields);
    }

    public void ApplyUiLocalisation()
    {
        _titleLabel.Text = UiStrings.Get("export_config.title");
        _headerLabel.Text = UiStrings.Get("export_config.header");
        _extensionsTab.Header = UiStrings.Get("export_config.tab_extensions");
        _templatesTab.Header = UiStrings.Get("export_config.tab_templates");
        _helpTab.Header = UiStrings.Get("export_config.tab_help");
        _saveButton.Content = UiStrings.Get("export_config.save_settings");
        _resetButton.Content = UiStrings.Get("export_config.reset_defaults");
        _helpHeading.Text = UiStrings.Get("export_config.help_heading");

        foreach (FieldDefinition definition in TemplateDefinitions)
            _templateLabels[definition.Key].Text = UiStrings.Get(definition.LabelKey!);

        var help = new StringBuilder();
        foreach (string key in HelpVariableKeys)
            help.AppendLine("    " + UiStrings.Get(key));
        help.AppendLine();
        help.AppendLine(UiStrings.Get("export_config.help_extensions_heading"));
        help.AppendLine();
        help.Append(UiStrings.Get("export_config.help_extensions_info"));
        _helpText.Text = help.ToString();
    }

    private static ScrollViewer CreateFieldsView(
        IEnumerable<FieldDefinition> definitions,
        Dictionary<string, TextBox> fields,
        Dictionary<string, TextBlock>? localisedLabels)
    {
        var rows = new StackPanel { Spacing = 5 };
        foreach (FieldDefinition definition in definitions)
        {
            var label = new TextBlock
            {
                Text = definition.LabelKey is null ? definition.Key + ":" : UiStrings.Get(definition.LabelKey),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var field = new TextBox { MinWidth = 280 };
            fields.Add(definition.Key, field);
            localisedLabels?.Add(definition.Key, label);

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("190,*"),
                ColumnSpacing = 8,
                Children = { label, field },
            };
            Grid.SetColumn(field, 1);
            rows.Children.Add(row);
        }

        return new ScrollViewer
        {
            Padding = new Thickness(8),
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = rows,
        };
    }

    private static void ApplyFields(
        ExportConfig config,
        IEnumerable<FieldDefinition> definitions,
        IReadOnlyDictionary<string, TextBox> fields)
    {
        foreach (FieldDefinition definition in definitions)
        {
            string value = fields[definition.Key].Text?.Trim() ?? string.Empty;
            if (value.Length > 0)
                definition.Set(config, value);
        }
    }

    private async void OnReset(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            SetStatus(UiStrings.Get("export_config.reset_confirm"), Brushes.OrangeRed);
            return;
        }

        var dialog = new MessageDialogWindow(
            UiStrings.Get("export_config.reset_title"),
            UiStrings.Get("export_config.reset_confirm"),
            showCancel: true);
        if (!await dialog.ShowDialog<bool>(owner))
            return;

        ExportConfig.SetInstance(new ExportConfig());
        LoadConfig();
        SetStatus(UiStrings.Get("export_config.status_reset"), Brushes.Green);
    }

    private void OnSave(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        List<string> warnings = ValidateExtensions();
        ApplyConfig();
        try
        {
            string? directory = Path.GetDirectoryName(_configFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            ExportConfig.Instance.SaveToFile(_configFilePath);

            string status = warnings.Count == 0
                ? UiStrings.Get("export_config.settings_saved")
                : UiStrings.Format("export_config.settings_saved_warnings", string.Join(" ", warnings));
            SetStatus(status, Brushes.Green);
        }
        catch (Exception ex)
        {
            SetStatus(UiStrings.Format("export_config.save_failed", ex.Message), Brushes.IndianRed);
        }
    }

    private List<string> ValidateExtensions()
    {
        var warnings = new List<string>();
        foreach (FieldDefinition definition in ExtensionDefinitions)
        {
            TextBox field = _extensionFields[definition.Key];
            string value = field.Text?.Trim() ?? string.Empty;
            if (value.Length == 0 || value.StartsWith(".", StringComparison.Ordinal))
                continue;

            field.Text = "." + value;
            warnings.Add(UiStrings.Format("export_config.ext_auto_corrected", definition.Key));
        }
        return warnings;
    }

    private void SetStatus(string text, IBrush brush)
    {
        _statusLabel.Text = text;
        _statusLabel.Foreground = brush;
    }

    private sealed record FieldDefinition(
        string Key,
        string? LabelKey,
        Func<ExportConfig, string> Get,
        Action<ExportConfig, string> Set);
}
