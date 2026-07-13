using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.UI.Avalonia.Panels.Starships;

public sealed partial class StarshipPanel
{
    private readonly TextBlock _sceneLabel = new();
    private readonly TextBox _scene = new();
    private readonly ComboBox _sceneChoices = new();
    private readonly TextBlock _customInfo = new() { Foreground = global::Avalonia.Media.Brushes.Gray, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
    private readonly StackPanel _customContent = new() { Spacing = 7 };
    private readonly List<ShipPartControl> _partControls = [];
    private readonly List<ShipTextureControl> _textureControls = [];
    private readonly List<ShipColourControl> _colourControls = [];
    private ComboBox? _palette;
    private ShipCustomisationConfig? _customConfig;
    private bool _customisationEnabled;

    private Control BuildCustomisationLayout()
    {
        _sceneChoices.ItemsSource = ShipCustomisationDatabase.AllResourcePaths
            .Concat(StarshipLogic.ShipInfo.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _sceneChoices.SelectionChanged += (_, _) =>
        {
            if (_loading || _sceneChoices.SelectedItem is not string selected) return;
            _scene.Text = selected;
            ApplySceneResource();
        };
        _scene.LostFocus += (_, _) => ApplySceneResource();
        _scene.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            ApplySceneResource();
            args.Handled = true;
        };
        var sceneRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,*"), ColumnSpacing = 7, Children = { _sceneLabel, _scene, _sceneChoices } };
        Grid.SetColumn(_scene, 1); Grid.SetColumn(_sceneChoices, 2);
        var stack = new StackPanel { Margin = new Thickness(10), Spacing = 10, Children = { sceneRow, _customInfo, _customContent } };
        return new ScrollViewer { Content = stack, VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    }

    private void ApplyCustomisationLocalisation()
    {
        _sceneLabel.Text = UiStrings.Get("starship.customisation_scene_label");
        if (_selectedShipIndex >= 0 && _ships is not null)
        {
            string filename = _ships.GetObject(_selectedShipIndex).GetObject("Resource")?.GetString("Filename") ?? string.Empty;
            LoadCustomisation(filename, _selectedShipIndex, StarshipLogic.IsCorvette(filename));
        }
    }

    private void LoadCustomisation(string resourceFilename, int shipIndex, bool corvette)
    {
        _loading = true;
        try
        {
            _sceneChoices.ItemsSource = ShipCustomisationDatabase.AllResourcePaths
                .Concat(StarshipLogic.ShipInfo.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _scene.Text = resourceFilename;
            _sceneChoices.SelectedItem = _sceneChoices.ItemsSource?.Cast<string>().FirstOrDefault(path => path.Equals(resourceFilename, StringComparison.OrdinalIgnoreCase));
            _partControls.Clear(); _textureControls.Clear(); _colourControls.Clear(); _palette = null; _customContent.Children.Clear();
            _customisationEnabled = !corvette;
            _customisationTab.IsEnabled = !corvette;
            if (corvette)
            {
                _customInfo.Text = UiStrings.Get("starship.customisation_corvette_disabled");
                _customInfo.IsVisible = true;
                if (_outerTabs.SelectedItem == _customisationTab) _outerTabs.SelectedItem = _detailsTab;
                return;
            }
            _customConfig = ShipCustomisationDatabase.GetConfigByResource(resourceFilename);
            if (_customConfig is null)
            {
                _customInfo.Text = UiStrings.Get("starship.customisation_no_config");
                _customInfo.IsVisible = true;
                return;
            }
            _customInfo.Text = UiStrings.Get("starship.customisation_combo_warning");
            _customInfo.IsVisible = true;
            JsonObject? ccd = StarshipLogic.GetShipCustomisation(_playerState?.GetArray("CharacterCustomisationData"), shipIndex);
            BuildPartControls(_customConfig, ccd);
            BuildTextureControls(_customConfig, ccd);
            BuildPaletteControls(_customConfig, ccd);
        }
        finally { _loading = false; }
    }

    private void BuildPartControls(ShipCustomisationConfig config, JsonObject? ccd)
    {
        if (config.Slots.Count == 0) return;
        _customContent.Children.Add(CreateHeading("starship.customisation_parts_heading"));
        HashSet<string> descriptors = ReadDescriptorGroups(ccd);
        foreach (ShipCustomisationSlot slot in config.Slots)
        {
            var choices = new List<ShipPartChoice> { new(UiStrings.Get("starship.customisation_none"), null) };
            choices.AddRange(slot.Items.Select(item => new ShipPartChoice(item.ItemID, item)));
            var combo = new ComboBox { ItemsSource = choices, HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.SelectedItem = choices.FirstOrDefault(choice => choice.Item?.DescriptorGroupIDs.Any(descriptors.Contains) == true) ?? choices[0];
            combo.SelectionChanged += (_, _) => Changed();
            _partControls.Add(new ShipPartControl(combo, slot));
            _customContent.Children.Add(CreateRow($"{slot.Label}:", combo));
        }
    }

    private void BuildTextureControls(ShipCustomisationConfig config, JsonObject? ccd)
    {
        if (config.TextureGroups.Count == 0) return;
        _customContent.Children.Add(CreateHeading("starship.customisation_texture_heading"));
        Dictionary<string, string> selected = ReadTextureOptions(ccd);
        foreach (ShipCustomisationTextureGroup group in config.TextureGroups)
        {
            var choices = new List<ShipValueChoice> { new(string.Empty, UiStrings.Get("starship.customisation_none")) };
            choices.AddRange(group.Options.Select(option => new ShipValueChoice(option, TextureFriendlyName(option))));
            var combo = new ComboBox { ItemsSource = choices };
            combo.SelectedItem = selected.TryGetValue(group.GroupID, out string? value)
                ? choices.FirstOrDefault(choice => choice.RawValue.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? choices[0]
                : choices[0];
            combo.SelectionChanged += (_, _) => Changed();
            _textureControls.Add(new ShipTextureControl(combo, group.GroupID));
            _customContent.Children.Add(CreateRow(UiStrings.Format("starship.customisation_paint_style_label", group.GroupID), combo));
        }
    }

    private void BuildPaletteControls(ShipCustomisationConfig config, JsonObject? ccd)
    {
        if (config.PaletteIDs.Count == 0) return;
        var paletteChoices = new List<ShipValueChoice> { new(string.Empty, UiStrings.Get("starship.customisation_none")) };
        paletteChoices.AddRange(config.PaletteIDs.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => new ShipValueChoice(id, PaletteFriendlyName(id))));
        _palette = new ComboBox { ItemsSource = paletteChoices };
        string paletteId = ccd?.GetObject("CustomData")?.GetString("PaletteID")?.TrimStart('^') ?? string.Empty;
        if (paletteId.Length == 0) paletteId = "SHIP";
        _palette.SelectedItem = paletteChoices.FirstOrDefault(choice => choice.RawValue.Equals(paletteId, StringComparison.OrdinalIgnoreCase)) ?? paletteChoices[0];
        _palette.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            RefreshColourChoices(ccd);
            RaiseModified();
        };
        _customContent.Children.Add(CreateRow(UiStrings.Get("starship.customisation_paint_palette"), _palette));
        _customContent.Children.Add(CreateHeading("starship.customisation_colours_heading"));
        (string Palette, string Alt, string DisplayPalette, string Key)[] channels =
        [
            ("Paint", "Primary", "", "starship.customisation_colour1"),
            ("Paint", "Alternative3", "", "starship.customisation_colour2"),
            ("Undercoat", "Alternative1", "", "starship.customisation_colour3"),
            ("Paint", "Alternative1", "", "starship.customisation_decal1"),
            ("Paint", "Alternative2", "", "starship.customisation_decal2"),
        ];
        channels = channels.Concat(config.ExtraColourChannels.Select(extra => (extra.PaletteName, extra.ColourAlt, extra.DisplayPaletteId, extra.LabelKey))).ToArray();
        foreach (var channel in channels)
        {
            var combo = new ComboBox { MinWidth = 190 };
            var binding = new ShipColourControl(combo, channel.Palette, channel.Alt, channel.DisplayPalette);
            _colourControls.Add(binding);
            combo.SelectionChanged += (_, _) =>
            {
                if (_loading || combo.SelectedItem is not ShipColourChoice choice) return;
                WriteColour(channel.Palette, channel.Alt, choice.Colour);
            };
            _customContent.Children.Add(CreateRow(UiStrings.Get(channel.Key), combo));
        }
        RefreshColourChoices(ccd);
    }

    private void RefreshColourChoices(JsonObject? ccd)
    {
        bool oldLoading = _loading; _loading = true;
        try
        {
            foreach (ShipColourControl binding in _colourControls)
            {
                string paletteId = binding.DisplayPaletteId;
                if (paletteId.Length == 0) paletteId = (_palette?.SelectedItem as ShipValueChoice)?.RawValue ?? "SHIP";
                NmsColourPalette.PaletteEntry[] choices = NmsColourPalette.GetPaletteColours(paletteId) ?? NmsColourPalette.PaintPalette;
                List<ShipColourChoice> values = choices.Select(value => new ShipColourChoice(value.Name, value.Colour)).ToList();
                binding.Combo.ItemsSource = values;
                System.Drawing.Color current = ReadColour(ccd?.GetObject("CustomData")?.GetArray("Colours"), binding.PaletteName, binding.ColourAlt);
                if (current == System.Drawing.Color.Empty && binding.PaletteName.Equals("Undercoat", StringComparison.OrdinalIgnoreCase) && binding.ColourAlt == "Alternative1")
                    current = ReadColour(ccd?.GetObject("CustomData")?.GetArray("Colours"), "Undercoat", "Primary");
                int nearest = FindNearestColour(values, current);
                binding.Combo.SelectedIndex = nearest;
            }
        }
        finally { _loading = oldLoading; }
    }

    private void SaveCustomisation(JsonObject playerState, int shipIndex)
    {
        if (!_customisationEnabled) return;
        ApplySceneResource();
        JsonArray? ccdArray = playerState.GetArray("CharacterCustomisationData");
        int ccdIndex = StarshipLogic.ShipIndexToCcdIndex(shipIndex);
        if (ccdArray is null || ccdIndex < 0 || ccdIndex >= ccdArray.Length) return;
        JsonObject? customData = ccdArray.GetObject(ccdIndex).GetObject("CustomData");
        if (customData is null) return;
        JsonArray descriptorGroups = GetOrCreateArray(customData, "DescriptorGroups"); descriptorGroups.Clear();
        foreach (ShipPartControl control in _partControls)
            if (control.Combo.SelectedItem is ShipPartChoice { Item: not null } selected)
                foreach (string id in selected.Item.DescriptorGroupIDs) descriptorGroups.Add($"^{id}");
        if (_palette?.SelectedItem is ShipValueChoice palette)
            customData.Set("PaletteID", string.IsNullOrWhiteSpace(palette.RawValue) || palette.RawValue == "SHIP" ? "^" : $"^{palette.RawValue}");
        JsonArray textures = GetOrCreateArray(customData, "TextureOptions"); textures.Clear();
        foreach (ShipTextureControl control in _textureControls)
        {
            if (control.Combo.SelectedItem is not ShipValueChoice { RawValue.Length: > 0 } value) continue;
            var entry = new JsonObject(); entry.Set("TextureOptionGroupName", $"^{control.GroupId}"); entry.Set("TextureOptionName", $"^{value.RawValue}"); textures.Add(entry);
        }
    }

    private void ApplySceneResource()
    {
        if (_loading || !_customisationEnabled || _ships is null || _selectedShipIndex < 0) return;
        string value = _scene.Text?.Trim() ?? string.Empty; if (value.Length == 0) return;
        JsonObject? resource = _ships.GetObject(_selectedShipIndex).GetObject("Resource"); string current = resource?.GetString("Filename") ?? string.Empty; if (resource is null || current.Equals(value, StringComparison.OrdinalIgnoreCase)) return;
        resource.Set("Filename", value); string type = StarshipLogic.LookupShipTypeName(value); SelectType(type, StarshipLogic.IsFilenameModified(value) ? value : null);
        var (_, cargo, tech) = StarshipLogic.GetShipInfo(value); CargoGrid.SetMaxSupportedLabel(cargo); TechnologyGrid.SetMaxSupportedLabel(tech);
        string owner = StarshipLogic.GetOwnerTypeForShip(type); CargoGrid.SetInventoryOwnerType(owner); TechnologyGrid.SetInventoryOwnerType(owner);
        LoadCustomisation(value, _selectedShipIndex, StarshipLogic.IsCorvette(value)); RaiseModified();
    }

    private void WriteColour(string paletteName, string colourAlt, System.Drawing.Color colour)
    {
        JsonArray? colours = GetCurrentCustomData()?.GetArray("Colours"); if (colours is null) return;
        int index = FindColourEntry(colours, paletteName, colourAlt);
        if (index < 0 && paletteName == "Undercoat" && colourAlt == "Alternative1") index = FindColourEntry(colours, "Undercoat", "Primary");
        if (index < 0) return;
        JsonArray? rgba = colours.GetObject(index).GetArray("Colour"); if (rgba is null || rgba.Length < 4) return;
        double[] values = NmsColourPalette.ToNormalisedRgba(colour); for (int i = 0; i < 4; i++) rgba.Set(i, values[i]); RaiseModified();
    }

    private JsonObject? GetCurrentCustomData()
    {
        if (_playerState?.GetArray("CharacterCustomisationData") is not { } array || _selectedShipIndex < 0) return null;
        int index = StarshipLogic.ShipIndexToCcdIndex(_selectedShipIndex); return index >= 0 && index < array.Length ? array.GetObject(index).GetObject("CustomData") : null;
    }

    private static HashSet<string> ReadDescriptorGroups(JsonObject? ccd)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase); JsonArray? values = ccd?.GetObject("CustomData")?.GetArray("DescriptorGroups"); if (values is null) return result;
        for (int i = 0; i < values.Length; i++) { string value = values.Get(i)?.ToString()?.TrimStart('^') ?? string.Empty; if (value.Length > 0) result.Add(value); } return result;
    }

    private static Dictionary<string, string> ReadTextureOptions(JsonObject? ccd)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); JsonArray? values = ccd?.GetObject("CustomData")?.GetArray("TextureOptions"); if (values is null) return result;
        for (int i = 0; i < values.Length; i++) { JsonObject entry = values.GetObject(i); string group = entry.GetString("TextureOptionGroupName")?.TrimStart('^') ?? string.Empty; string option = entry.GetString("TextureOptionName")?.TrimStart('^') ?? string.Empty; if (group.Length > 0) result[group] = option; } return result;
    }

    private static System.Drawing.Color ReadColour(JsonArray? colours, string paletteName, string colourAlt)
    {
        if (colours is null) return System.Drawing.Color.Empty; int index = FindColourEntry(colours, paletteName, colourAlt); if (index < 0) return System.Drawing.Color.Empty; JsonArray? rgba = colours.GetObject(index).GetArray("Colour");
        if (rgba is null || rgba.Length < 3) return System.Drawing.Color.Empty; return System.Drawing.Color.FromArgb((int)Math.Round(rgba.GetDouble(0) * 255), (int)Math.Round(rgba.GetDouble(1) * 255), (int)Math.Round(rgba.GetDouble(2) * 255));
    }

    private static int FindColourEntry(JsonArray colours, string paletteName, string colourAlt)
    {
        for (int i = 0; i < colours.Length; i++) { JsonObject? palette = colours.GetObject(i).GetObject("Palette"); if (palette?.GetString("Palette")?.Equals(paletteName, StringComparison.OrdinalIgnoreCase) == true && palette.GetString("ColourAlt")?.Equals(colourAlt, StringComparison.OrdinalIgnoreCase) == true) return i; } return -1;
    }

    private static int FindNearestColour(IReadOnlyList<ShipColourChoice> values, System.Drawing.Color target)
    {
        if (values.Count == 0) return -1; if (target == System.Drawing.Color.Empty) return 0; int best = 0; long distance = long.MaxValue;
        for (int i = 0; i < values.Count; i++) { long r = values[i].Colour.R - target.R; long g = values[i].Colour.G - target.G; long b = values[i].Colour.B - target.B; long current = r * r + g * g + b * b; if (current < distance) { distance = current; best = i; } } return best;
    }

    private static Control CreateRow(string label, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*"), ColumnSpacing = 7, Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, control } }; Grid.SetColumn(control, 1); return grid;
    }

    private static TextBlock CreateHeading(string key) => new() { Text = UiStrings.Get(key), FontWeight = global::Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(0, 7, 0, 1) };
    private static string PaletteFriendlyName(string value) => value switch { "SHIP" => UiStrings.Get("starship.palette_default"), "SHIP_METALLIC" => UiStrings.Get("starship.palette_metallic"), _ => value };
    private static string TextureFriendlyName(string value) => value switch { "COATING" => UiStrings.Get("starship.texture_coating"), "PANELS" => UiStrings.Get("starship.texture_panels"), "STEALTH" => UiStrings.Get("starship.texture_stealth"), "METALBOLT" => UiStrings.Get("starship.texture_metalbolt"), _ => value };
    private static JsonArray GetOrCreateArray(JsonObject owner, string key) { JsonArray? value = owner.GetArray(key); if (value is not null) return value; value = new JsonArray(); owner.Set(key, value); return value; }
}
