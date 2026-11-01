using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Contracts;
using NMSE.UI.Avalonia.Controls;
using NMSE.UI.Avalonia.Coordination;
using NMSE.UI.Avalonia.Services;

namespace NMSE.UI.Avalonia.Panels.Milestones;

/// <summary>Full editor for the save's GLOBAL_STATS milestone group.</summary>
public sealed class MilestonePanel : UserControl, ISaveEditorPanel, IGoToJsonSource
{
    private const int GuildRankLabel = 0;
    private const int GuildPromotionLabel = 1;

    private readonly AvaloniaResourceCatalog _resources;
    private readonly Dictionary<string, List<InvariantNumericTextBox>> _fields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _rawValues = new(StringComparer.Ordinal);
    private readonly List<(TextBlock Label, string Key)> _localisedLabels = [];
    private readonly List<Button> _goToJsonButtons = [];
    private readonly Dictionary<(string Id, int Kind), TextBlock> _guildLabels = [];
    private readonly Dictionary<MilestoneTab, TabItem> _catalogueTabs = [];
    private readonly TabControl _tabControl = new();
    private bool _loading;

    public MilestonePanel(AvaloniaResourceCatalog resources)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        foreach (var tab in MilestoneCatalog.TabOrder)
        {
            var sections = MilestoneCatalog.GetSections(tab).SelectMany(section =>
                MilestoneCatalog.GetSectionPlacements(tab, section).GroupBy(p => p.SubcategoryKey)
                    .Select(group => new SectionSpec(section, section, group.Key,
                        group.Select(p => new FieldSpec(p.GameNameKey, p.StatId, p.ShowGuildRank, p.EditorNameKey)).ToArray()))).ToArray();
            var columns = new[] { sections.Where((_, i) => i % 2 == 0).ToArray(), sections.Where((_, i) => i % 2 == 1).ToArray() };
            var item = new TabItem { Content = CreateTabContent(columns) };
            _catalogueTabs.Add(tab, item);
            _tabControl.Items.Add(item);
        }
        var reference = new Button { Content = UiStrings.Get("milestone.reference_title") };
        reference.Click += async (_, _) => await ShowReferenceAsync();
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { reference, _tabControl } };
        Grid.SetRow(_tabControl, 1);
        Content = root;
        ApplyUiLocalisation();
    }

    public event EventHandler? DataModified;
    public event EventHandler<GoToJsonRequestEventArgs>? GoToJsonRequested;

    public void LoadData(JsonObject saveData)
    {
        ArgumentNullException.ThrowIfNull(saveData);
        _loading = true;
        try
        {
            foreach (InvariantNumericTextBox field in _fields.Values.SelectMany(value => value))
                field.NumericValue = 0;
            _rawValues.Clear();

            JsonArray? entries = MilestoneLogic.FindGlobalStats(saveData);
            if (entries is null)
            {
                UpdateAllGuildLabels();
                return;
            }

            for (int index = 0; index < entries.Length; index++)
            {
                JsonObject? entry = entries.GetObject(index);
                string? id = entry?.GetString("Id");
                if (entry is null || id is null || !_fields.TryGetValue(id, out List<InvariantNumericTextBox>? controls))
                    continue;

                int value = MilestoneLogic.ReadStatEntryValue(entry);
                _rawValues[id] = value;
                foreach (InvariantNumericTextBox control in controls)
                    control.NumericValue = value;
            }

            UpdateAllGuildLabels();
        }
        finally
        {
            _loading = false;
        }
    }

    public void SaveData(JsonObject saveData)
    {
        ArgumentNullException.ThrowIfNull(saveData);
        foreach (InvariantNumericTextBox field in _fields.Values.SelectMany(value => value))
            field.TryCommit();

        JsonArray? entries = MilestoneLogic.FindGlobalStats(saveData);
        if (entries is null)
            return;

        for (int index = 0; index < entries.Length; index++)
        {
            JsonObject? entry = entries.GetObject(index);
            string? id = entry?.GetString("Id");
            if (entry is null || id is null || !_fields.TryGetValue(id, out List<InvariantNumericTextBox>? controls))
                continue;

            int? changedValue = null;
            foreach (InvariantNumericTextBox control in controls)
            {
                int value = ConvertToInt32(control.NumericValue);
                if (!_rawValues.TryGetValue(id, out int rawValue) || value != rawValue)
                {
                    changedValue = value;
                    break;
                }
            }

            if (changedValue.HasValue)
                MilestoneLogic.WriteStatEntryValue(entry, changedValue.Value);
        }
    }

    public void ApplyUiLocalisation()
    {
        foreach (var (tab, item) in _catalogueTabs)
            item.Header = UiStrings.Get(MilestoneCatalog.TabLabelKey(tab));
        foreach ((TextBlock label, string key) in _localisedLabels)
            label.Text = UiStrings.Get(key);
        foreach (Button button in _goToJsonButtons)
            ToolTip.SetTip(button, UiStrings.Get("goto_json.tooltip"));
        UpdateAllGuildLabels();
    }

    private Control CreateTabContent(IReadOnlyList<SectionSpec[]> columns)
    {
        var goToJson = new Button
        {
            Content = "\U0001F4D1",
            FontFamily = FontFamily.Parse("Apple Color Emoji, Segoe UI Emoji"),
            Width = 32,
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 8, 4),
        };
        goToJson.Click += (_, _) => GoToJsonRequested?.Invoke(
            this, new GoToJsonRequestEventArgs("PlayerStateData.Stats"));
        _goToJsonButtons.Add(goToJson);

        var columnGrid = new Grid { ColumnSpacing = 8, Margin = new Thickness(8) };
        for (int index = 0; index < columns.Count; index++)
        {
            columnGrid.ColumnDefinitions.Add(new ColumnDefinition(320, GridUnitType.Pixel));
            var stack = new StackPanel { Spacing = 8 };
            foreach (SectionSpec section in columns[index])
                stack.Children.Add(CreateSection(section));
            columnGrid.Children.Add(stack);
            Grid.SetColumn(stack, index);
        }

        var viewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = columnGrid,
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children = { goToJson, viewer },
        };
        Grid.SetRow(viewer, 1);
        return root;
    }

    private Control CreateSection(SectionSpec section)
    {
        var content = new StackPanel { Spacing = 4 };
        if (!string.IsNullOrEmpty(section.Title) || !string.IsNullOrEmpty(section.TitleKey))
            content.Children.Add(CreateSectionHeader(section));

        if (section.SubcategoryKey is { } subcategory)
            content.Children.Add(CreateLocalisedLabel(subcategory));
        foreach (FieldSpec field in section.Fields)
            content.Children.Add(field.ShowGuildRank ? CreateGuildField(field) : CreateField(field));

        return new Border
        {
            Padding = new Thickness(8),
            BorderBrush = new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Child = content,
        };
    }

    private Control CreateSectionHeader(SectionSpec section)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (MilestoneCatalog.IconForSection(section.Title) is { } filename &&
            _resources.Icons?.GetIcon(filename) is { } bitmap)
        {
            row.Children.Add(new Image { Source = bitmap, Width = 22, Height = 22, Stretch = Stretch.Uniform });
        }

        var title = new TextBlock
        {
            Text = section.TitleKey is null ? section.Title : UiStrings.Get(section.TitleKey),
            FontFamily = Services.FontManager.NmsFont,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        if (section.TitleKey is not null)
            _localisedLabels.Add((title, section.TitleKey));
        row.Children.Add(title);
        return row;
    }

    private Control CreateField(FieldSpec field)
    {
        var label = CreateLocalisedLabel(field.LabelKey);
        ToolTip.SetTip(label, $"{UiStrings.Get(field.EditorNameKey)} · {field.StatId}");
        var input = CreateInput(field.StatId, width: 118);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,118"), ColumnSpacing = 6 };
        row.Children.Add(label);
        row.Children.Add(input);
        Grid.SetColumn(input, 1);
        return row;
    }

    private Control CreateGuildField(FieldSpec field)
    {
        var label = CreateLocalisedLabel(field.LabelKey);
        ToolTip.SetTip(label, $"{UiStrings.Get(field.EditorNameKey)} · {field.StatId}");
        var input = CreateInput(field.StatId, width: 90);
        var rank = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _guildLabels[(field.StatId, GuildRankLabel)] = rank;

        var valueRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,90,58"), ColumnSpacing = 5 };
        valueRow.Children.Add(label);
        valueRow.Children.Add(input);
        Grid.SetColumn(input, 1);
        valueRow.Children.Add(rank);
        Grid.SetColumn(rank, 2);

        var promoName = CreateLocalisedLabel("milestone.guild_promo_in");
        promoName.Margin = new Thickness(12, 0, 0, 0);
        promoName.Opacity = 0.65;
        var promoValue = new TextBlock { Opacity = 0.65 };
        _guildLabels[(field.StatId, GuildPromotionLabel)] = promoValue;

        var promoRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,90,58"), ColumnSpacing = 5 };
        promoRow.Children.Add(promoName);
        promoRow.Children.Add(promoValue);
        Grid.SetColumn(promoValue, 1);

        input.NumericValueChanged += (_, _) =>
            UpdateGuildLabels(field.StatId, ConvertToInt32(input.NumericValue));
        return new StackPanel { Spacing = 1, Children = { valueRow, promoRow } };
    }

    private TextBlock CreateLocalisedLabel(string key)
    {
        var label = new TextBlock
        {
            Text = UiStrings.Get(key),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        _localisedLabels.Add((label, key));
        return label;
    }

    private InvariantNumericTextBox CreateInput(string statId, double width)
    {
        var input = new InvariantNumericTextBox
        {
            Minimum = int.MinValue,
            Maximum = int.MaxValue,
            Width = width,
            TextAlignment = TextAlignment.Right,
        };
        if (!_fields.TryGetValue(statId, out List<InvariantNumericTextBox>? controls))
        {
            controls = [];
            _fields.Add(statId, controls);
        }
        controls.Add(input);
        input.NumericValueChanged += (_, _) =>
        {
            if (_loading) return;
            _loading = true;
            try
            {
                foreach (var other in _fields[statId])
                    if (!ReferenceEquals(other, input)) other.NumericValue = input.NumericValue;
                UpdateGuildLabels(statId, ConvertToInt32(input.NumericValue));
            }
            finally { _loading = false; }
            DataModified?.Invoke(this, EventArgs.Empty);
        };
        return input;
    }

    private void UpdateAllGuildLabels()
    {
        foreach (string id in _guildLabels.Keys.Select(key => key.Id).Distinct(StringComparer.Ordinal))
        {
            int value = _fields.TryGetValue(id, out List<InvariantNumericTextBox>? controls)
                ? ConvertToInt32(controls.FirstOrDefault()?.NumericValue)
                : 0;
            UpdateGuildLabels(id, value);
        }
    }

    private void UpdateGuildLabels(string statId, int value)
    {
        if (_guildLabels.TryGetValue((statId, GuildRankLabel), out TextBlock? rank))
        {
            rank.Text = string.Format(
                CultureInfo.InvariantCulture,
                "{0} / {1}",
                MilestoneLogic.GetGuildRank(statId, value),
                MilestoneLogic.GetGuildMaxRank(statId));
        }
        if (_guildLabels.TryGetValue((statId, GuildPromotionLabel), out TextBlock? promotion))
        {
            int remaining = MilestoneLogic.GetGuildNextRankIn(statId, value);
            promotion.Text = remaining < 0
                ? UiStrings.Get("milestone.guild_rank_max")
                : remaining.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static int ConvertToInt32(double? value) => value switch
    {
        null => 0,
        <= int.MinValue => int.MinValue,
        >= int.MaxValue => int.MaxValue,
        _ => (int)value.Value,
    };

    private sealed record FieldSpec(string LabelKey, string StatId, bool ShowGuildRank, string EditorNameKey);
    private sealed record SectionSpec(string Title, string? TitleKey, string? SubcategoryKey, FieldSpec[] Fields);

    private async Task ShowReferenceAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var filter = new TextBox { PlaceholderText = UiStrings.Get("milestone.reference_filter") };
        var list = new ListBox();
        var go = new Button { Content = UiStrings.Get("milestone.reference_navigate") };
        var window = new Window { Title = UiStrings.Get("milestone.reference_title"), Width = 900, Height = 650, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new Grid { Margin = new Thickness(12), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 8, Children = { filter, list, go } };
        Grid.SetRow(list, 1); Grid.SetRow(go, 2); window.Content = root;
        void Refresh() => list.ItemsSource = MilestoneCatalog.Search(filter.Text ?? "", UiStrings.Get)
            .Select(p => new ReferenceRow(p, $"{UiStrings.Get(p.GameNameKey)} · {p.StatId} · {UiStrings.Get(MilestoneCatalog.TabLabelKey(p.Tab))} / {UiStrings.Get(p.SectionKey)} · {UiStrings.Get(p.EditorNameKey)}")).ToArray();
        filter.TextChanged += (_, _) => Refresh();
        go.Click += (_, _) =>
        {
            if (list.SelectedItem is not ReferenceRow row) return;
            _tabControl.SelectedItem = _catalogueTabs[row.Placement.Tab];
            window.Close();
            Dispatcher.UIThread.Post(() =>
            {
                var input = _fields[row.Placement.StatId].FirstOrDefault(field => field.IsEffectivelyVisible);
                input?.BringIntoView(); input?.Focus();
            }, DispatcherPriority.Loaded);
        };
        Refresh();
        await window.ShowDialog(owner);
    }

    private sealed record ReferenceRow(MilestonePlacement Placement, string Label)
    {
        public override string ToString() => Label;
    }
}
