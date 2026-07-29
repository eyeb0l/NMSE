using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Media;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Controls;

namespace NMSE.UI.Avalonia.Panels.Player;

public sealed partial class PlayerPanel
{
    public void LoadData(JsonObject saveData)
    {
        ArgumentNullException.ThrowIfNull(saveData);
        _saveData = saveData;
        _loading = true;
        try
        {
            JsonObject? playerState = saveData.GetObject("PlayerStateData");
            if (playerState is null)
                return;

            _rawStatValues = new Dictionary<string, decimal>
            {
                ["Health"] = MainStatsLogic.ReadRawStatValue(playerState, "Health"),
                ["Shield"] = MainStatsLogic.ReadRawStatValue(playerState, "Shield"),
                ["Energy"] = MainStatsLogic.ReadRawStatValue(playerState, "Energy"),
                ["Units"] = MainStatsLogic.ReadRawStatValue(playerState, "Units"),
                ["Nanites"] = MainStatsLogic.ReadRawStatValue(playerState, "Nanites"),
                ["Specials"] = MainStatsLogic.ReadRawStatValue(playerState, "Specials"),
            };
            SetStat(_healthField, playerState, "Health", 999999);
            SetStat(_shieldField, playerState, "Shield", 999999);
            SetStat(_energyField, playerState, "Energy", 999999);
            SetStat(_unitsField, playerState, "Units", uint.MaxValue);
            SetStat(_nanitesField, playerState, "Nanites", uint.MaxValue);
            SetStat(_quicksilverField, playerState, "Specials", uint.MaxValue);

            JsonObject? commonState = saveData.GetObject("CommonStateData");
            _saveNameField.Text = SafeString(commonState, "SaveName");
            _saveSummaryField.Text = SafeString(playerState, "SaveSummary");
            int totalSeconds = SafeInt(commonState, "TotalPlayTime");
            TimeSpan playTime = TimeSpan.FromSeconds(totalSeconds);
            _playTimeField.Text = UiStrings.Format("player.time_format", (int)playTime.TotalHours, playTime.Minutes, playTime.Seconds);
            _thirdPersonCharacterCamera.IsChecked = SafeBool(commonState, "UsesThirdPersonCharacterCam");

            PlayerName = ResolvePlayerName(saveData, commonState, playerState);
            _accountName.Text = PlayerName;
            LoadOutfits(playerState);
            LoadDifficulty(playerState);
            LoadExpedition(saveData, playerState);
            LoadCoordinates(playerState, saveData);
            LoadSpaceBattle(playerState, saveData);
            if (_accountData is not null)
                LoadTitles(_accountData);
        }
        finally
        {
            _loading = false;
        }
    }

    public void SaveData(JsonObject saveData)
    {
        ArgumentNullException.ThrowIfNull(saveData);
        CommitNumericFields();
        JsonObject? playerState = saveData.GetObject("PlayerStateData");
        if (playerState is null)
            return;

        MainStatsLogic.WriteStatValues(
            playerState,
            Decimal(_healthField), Decimal(_shieldField), Decimal(_energyField),
            Decimal(_unitsField), Decimal(_nanitesField), Decimal(_quicksilverField),
            _rawStatValues);

        saveData.GetObject("CommonStateData")?.Set("SaveName", _saveNameField.Text ?? string.Empty);
        playerState.Set("SaveSummary", _saveSummaryField.Text ?? string.Empty);
        saveData.GetObject("CommonStateData")?.Set("UsesThirdPersonCharacterCam", _thirdPersonCharacterCamera.IsChecked == true);

        if (!SaveContext.IsExpeditionSave)
        {
            JsonObject? difficulty = playerState.GetObject("DifficultyState");
            SetPreset(difficulty?.GetObject("Preset"), _currentPreset);
            SetPreset(difficulty?.GetObject("EasiestUsedPreset"), _easiestPreset);
            SetPreset(difficulty?.GetObject("HardestUsedPreset"), _hardestPreset);
        }

        if (_playerState.SelectedIndex >= 0 && _playerState.SelectedIndex < CoordinateHelper.PlayerStates.Length)
            saveData.GetObject("SpawnStateData")?.Set("LastKnownPlayerState", CoordinateHelper.PlayerStates[_playerState.SelectedIndex]);

        playerState.Set("OnOtherSideOfPortal", _portalInterference.IsChecked == true);
        SavePurpleWarpState(playerState);
        SaveSpaceBattle(playerState);
        SaveCoordinates(playerState);
    }

