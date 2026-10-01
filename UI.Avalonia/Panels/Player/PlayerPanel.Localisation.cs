using Avalonia.Controls;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;

namespace NMSE.UI.Avalonia.Panels.Player;

public sealed partial class PlayerPanel
{
    public void ApplyUiLocalisation()
    {
        _generalTab.Header = UiStrings.Get("player.tab_general");
        _guidesTab.Header = UiStrings.Get("player.tab_guide");
        _titlesTab.Header = UiStrings.Get("player.tab_titles");
        _hotkeysTab.Header = UiStrings.Get("player.tab_hotkeys");
        _hotkeys.ApplyUiLocalisation();

        SetSection("stats", "player.section_stats");
        SetSection("save_info", "player.save_info");
        SetSection("outfits", "outfits.title");
        SetSection("coordinates", "player.section_coordinates");
        SetSection("space_battle", "player.space_battle_section");
        SetSection("change_coordinates", "player.section_change_coords");
        SetSection("portal_converter", "player.section_portal_to_coords");
        SetSection("save_utilities", "player.section_save_utils");
        SetSection("guide_title", "player.guide_title");
        SetSection("titles_title", "player.titles_header");

        SetLabel("health", "player.health");
        SetLabel("shield", "player.shield");
        SetLabel("energy", "player.energy");
        SetLabel("units", "player.units");
        SetLabel("nanites", "player.nanites");
        SetLabel("quicksilver", "player.quicksilver");
        SetLabel("save_name", "player.save_name");
        SetLabel("save_summary", "player.save_summary");
        SetLabel("play_time", "player.total_play_time");
        SetLabel("last_save", "player.last_save_date");
        SetLabel("current_preset", "player.current_preset");
        SetLabel("easiest_preset", "player.easiest_used");
        SetLabel("hardest_preset", "player.hardest_used");
        SetLabel("expedition", "player.expedition_number");
        SetLabel("account_name", "player.account_name");
        SetLabel("outfit", "outfits.title");
        SetLabel("galaxy", "player.galaxy");
        SetLabel("portal_hex", "player.portal_code_hex");
        SetLabel("portal_dec", "player.portal_code_dec");
        SetLabel("portal_glyphs", "player.portal_glyphs");
        SetLabel("signal_booster", "player.signal_booster");
        SetLabel("player_state", "player.player_state");
        SetLabel("distance", "player.distance_to_center");
        SetLabel("jumps", "player.jumps_to_center");
        SetLabel("freighter_here", "player.freighter_in_system");
        SetLabel("nexus_here", "player.nexus_in_system");
        SetLabel("planets", "player.planets_in_system");
        SetLabel("warps_next", "player.warps_to_next");
        SetLabel("time_next", "player.time_to_next");
        SetLabel("galaxy_range", "player.galaxy_range");
        SetLabel("voxel_x", "player.voxel_x");
        SetLabel("voxel_y", "player.voxel_y");
        SetLabel("voxel_z", "player.voxel_z");
        SetLabel("solar_system", "player.solar_system");
        SetLabel("planet", "player.planet_range");
        SetLabel("glyphs", "player.glyphs_label");
        SetLabel("portal_input", "player.portal_code_hex");
        SetLabel("source", "player.source_label");
        SetLabel("destination", "player.dest_label");
        SetLabel("destination_platform", "player.dest_platform");
        SetLabel("guide_filter", "player.guide_filter");

        _thirdPersonCharacterCamera.Content = UiStrings.Get("player.third_person_camera");
        _portalInterference.Content = UiStrings.Get("player.portal_interference");
        _purpleWarpEnabled.Content = UiStrings.Get("player.purple_warp_enabled");
        _copySlot.Content = UiStrings.Get("player.copy_slot");
        _moveSlot.Content = UiStrings.Get("player.move_slot");
        _swapSlots.Content = UiStrings.Get("player.swap_slots");
        _deleteSlot.Content = UiStrings.Get("player.delete_slot");
        _transferPlatform.Content = UiStrings.Get("player.transfer_to_platform");
        _applyCoordinates.Content = UiStrings.Get("player.apply_coordinates");
        _convertPortal.Content = UiStrings.Get("player.convert_coords");
        _coordinateRoulette.Content = UiStrings.Get("player.coordinate_roulette");
        _triggerBattle.Content = UiStrings.Get("player.trigger_space_battle");
        _outfitExport.Content = UiStrings.Get("outfits.export");
        _outfitImport.Content = UiStrings.Get("outfits.import");
        _outfitCopy.Content = UiStrings.Get("outfits.copy_to_custom");
        _guideUnlockAll.Content = UiStrings.Get("player.guide_unlock_all");
        _guideLockAll.Content = UiStrings.Get("player.guide_lock_all");
        _titlesUnlockAll.Content = UiStrings.Get("player.titles_unlock_all");
        _titlesLockAll.Content = UiStrings.Get("player.titles_lock_all");
        _saveUtilityWarning.Text = UiStrings.Get("player.save_utils_warning");

        RefreshComboItems(_currentPreset, DifficultyPresetLocKeys);
        RefreshComboItems(_easiestPreset, DifficultyPresetLocKeys);
        RefreshComboItems(_hardestPreset, DifficultyPresetLocKeys);
        RefreshComboItems(_playerState, CoordinateHelper.PlayerStateLocKeys);
        RefreshComboItems(_destinationPlatform,
        [
            "player.platform_steam", "player.platform_gog", "player.platform_xbox",
            "player.platform_ps4", "player.platform_switch",
        ]);
        if (_destinationPlatform.SelectedIndex < 0 && _destinationPlatform.ItemCount > 0)
            _destinationPlatform.SelectedIndex = 0;

        for (int index = 0; index < GuideCategories.Length; index++)
        {
            string category = GuideCategories[index];
            if (_guideCategoryHeaders.TryGetValue(category, out TextBlock? header))
                header.Text = UiStrings.GetOrNull($"player.guide_cat_{index}") ?? category;
            if (_guideGrids.TryGetValue(category, out DataGrid? grid) && grid.Columns.Count >= 5)
            {
                grid.Columns[0].Header = UiStrings.Get("player.guide_col_icon");
                grid.Columns[1].Header = UiStrings.Get("player.guide_col_topic_id");
                grid.Columns[2].Header = UiStrings.Get("player.guide_col_name");
                grid.Columns[3].Header = UiStrings.Get("player.guide_col_seen");
                grid.Columns[4].Header = UiStrings.Get("player.guide_col_unlocked");
            }
        }
        if (_titlesGrid.Columns.Count >= 4)
        {
            _titlesGrid.Columns[0].Header = UiStrings.Get("player.titles_col_id");
            _titlesGrid.Columns[1].Header = UiStrings.Get("player.titles_col_title");
            _titlesGrid.Columns[2].Header = UiStrings.Get("player.titles_col_description");
            _titlesGrid.Columns[3].Header = UiStrings.Get("player.titles_col_unlocked");
        }

        if (_saveData is not null)
            RefreshCoordinateDisplay();
        if (_accountData is not null)
        {
            _loading = true;
            try
            {
                LoadGuides(_accountData);
                LoadTitles(_accountData);
            }
            finally
            {
                _loading = false;
            }
        }
    }

    private void SetSection(string id, string key)
    {
        if (_sections.TryGetValue(id, out TextBlock? block))
            block.Text = UiStrings.Get(key);
    }

    private void SetLabel(string id, string key)
    {
        if (_labels.TryGetValue(id, out TextBlock? block))
            block.Text = UiStrings.Get(key);
    }

    private static void RefreshComboItems(ComboBox combo, IReadOnlyList<string> localisationKeys)
    {
        int selected = combo.SelectedIndex;
        combo.Items.Clear();
        foreach (string key in localisationKeys)
            combo.Items.Add(UiStrings.Get(key));
        combo.SelectedIndex = selected >= 0 && selected < combo.ItemCount ? selected : -1;
    }
}
