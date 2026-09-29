using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
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
    private readonly TabItem _mainTab;
    private readonly TabItem _otherTab;
    private bool _loading;

    public MilestonePanel(AvaloniaResourceCatalog resources)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _mainTab = new TabItem { Content = CreateTabContent(MainColumns) };
        _otherTab = new TabItem { Content = CreateTabContent(OtherColumns) };
        Content = new TabControl { Items = { _mainTab, _otherTab } };
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
        _mainTab.Header = UiStrings.Get("milestone.tab_main");
        _otherTab.Header = UiStrings.Get("milestone.tab_other");
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

        foreach (FieldSpec field in section.Fields)
            content.Children.Add(section.IsGuild ? CreateGuildField(field) : CreateField(field));

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
        if (MilestoneLogic.SectionIconMap.TryGetValue(section.Title, out string? filename) &&
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
            if (!_loading)
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

    private static FieldSpec F(string labelKey, string statId) => new(labelKey, statId);
    private static SectionSpec S(string title, string? titleKey, params FieldSpec[] fields) =>
        new(title, titleKey, false, fields);
    private static SectionSpec G(string title, string titleKey, params FieldSpec[] fields) =>
        new(title, titleKey, true, fields);

    private sealed record FieldSpec(string LabelKey, string StatId);
    private sealed record SectionSpec(string Title, string? TitleKey, bool IsGuild, FieldSpec[] Fields);

    private static readonly IReadOnlyList<SectionSpec[]> MainColumns =
    [
        [
            S("Milestones", "milestone.section_milestones",
                F("milestone.on_foot_exploration", "^DIST_WALKED"),
                F("milestone.alien_encounters", "^ALIENS_MET"),
                F("milestone.words_collected", "^WORDS_LEARNT"),
                F("milestone.most_units_accrued", "^MONEY"),
                F("milestone.ships_destroyed", "^ENEMIES_KILLED"),
                F("milestone.sentinels_destroyed", "^SENTINEL_KILLS"),
                F("milestone.space_exploration", "^DIST_WARP"),
                F("milestone.planet_zoology_scanned", "^DISC_ALL_CREATU")),
            S("Alien Factions", "milestone.section_alien_factions"),
            S("Gek", "milestone.gek",
                F("milestone.standing", "^TRA_STANDING"),
                F("milestone.missions", "^TDONE_MISSIONS"),
                F("milestone.systems_visited", "^TSEEN_SYSTEMS"),
                F("milestone.gek_met", "^TRA_MET")),
            S("Vy'keen", "milestone.vykeen",
                F("milestone.standing", "^WAR_STANDING"),
                F("milestone.missions", "^WDONE_MISSIONS"),
                F("milestone.systems_visited", "^WSEEN_SYSTEMS"),
                F("milestone.vykeen_met", "^WAR_MET")),
            S("Korvax", "milestone.korvax",
                F("milestone.standing", "^EXP_STANDING"),
                F("milestone.missions", "^EDONE_MISSIONS"),
                F("milestone.systems_visited", "^ESEEN_SYSTEMS"),
                F("milestone.korvax_met", "^EXP_MET")),
            S("Autophage", "milestone.autophage",
                F("milestone.standing", "^BUI_STANDING"),
                F("milestone.missions", "^BDONE_MISSIONS"),
                F("milestone.autophage_met", "^BUI_MET"),
                F("milestone.radiant_shards", "^DRONE_SHARDS")),
        ],
        [
            S("Kills", "milestone.section_kills",
                F("milestone.ammo_fired", "^AMMO_FIRED"),
                F("milestone.predators", "^PREDS_KILLED"),
                F("milestone.sentinel_drones", "^DRONES_KILLED"),
                F("milestone.sentinel_quads", "^QUADS_KILLED"),
                F("milestone.sentinel_walkers", "^WALKERS_KILLED"),
                F("milestone.police", "^POLICE_KILLED"),
                F("milestone.civilian_freighters", "^CIV_FREI_KILLS"),
                F("milestone.fish_killed", "^FISH_KILLS"),
                F("milestone.flora_killed", "^FLORA_KILLED"),
                F("milestone.grubs", "^GRUBS_KILLED"),
                F("milestone.jellyfish_boss_1", "^JELLYBOSS"),
                F("milestone.kills_in_mech", "^KILLS_IN_MECH"),
                F("milestone.mechs", "^MECHS_KILLED"),
                F("milestone.miniworms", "^MINIWORM_KILL"),
                F("milestone.pirate_freighters_destroyed", "^PIR_FREI_WINS"),
                F("milestone.queens", "^QUEENS_KILLED"),
                F("milestone.road_kill", "^ROAD_KILL"),
                F("milestone.jellyfish_boss_2", "^S20_JELLYBOSS"),
                F("milestone.sentinel_freighters", "^SENTFREI_KILLED"),
                F("milestone.spiders", "^SPIDERS_KILLED"),
                F("milestone.spookfiend_boss", "^SPOOKBOSS"),
                F("milestone.spookfiend_juice", "^SPOOK_JUICE"),
                F("milestone.spookfiends", "^SPOOK_KILLS"),
                F("milestone.stone_guardians", "^STONE_KILLS")),
        ],
        [
            S("", "milestone.section_guilds"),
            G("Merchants Guild", "milestone.merchants_guild",
                F("milestone.standing", "^TGUILD_STAND"),
                F("milestone.missions_completed", "^TGDONE_MISSIONS"),
                F("milestone.plants_farmed", "^PLANTS_PLANTED"),
                F("milestone.proc_prods", "^PROC_PRODS")),
            G("Mercenaries Guild", "milestone.mercenaries_guild",
                F("milestone.standing", "^WGUILD_STAND"),
                F("milestone.missions_completed", "^WGDONE_MISSIONS"),
                F("milestone.pirates", "^PIRATES_KILLED"),
                F("milestone.fiends", "^FIENDS_KILLED")),
        ],
        [
            G("Explorers Guild", "milestone.explorers_guild",
                F("milestone.standing", "^EGUILD_STAND"),
                F("milestone.missions_completed", "^EGDONE_MISSIONS"),
                F("milestone.rare_creatures", "^RARE_SCANNED"),
                F("milestone.flora_discovered", "^DISC_FLORA")),
            G("Outlaws", "milestone.outlaws",
                F("milestone.standing", "^PIRATE_STAND"),
                F("milestone.missions_completed", "^PIRATE_MISSIONS"),
                F("milestone.bounties", "^BOUNTIES"),
                F("milestone.traders_killed", "^TRADERS_KILLED"),
                F("milestone.smuggled_value", "^SMUGGLE_VALUE")),
        ],
    ];

    private static readonly IReadOnlyList<SectionSpec[]> OtherColumns =
    [
        [
            S("Other Milestones / Stats", "milestone.section_other",
                F("milestone.total_play_time", "^TIME"),
                F("milestone.play_sessions", "^PLAY_SESSIONS"),
                F("milestone.total_deaths", "^DEATHS"),
                F("milestone.longest_life", "^LONGEST_LIFE"),
                F("milestone.units_all_time", "^MONEY_EVER"),
                F("milestone.nanites", "^NANITES"),
                F("milestone.nanites_all_time", "^NANITES_EVER"),
                F("milestone.ships_bought", "^SHIPS_BOUGHT"),
                F("milestone.distance_jetpack", "^DIST_JETPACK"),
                F("milestone.distance_flying", "^DIST_FLY"),
                F("milestone.distance_exocraft", "^DIST_EXO"),
                F("milestone.distance_pulse", "^DIST_PULSE"),
                F("milestone.distance_submarine", "^DIST_SUB"),
                F("milestone.distance_in_space", "^DIST_SPACE"),
                F("milestone.planets_discovered", "^DISC_PLANETS"),
                F("milestone.systems_discovered", "^DISC_SYSTEMS"),
                F("milestone.creatures_discovered", "^DISC_CREATURES"),
                F("milestone.minerals_discovered", "^DISC_MINERALS"),
                F("milestone.waypoints_discovered", "^DISC_WAYPOINTS"),
                F("milestone.planets_visited", "^VISIT_PLANETS"),
                F("milestone.creatures_fed", "^CREATURES_FED"),
                F("milestone.creatures_killed", "^CREATURES_KILL"),
                F("milestone.extreme_survival", "^EXTREME_WALK"),
                F("milestone.pirate_missions_done", "^PDONE_MISSIONS"),
                F("milestone.pirate_systems_visited", "^PIRATE_SYSTEMS")),
            S("Discoveries (Planets)", "milestone.section_disc_planets",
                F("milestone.disc_abandoned", "^DISC_ABAND"),
                F("milestone.disc_cold", "^DISC_P_COLD"),
                F("milestone.disc_dead", "^DISC_P_DEAD"),
                F("milestone.disc_dust", "^DISC_P_DUST"),
                F("milestone.disc_gas", "^DISC_P_GAS"),
                F("milestone.disc_hot", "^DISC_P_HOT"),
                F("milestone.disc_lava", "^DISC_P_LAVA"),
                F("milestone.disc_lush", "^DISC_P_LUSH"),
                F("milestone.disc_radioactive", "^DISC_P_RAD"),
                F("milestone.disc_rgb", "^DISC_P_RGB"),
                F("milestone.disc_swamp", "^DISC_P_SWAMP"),
                F("milestone.disc_toxic", "^DISC_P_TOX"),
                F("milestone.disc_water", "^DISC_P_WATER"),
                F("milestone.disc_weird", "^DISC_P_WEIRD"),
                F("milestone.disc_rare_system", "^DISC_RARE_SYS"),
                F("milestone.visit_cold", "^VISIT_COLD"),
                F("milestone.visit_dead", "^VISIT_DEAD"),
                F("milestone.visit_dust", "^VISIT_DUST"),
                F("milestone.visit_gas", "^VISIT_GAS"),
                F("milestone.visit_hot", "^VISIT_HOT"),
                F("milestone.visit_lava", "^VISIT_LAVA"),
                F("milestone.visit_lush", "^VISIT_LUSH"),
                F("milestone.visit_radioactive", "^VISIT_RAD"),
                F("milestone.visit_rgb", "^VISIT_RGB"),
                F("milestone.visit_swamp", "^VISIT_SWAMP"),
                F("milestone.visit_toxic", "^VISIT_TOX"),
                F("milestone.visit_water", "^VISIT_WATER"),
                F("milestone.visit_weird", "^VISIT_WEIRD")),
        ],
        [
            S("", null,
                F("milestone.storm_survival", "^STORM_WALK"),
                F("milestone.cave_exploration", "^CAVE_WALK"),
                F("milestone.time_in_space", "^SPACE_TIME"),
                F("milestone.space_battles", "^SPACE_BATTLES"),
                F("milestone.fish_caught", "^FISH_CAUGHT"),
                F("milestone.fish_released", "^FISH_RELEASED"),
                F("milestone.bones_found", "^BONES_FOUND"),
                F("milestone.fossils_made", "^FOS_MADE"),
                F("milestone.salvage_looted", "^SALVAGE_LOOTED"),
                F("milestone.ruins_looted", "^RUINS_LOOTED"),
                F("milestone.gifts_given", "^GIFTS_GIVEN"),
                F("milestone.parts_placed", "^PARTS_PLACED"),
                F("milestone.base_parts_got", "^BASEPARTS_GOT"),
                F("milestone.pets_adopted", "^PETS_ADOPTED"),
                F("milestone.photo_mode_used", "^PHOTO_MODE_USED"),
                F("milestone.portal_warps", "^PORTAL_WARPS"),
                F("milestone.items_teleported", "^ITEMS_TELEPRT"),
                F("milestone.abandoned_freighters", "^ABAND_FREIGHTER"),
                F("milestone.acrobat", "^ACROBAT"),
                F("milestone.props_analysed", "^ANALYSE_PROP"),
                F("milestone.app_sessions", "^APP_SESSIONS"),
                F("milestone.artifact_hints", "^ARTIFACT_HINTS"),
                F("milestone.asteroids_destroyed", "^ASTEROIDS"),
                F("milestone.pirate_missions_req", "^MISSION_PIRATES")),
            S("Discoveries (Creatures)", "milestone.section_disc_creatures",
                F("milestone.disc_cre_aggressive", "^DISC_CRE_AGGRO"),
                F("milestone.disc_cre_flying", "^DISC_CRE_AIR"),
                F("milestone.disc_cre_cave", "^DISC_CRE_CAVE"),
                F("milestone.disc_cre_dissonant", "^DISC_CRE_DISS"),
                F("milestone.disc_cre_land", "^DISC_CRE_LAND"),
                F("milestone.disc_cre_robot", "^DISC_CRE_ROBOT"),
                F("milestone.disc_cre_water", "^DISC_CRE_WATER"),
                F("milestone.disc_cre_weird", "^DISC_CRE_WEIRD"),
                F("milestone.disc_glowing_strider", "^DISC_STRIDERGLO")),
        ],
        [
            S("", null,
                F("milestone.atlas_loops", "^ATLAS_LOOPS"),
                F("milestone.basecamp_lore", "^BASECOMP_LORE"),
                F("milestone.corvette_parts", "^BIGGS_PART_GOT"),
                F("milestone.black_hole_walks", "^BLACKHOLE_WALKS"),
                F("milestone.black_hole_warps", "^BLACKHOLE_WARPS"),
                F("milestone.dice_games_lost", "^DICE_GAME_LOST"),
                F("milestone.dice_games_won", "^DICE_GAME_WON"),
                F("milestone.early_warps", "^EARLY_WARPS"),
                F("milestone.eggs_received", "^EGGS_GOT"),
                F("milestone.eggs_hatched", "^EGGS_HATCHED"),
                F("milestone.eggs_modified", "^EGGS_MODDED"),
                F("milestone.egg_pods", "^EGG_PODS"),
                F("milestone.excavated", "^EXCAVATED"),
                F("milestone.walked_in_toxic", "^EX_TOX_WALK"),
                F("milestone.fiend_eggs", "^FIEND_EGG"),
                F("milestone.boots_fished", "^FISH_BOOT"),
                F("milestone.fish_cash", "^FISH_CASH"),
                F("milestone.legendary_fish", "^FISH_LEGEND"),
                F("milestone.fish_trapped", "^FISH_TRAPPED"),
                F("milestone.pods_broken", "^FPODS_BROKEN"),
                F("milestone.frigates", "^FRIGATES"),
                F("milestone.gravitino_balls", "^GRAVBALLS"),
                F("milestone.gravity_grabs", "^GRAV_GRAB"),
                F("milestone.gravity_pushes", "^GRAV_PUSH"),
                F("milestone.pirate_mysteries", "^PIRATE_MYSTERY")),
            S("Multiplayer", "milestone.section_multiplayer",
                F("milestone.mp_depots_done", "^MP_DEPOT_DONE"),
                F("milestone.mp_depots_hacked", "^MP_DEPOT_HACK"),
                F("milestone.mp_events", "^MP_EVENT_COUNT"),
                F("milestone.mp_fish", "^MP_FISH_COUNT"),
                F("milestone.mp_full_session_count", "^MP_FULL_COUNT"),
                F("milestone.mp_full_time_spent", "^MP_FULL_TIME"),
                F("milestone.mp_missions_accessed", "^MP_MIS_ACCESS"),
                F("milestone.mp_missions_started", "^MP_MIS_STARTED"),
                F("milestone.mp_orb_count", "^MP_ORB_COUNT"),
                F("milestone.mp_orb_time", "^MP_ORB_TIME"),
                F("milestone.mp_pirate_waves", "^MP_PIRATES_WAVE"),
                F("milestone.mp_planet_quest_markers", "^MP_PQ_RMARKER"),
                F("milestone.mp_planet_quest_stones", "^MP_PQ_WSTONES"),
                F("milestone.mp_rep_fails", "^MP_REP_FAILS"),
                F("milestone.mp_sessions", "^MP_SESSIONS"),
                F("milestone.nexus_missions", "^NEXUS_MISSIONS"),
                F("milestone.nexus_planet_quests", "^NEXUS_MISS_PQ"),
                F("milestone.nexus_qs_missions", "^NEXUS_MISS_QS"),
                F("milestone.nexus_standing", "^NEXUS_STAND")),
        ],
        [
            S("", null,
                F("milestone.grav_throws", "^GRAV_THROW"),
                F("milestone.weapon_repairs", "^GUNSLOTREPAIRS"),
                F("milestone.head_repairs", "^HEAD_REPAIRS"),
                F("milestone.junk_metal", "^JM"),
                F("milestone.junk_metal_banked", "^JM_BANKED"),
                F("milestone.settlement_judgements", "^JUDGEMENTS"),
                F("milestone.longest_life_ex", "^LONGEST_LIFE_EX"),
                F("milestone.meditation", "^MEDITATION"),
                F("milestone.npcs_rescued", "^NPCS_RESCUED"),
                F("milestone.plants_gathered", "^PLANTS_GATHERED"),
                F("milestone.police_summons", "^POLICE_SUMMON"),
                F("milestone.poop_collected", "^POOP_COLLECTED"),
                F("milestone.quicksilver_spent", "^QS_SPENT"),
                F("milestone.resources_extracted", "^RES_EXTRACTED"),
                F("milestone.space_pois", "^SPACE_POI"),
                F("milestone.space_walks", "^SPACE_WALK"),
                F("milestone.storm_crystals", "^STORM_CRYSTALS"),
                F("milestone.times_in_space", "^TIMES_IN_SPACE"),
                F("milestone.treasure_found", "^TREASURE_FOUND"),
                F("milestone.tunnelled_distance", "^TUNNELLED"),
                F("milestone.vr_grabs", "^VR_GRABS"),
                F("milestone.vr_inits", "^VR_INIT"),
                F("milestone.vr_snapturns", "^VR_SNAPTURNS"),
                F("milestone.pirate_freighters_seen", "^PIR_FREI_SEEN")),
            S("Pet Battles", "milestone.section_pet_battles",
                F("milestone.pb_boss_wins", "^PB_BOSS_WINS"),
                F("milestone.pb_challenge_hall_wins", "^PB_CHALL_WINS"),
                F("milestone.pb_nexus", "^PB_D_NEXUS"),
                F("milestone.pb_losses", "^PB_LOSSES"),
                F("milestone.pb_maxed_pets", "^PB_PETS_MAXED"),
                F("milestone.pb_wins", "^PB_WINS"),
                F("milestone.pets_owned", "^PETS_OWNED"),
                F("milestone.pet_levels_spent", "^PET_LEVEL_SPENT")),
            S("Travel", "milestone.section_travel",
                F("milestone.dist_any_corvette", "^DIST_BIGGS"),
                F("milestone.dist_creature", "^DIST_CRE_RIDE"),
                F("milestone.dist_own_corvette", "^DIST_MY_BIGGS"),
                F("milestone.dist_other_corvette", "^DIST_OTH_BIGGS"),
                F("milestone.dist_flying_pet", "^DIST_PET_FLY"),
                F("milestone.dist_pet", "^DIST_PET_RIDE"),
                F("milestone.dist_swam", "^DIST_SWAM"),
                F("milestone.walked_in_cold", "^EX_COLD_WALK"),
                F("milestone.walked_in_heat", "^EX_HOT_WALK"),
                F("milestone.walked_in_radiation", "^EX_RAD_WALK")),
        ],
    ];
}