    public void LoadAccountData(JsonObject accountData)
    {
        ArgumentNullException.ThrowIfNull(accountData);
        _accountData = accountData;
        _loading = true;
        try
        {
            LoadGuides(accountData);
            LoadTitles(accountData);
        }
        finally
        {
            _loading = false;
        }
    }

    public void SetAccountData(JsonObject? accountData)
    {
        if (accountData is not null)
        {
            LoadAccountData(accountData);
            return;
        }

        _accountData = null;
        _loading = true;
        try
        {
            foreach (GuideRow row in _guideRows)
                row.PropertyChanged -= OnGuideRowChanged;
            foreach (TitleRow row in _titleRows)
                row.PropertyChanged -= OnTitleRowChanged;
            _guideRows.Clear();
            _titleRows.Clear();
            RefreshGuideFilter();
        }
        finally
        {
            _loading = false;
        }
    }

    public void SaveAccountData(JsonObject accountData)
    {
        ArgumentNullException.ThrowIfNull(accountData);
        _accountData = accountData;
        JsonObject guideData = accountData.GetObject("UserSettingsData") ?? accountData;
        JsonArray seen = GetOrCreateArray(guideData, "SeenWikiTopics");
        JsonArray guides = GetOrCreateArray(guideData, "UnlockedWikiTopics");
        foreach (GuideRow row in _guideRows)
        {
            SetArrayMembership(seen, row.TopicId, row.Seen, prefix: string.Empty);
            SetArrayMembership(guides, row.TopicId, row.Unlocked, prefix: string.Empty);
        }

        JsonObject titleData = accountData.GetObject("UserSettingsData") ?? accountData;
        JsonArray titles = GetOrCreateArray(titleData, "UnlockedTitles");
        foreach (TitleRow row in _titleRows)
            SetArrayMembership(titles, row.TitleId, row.Unlocked, prefix: "^");
    }

    private void SetStat(InvariantNumericTextBox control, JsonObject playerState, string key, decimal maximum)
    {
        decimal value = MainStatsLogic.ReadStatValue(playerState, key, 0, maximum);
        control.NumericValue = (double)value;
    }

    private void LoadOutfits(JsonObject playerState)
    {
        int selected = _outfitSelector.SelectedIndex;
        _outfitSelector.Items.Clear();
        JsonArray? outfits = playerState.GetArray("Outfits");
        JsonArray? names = playerState.GetArray("OutfitNames");
        if (outfits is not null)
        {
            for (int index = 0; index < outfits.Length; index++)
            {
                JsonObject? outfit = outfits.GetObject(index);
                if (outfit is not null)
                    _outfitSelector.Items.Add(OutfitLogic.GetOutfitDisplayName(names, outfit, index));
            }
        }
        _outfitSelector.SelectedIndex = _outfitSelector.ItemCount == 0 ? -1 : Math.Clamp(selected, 0, _outfitSelector.ItemCount - 1);
    }

    private void LoadDifficulty(JsonObject playerState)
    {
        JsonObject? state = playerState.GetObject("DifficultyState");
        SelectPreset(_currentPreset, SafeString(state?.GetObject("Preset"), "DifficultyPresetType"));
        SelectPreset(_easiestPreset, SafeString(state?.GetObject("EasiestUsedPreset"), "DifficultyPresetType"));
        SelectPreset(_hardestPreset, SafeString(state?.GetObject("HardestUsedPreset"), "DifficultyPresetType"));
    }

    private void LoadExpedition(JsonObject saveData, JsonObject playerState)
    {
        bool expedition = SaveContext.IsExpeditionSave;
        _expeditionNumber.IsVisible = expedition;
        if (_labels.TryGetValue("expedition", out TextBlock? label))
            label.IsVisible = expedition;
        _currentPreset.IsEnabled = !expedition;
        _easiestPreset.IsEnabled = !expedition;
        _hardestPreset.IsEnabled = !expedition;
        if (!expedition)
        {
            _expeditionNumber.Text = string.Empty;
            return;
        }

        int season = SafeInt(playerState, "StartingSeasonNumber");
        string display = season > 0 ? UiStrings.Format("player.expedition_format", season) : string.Empty;
        JsonObject? seasonData = saveData.GetObject("CommonStateData")?.GetObject("SeasonData");
        if (string.IsNullOrEmpty(display))
            display = SafeString(seasonData, "DisplayNumber");
        if (string.IsNullOrEmpty(display))
        {
            int fallback = SafeInt(seasonData, "SeasonNumber");
            if (fallback > 0)
                display = UiStrings.Format("player.expedition_format", fallback);
        }
        _expeditionNumber.Text = display;
    }

    private void LoadCoordinates(JsonObject playerState, JsonObject saveData)
    {
        JsonObject? address = playerState.GetObject("UniverseAddress");
        JsonObject? galactic = address?.GetObject("GalacticAddress");
        if (address is null || galactic is null)
            return;

        int reality = SafeInt(address, "RealityIndex");
        int x = SafeInt(galactic, "VoxelX");
        int y = SafeInt(galactic, "VoxelY");
        int z = SafeInt(galactic, "VoxelZ");
        int system = SafeInt(galactic, "SolarSystemIndex");
        int planet = SafeInt(galactic, "PlanetIndex");
        _rawCoordinateValues = new Dictionary<string, int>
        {
            ["RealityIndex"] = reality,
            ["VoxelX"] = x,
            ["VoxelY"] = y,
            ["VoxelZ"] = z,
            ["SolarSystemIndex"] = system,
            ["PlanetIndex"] = planet,
        };

        _galaxyNumber.NumericValue = Math.Clamp(reality + 1, 1, 257);
        _voxelX.NumericValue = Math.Clamp(x, -2048, 2047);
        _voxelY.NumericValue = Math.Clamp(y, -128, 127);
        _voxelZ.NumericValue = Math.Clamp(z, -2048, 2047);
        _solarSystem.NumericValue = Math.Clamp(system, 0, 4095);
        _planet.NumericValue = Math.Clamp(planet, 0, 15);
        RefreshCoordinateDisplay();

        string currentState = SafeString(saveData.GetObject("SpawnStateData"), "LastKnownPlayerState");
        _playerState.SelectedIndex = Array.IndexOf(CoordinateHelper.PlayerStates, currentState);
        _freighterInSystem.Text = IsSameSystem(playerState.GetObject("FreighterUniverseAddress"), reality, x, y, z, system)
            ? UiStrings.Get("common.yes") : UiStrings.Get("common.no");
        _nexusInSystem.Text = IsSameSystem(playerState.GetObject("NexusUniverseAddress"), reality, x, y, z, system)
            ? UiStrings.Get("common.yes") : UiStrings.Get("common.no");
        _planetsInSystem.Text = CountPlanets(playerState).ToString(CultureInfo.CurrentCulture);
        _portalInterference.IsChecked = SafeBool(playerState, "OnOtherSideOfPortal");
        _purpleWarpEnabled.IsChecked = SafeBool(playerState, "HasDiscoveredPurpleSystems") && ArrayContains(playerState.GetArray("KnownTech"), "^HDRIVEBOOST4");
    }

    private void RefreshCoordinateDisplay()
    {
        int reality = Integer(_galaxyNumber, 1) - 1;
        int x = Integer(_voxelX);
        int y = Integer(_voxelY);
        int z = Integer(_voxelZ);
        int system = Integer(_solarSystem);
        int planet = Integer(_planet);
        _galaxy.Text = $"{GalaxyDatabase.GetGalaxyDisplayName(reality)} ({GalaxyDatabase.GetGalaxyType(reality)})";
        try
        {
            _galaxyCoreDot.Background = new SolidColorBrush(Color.Parse(GalaxyDatabase.GetGalaxyCoreColor(reality)));
        }
        catch
        {
            _galaxyCoreDot.Background = Brushes.Black;
        }

        string code = CoordinateHelper.VoxelToPortalCode(x, y, z, system, planet);
        _portalHex.Text = code;
        _portalDecimal.Text = CoordinateHelper.PortalHexToDec(code);
        _portalGlyphs.PortalCode = code;
        _signalBooster.Text = CoordinateHelper.VoxelToSignalBooster(x, y, z, system);
        double distance = CoordinateHelper.GetDistanceToCenter(x, y, z);
        _distanceToCenter.Text = $"{distance.ToString("F0", CultureInfo.InvariantCulture)} ly";
        _jumpsToCenter.Text = CoordinateHelper.GetJumpsToCenter(distance, CoordinateHelper.DefaultHyperdriveRange).ToString(CultureInfo.CurrentCulture);
    }

    private void LoadSpaceBattle(JsonObject playerState, JsonObject saveData)
    {
        int totalPlayTime = SafeInt(saveData.GetObject("CommonStateData"), "TotalPlayTime");
        int timeLastBattle = SafeInt(playerState, "TimeLastSpaceBattle");
        int timeRemaining = Math.Clamp(CoordinateHelper.SpaceBattleIntervalSeconds - (totalPlayTime - timeLastBattle), 0, CoordinateHelper.SpaceBattleIntervalSeconds);
        TimeSpan remaining = TimeSpan.FromSeconds(timeRemaining);
        _timeToNextBattle.Text = UiStrings.Format("player.time_format", (int)remaining.TotalHours, remaining.Minutes, remaining.Seconds);

        int warpsLastBattle = SafeInt(playerState, "WarpsLastSpaceBattle");
        int warpsRemaining = Math.Max(0, CoordinateHelper.SpaceBattleIntervalWarps - (ReadTotalWarps(playerState) - warpsLastBattle));
        _warpsToNextBattle.NumericValue = Math.Min(999, warpsRemaining);
    }

    private void SaveCoordinates(JsonObject playerState)
    {
        JsonObject? address = playerState.GetObject("UniverseAddress");
        JsonObject? galactic = address?.GetObject("GalacticAddress");
        if (address is null || galactic is null)
            return;

        int reality = Integer(_galaxyNumber, 1) - 1;
        if (_rawCoordinateValues is null || !_rawCoordinateValues.TryGetValue("RealityIndex", out int rawReality) || reality != rawReality)
            address.Set("RealityIndex", reality);
        WriteCoordinate(galactic, "VoxelX", Integer(_voxelX), _voxelX);
        WriteCoordinate(galactic, "VoxelY", Integer(_voxelY), _voxelY);
        WriteCoordinate(galactic, "VoxelZ", Integer(_voxelZ), _voxelZ);
        WriteCoordinate(galactic, "SolarSystemIndex", Integer(_solarSystem), _solarSystem);
        WriteCoordinate(galactic, "PlanetIndex", Integer(_planet), _planet);
    }

    private void WriteCoordinate(JsonObject target, string key, int value, InvariantNumericTextBox control)
    {
        if (_rawCoordinateValues is not null && _rawCoordinateValues.TryGetValue(key, out int raw))
        {
            int clamped = Math.Clamp(raw, (int)(control.Minimum ?? int.MinValue), (int)(control.Maximum ?? int.MaxValue));
            if (value == clamped)
                return;
        }
        RawNumberGuard.SetInt(target, key, value);
    }

    private void SavePurpleWarpState(JsonObject playerState)
    {
        bool enabled = _purpleWarpEnabled.IsChecked == true;
        playerState.Set("HasDiscoveredPurpleSystems", enabled);
        JsonArray? knownTech = playerState.GetArray("KnownTech");
        if (knownTech is null)
            return;
        int index = knownTech.IndexOf("^HDRIVEBOOST4");
        if (enabled && index < 0)
            knownTech.Add("^HDRIVEBOOST4");
        else if (!enabled && index >= 0)
            knownTech.RemoveAt(index);
    }

    private void SaveSpaceBattle(JsonObject playerState)
    {
        int warpsToNext = Integer(_warpsToNextBattle);
        int lastBattle = ReadTotalWarps(playerState) - CoordinateHelper.SpaceBattleIntervalWarps + warpsToNext;
        playerState.Set("WarpsLastSpaceBattle", Math.Max(0, lastBattle));
    }

    private void LoadGuides(JsonObject accountData)
    {
        foreach (GuideRow row in _guideRows)
            row.PropertyChanged -= OnGuideRowChanged;
        _guideRows.Clear();

        JsonObject? userData = accountData.GetObject("UserSettingsData");
        if (userData is null)
        {
            RefreshGuideFilter();
            return;
        }
        HashSet<string> seen = ReadStringSet(userData.GetArray("SeenWikiTopics"));
        HashSet<string> unlocked = ReadStringSet(userData.GetArray("UnlockedWikiTopics"));
        var shown = new HashSet<string>(StringComparer.Ordinal);
        foreach (WikiGuideTopic topic in WikiGuideDatabase.Topics)
        {
            shown.Add(topic.Id);
            AddGuideRow(topic.Id, topic.Name, WikiGuideDatabase.GetEnglishCategory(topic.Id), seen.Contains(topic.Id), unlocked.Contains(topic.Id));
        }
        foreach (string id in seen.Union(unlocked))
        {
            if (shown.Add(id) && !string.IsNullOrWhiteSpace(id))
                AddGuideRow(id, WikiGuideDatabase.GetTopicName(id), WikiGuideDatabase.GetEnglishCategory(id), seen.Contains(id), unlocked.Contains(id));
        }
        RefreshGuideFilter();
    }

    private void AddGuideRow(string id, string name, string category, bool seen, bool unlocked)
    {
        if (!_guideViews.ContainsKey(category))
            category = GuideCategories[^1];
        string iconKey = WikiGuideDatabase.GetTopicIconKey(id);
        var row = new GuideRow
        {
            TopicId = id,
            TopicName = name,
            Category = category,
            Seen = seen,
            Unlocked = unlocked,
            Icon = string.IsNullOrWhiteSpace(iconKey) ? null : _iconProvider?.GetIcon(iconKey + ".png") ?? _iconProvider?.GetIcon(iconKey),
        };
        row.PropertyChanged += OnGuideRowChanged;
        _guideRows.Add(row);
    }

    private void RefreshGuideFilter()
    {
        string filter = (_guideFilter.Text ?? string.Empty).Trim();
        foreach ((string category, ObservableCollection<GuideRow> view) in _guideViews)
        {
            view.Clear();
            foreach (GuideRow row in _guideRows.Where(row => row.Category == category &&
                         (filter.Length == 0 || row.TopicId.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                          row.TopicName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                          row.Category.Contains(filter, StringComparison.OrdinalIgnoreCase))))
                view.Add(row);
            if (_guideGrids.TryGetValue(category, out DataGrid? grid))
                grid.Height = Math.Clamp(54 + (view.Count * 34), 90, 420);
        }
    }

    private void OnGuideRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading || sender is not GuideRow row || _accountData is null)
            return;
        JsonObject? userData = _accountData.GetObject("UserSettingsData");
        if (userData is null)
            return;
        if (e.PropertyName == nameof(GuideRow.Seen) && userData.GetArray("SeenWikiTopics") is { } seen)
            SetArrayMembership(seen, row.TopicId, row.Seen, string.Empty);
        else if (e.PropertyName == nameof(GuideRow.Unlocked) && userData.GetArray("UnlockedWikiTopics") is { } unlocked)
            SetArrayMembership(unlocked, row.TopicId, row.Unlocked, string.Empty);
        RaiseDataModified();
    }

    private void LoadTitles(JsonObject accountData)
    {
        foreach (TitleRow row in _titleRows)
            row.PropertyChanged -= OnTitleRowChanged;
        _titleRows.Clear();
        if (!TitleDatabase.IsLoaded)
            return;

        JsonObject userData = accountData.GetObject("UserSettingsData") ?? accountData;
        HashSet<string> unlocked = ReadStringSet(userData.GetArray("UnlockedTitles"), trimPrefix: true);
        foreach (TitleEntry title in TitleDatabase.Titles)
        {
            string name;
            try { name = string.Format(CultureInfo.CurrentCulture, title.Name, PlayerName); }
            catch (FormatException) { name = title.Name; }
            var row = new TitleRow
            {
                TitleId = title.Id,
                TitleName = name,
                Description = title.UnlockDescription,
                Unlocked = unlocked.Contains(title.Id),
            };
            row.PropertyChanged += OnTitleRowChanged;
            _titleRows.Add(row);
        }
    }

    private void OnTitleRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading || e.PropertyName != nameof(TitleRow.Unlocked) || sender is not TitleRow row || _accountData is null)
            return;
        JsonObject userData = _accountData.GetObject("UserSettingsData") ?? _accountData;
        if (userData.GetArray("UnlockedTitles") is { } titles)
            SetArrayMembership(titles, row.TitleId, row.Unlocked, "^");
        RaiseDataModified();
    }

    private static HashSet<string> ReadStringSet(JsonArray? array, bool trimPrefix = false)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (array is null)
            return result;
        for (int index = 0; index < array.Length; index++)
        {
            string? value = ExtractStringValue(array.Get(index));
            if (trimPrefix && value?.StartsWith('^') == true)
                value = value[1..];
            if (!string.IsNullOrWhiteSpace(value))
                result.Add(value);
        }
        return result;
    }

    private static void SetArrayMembership(JsonArray array, string id, bool included, string prefix)
    {
        int found = -1;
        for (int index = 0; index < array.Length; index++)
        {
            string? value = ExtractStringValue(array.Get(index));
            if (string.Equals(value, id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, prefix + id, StringComparison.OrdinalIgnoreCase))
            {
                found = index;
                break;
            }
        }
        if (included && found < 0)
            array.Add(prefix + id);
        else if (!included && found >= 0)
            array.RemoveAt(found);
    }

    private static JsonArray GetOrCreateArray(JsonObject target, string key)
    {
        JsonArray? array = target.GetArray(key);
        if (array is not null)
            return array;
        array = new JsonArray();
        target.Set(key, array);
        return array;
    }

    private static string ResolvePlayerName(JsonObject saveData, JsonObject? commonState, JsonObject playerState)
    {
        try
        {
            JsonArray? owners = commonState?.GetArray("UsedDiscoveryOwnersV2");
            if (owners is not { Length: > 0 })
                return UiStrings.Get("player.name_fallback");

            int primaryIndex = 0;
            string platform = SafeString(saveData, "Platform");
            string expectedPlatformToken = platform.StartsWith("Win", StringComparison.OrdinalIgnoreCase) ? "ST"
                : platform.StartsWith("PS", StringComparison.OrdinalIgnoreCase) ? "PS"
                : platform.StartsWith("XB", StringComparison.OrdinalIgnoreCase) ? "XB"
                : string.Empty;
            if (expectedPlatformToken.Length > 0)
            {
                for (int index = 0; index < owners.Length; index++)
                {
                    if (string.Equals(SafeString(owners.GetObject(index), "PTK"), expectedPlatformToken, StringComparison.Ordinal))
                    {
                        primaryIndex = index;
                        break;
                    }
                }
            }

            JsonObject? primaryOwner = owners.GetObject(primaryIndex);
            string name = SafeString(primaryOwner, "USN");
            if (name.Length > 0)
                return name;

            string uid = SafeString(primaryOwner, "UID");
            if (uid.Length == 0 || playerState.GetArray("PersistentPlayerBases") is not { } bases)
                return UiStrings.Get("player.name_fallback");

            for (int index = 0; index < bases.Length; index++)
            {
                JsonObject? owner = bases.GetObject(index)?.GetObject("Owner");
                if (string.Equals(SafeString(owner, "UID"), uid, StringComparison.Ordinal) &&
                    SafeString(owner, "USN") is { Length: > 0 } baseOwnerName)
                    return baseOwnerName;
            }
        }
        catch
        {
        }

        return UiStrings.Get("player.name_fallback");
    }

    private static bool IsSameSystem(JsonObject? address, int reality, int x, int y, int z, int system)
    {
        JsonObject? galactic = address?.GetObject("GalacticAddress");
        return address is not null && galactic is not null && SafeInt(address, "RealityIndex") == reality &&
               SafeInt(galactic, "VoxelX") == x && SafeInt(galactic, "VoxelY") == y &&
               SafeInt(galactic, "VoxelZ") == z && SafeInt(galactic, "SolarSystemIndex") == system;
    }

    private static int CountPlanets(JsonObject playerState)
    {
        int count = 0;
        JsonArray? seeds = playerState.GetArray("PlanetSeeds");
        if (seeds is null)
            return count;
        for (int index = 0; index < seeds.Length; index++)
        {
            JsonArray? seed = seeds.GetArray(index);
            if (seed is { Length: >= 2 } && seed.Get(1)?.ToString() != "0x0")
                count++;
        }
        return count;
    }

    private static int ReadTotalWarps(JsonObject playerState)
    {
        JsonArray? groups = playerState.GetArray("Stats");
        if (groups is null)
            return 0;
        for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            JsonObject? group = groups.GetObject(groupIndex);
            if (SafeString(group, "GroupId") != "^GLOBAL_STATS")
                continue;
            JsonArray? stats = group?.GetArray("Stats");
            if (stats is null)
                return 0;
            for (int statIndex = 0; statIndex < stats.Length; statIndex++)
            {
                JsonObject? stat = stats.GetObject(statIndex);
                if (SafeString(stat, "Id") == "^DIST_WARP")
                    return SafeInt(stat?.GetObject("Value"), "IntValue");
            }
            return 0;
        }
        return 0;
    }

    private static string? ExtractStringValue(object? value) => value switch
    {
        string text => text,
        BinaryData binary => Encoding.Latin1.GetString(binary.ToByteArray()),
        null => null,
        _ => value.ToString(),
    };

    private static bool ArrayContains(JsonArray? array, string value)
    {
        if (array is null)
            return false;
        for (int index = 0; index < array.Length; index++)
            if (string.Equals(ExtractStringValue(array.Get(index)), value, StringComparison.Ordinal))
                return true;
        return false;
    }

    private void CommitNumericFields()
    {
        foreach (InvariantNumericTextBox field in new[]
                 { _healthField, _shieldField, _energyField, _unitsField, _nanitesField, _quicksilverField,
                   _warpsToNextBattle, _galaxyNumber, _voxelX, _voxelY, _voxelZ, _solarSystem, _planet })
            field.TryCommit();
    }

    private static int Integer(InvariantNumericTextBox control, int fallback = 0) => (int)(control.NumericValue ?? fallback);
    private static decimal Decimal(InvariantNumericTextBox control) => (decimal)(control.NumericValue ?? 0);
    private static int SafeInt(JsonObject? target, string key) { try { return target?.GetInt(key) ?? 0; } catch { return 0; } }
    private static bool SafeBool(JsonObject? target, string key) { try { return target?.GetBool(key) ?? false; } catch { return false; } }
    private static string SafeString(JsonObject? target, string key) { try { return target?.GetString(key) ?? string.Empty; } catch { return string.Empty; } }

    private static void SelectPreset(ComboBox combo, string value) => combo.SelectedIndex = Array.IndexOf(DifficultyPresets, value);

    private static void SetPreset(JsonObject? target, ComboBox combo)
    {
        if (target is not null && combo.SelectedIndex >= 0 && combo.SelectedIndex < DifficultyPresets.Length)
            target.Set("DifficultyPresetType", DifficultyPresets[combo.SelectedIndex]);
    }
}
