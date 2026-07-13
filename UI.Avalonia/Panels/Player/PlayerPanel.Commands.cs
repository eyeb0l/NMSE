using Avalonia.Controls;
using Avalonia.Input;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.IO;
using NMSE.Models;
using NMSE.UI.Avalonia.Platform;

namespace NMSE.UI.Avalonia.Panels.Player;

public sealed partial class PlayerPanel
{
    private void WireCommands()
    {
        _applyCoordinates.Click += OnApplyCoordinates;
        _convertPortal.Click += OnConvertPortal;
        _coordinateRoulette.Click += OnCoordinateRoulette;
        _triggerBattle.Click += OnTriggerBattle;
        _portalInput.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                args.Handled = true;
                OnConvertPortal(this, EventArgs.Empty);
            }
        };

        _copySlot.Click += async (_, _) => await RunSlotOperationAsync("copy");
        _moveSlot.Click += async (_, _) => await RunSlotOperationAsync("move");
        _swapSlots.Click += async (_, _) => await RunSlotOperationAsync("swap");
        _deleteSlot.Click += async (_, _) => await RunSlotOperationAsync("delete");
        _transferPlatform.Click += OnTransferPlatform;
        _outfitExport.Click += OnOutfitExport;
        _outfitImport.Click += OnOutfitImport;
        _outfitCopy.Click += OnOutfitCopy;

        _guideUnlockAll.Click += (_, _) => SetAllGuides(true);
        _guideLockAll.Click += (_, _) => SetAllGuides(false);
        _titlesUnlockAll.Click += (_, _) => SetAllTitles(true);
        _titlesLockAll.Click += (_, _) => SetAllTitles(false);

        bool hasDesktop = _desktopServices is not null;
        foreach (Button button in new[]
                 { _copySlot, _moveSlot, _swapSlots, _deleteSlot, _transferPlatform, _outfitExport, _outfitImport, _outfitCopy })
            button.IsEnabled = hasDesktop;
    }

    private void OnGlyphClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { Tag: char value } || (_portalInput.Text?.Length ?? 0) >= 12)
            return;
        string text = _portalInput.Text ?? string.Empty;
        int insertion = Math.Clamp(_portalInput.CaretIndex, 0, text.Length);
        _portalInput.Text = text.Insert(insertion, value.ToString());
        _portalInput.CaretIndex = insertion + 1;
        _portalInput.Focus();
    }

    private void OnApplyCoordinates(object? sender, EventArgs e)
    {
        CommitNumericFields();
        if (_saveData?.GetObject("PlayerStateData") is { } playerState)
            SaveCoordinates(playerState);
        RefreshCoordinateDisplay();
        RaiseDataModified();
    }

    private async void OnConvertPortal(object? sender, EventArgs e)
    {
        string code = (_portalInput.Text ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0)
        {
            await ShowMessageAsync(UiStrings.Get("player.invalid_portal_title"), UiStrings.Get("player.portal_code_msg_12"));
            return;
        }
        if (!CoordinateHelper.PortalCodeToVoxel(code, out int x, out int y, out int z, out int system, out int planet))
        {
            await ShowMessageAsync(UiStrings.Get("player.invalid_portal_title"), UiStrings.Get("player.invalid_portal_format"));
            return;
        }
        _voxelX.NumericValue = Math.Clamp(x, -2048, 2047);
        _voxelY.NumericValue = Math.Clamp(y, -128, 127);
        _voxelZ.NumericValue = Math.Clamp(z, -2048, 2047);
        _solarSystem.NumericValue = Math.Clamp(system, 0, 4095);
        _planet.NumericValue = Math.Clamp(planet, 0, 15);
    }

    private async void OnCoordinateRoulette(object? sender, EventArgs e)
    {
        const string digits = "0123456789ABCDEF";
        string code = new(Enumerable.Range(0, 12).Select(_ => digits[Random.Shared.Next(16)]).ToArray());
        int galaxy = Random.Shared.Next(256);
        if (!CoordinateHelper.PortalCodeToVoxel(code, out int x, out int y, out int z, out int system, out int planet))
        {
            await ShowMessageAsync(UiStrings.Get("player.roulette_title"), UiStrings.Get("player.roulette_failed"));
            return;
        }
        if (_desktopServices is not null && !await _desktopServices.ConfirmAsync(
                UiStrings.Get("player.roulette_title"),
                UiStrings.Format("player.roulette_confirm", code, GalaxyDatabase.GetGalaxyDisplayName(galaxy), galaxy)))
            return;

        _galaxyNumber.NumericValue = galaxy + 1;
        _voxelX.NumericValue = Math.Clamp(x, -2048, 2047);
        _voxelY.NumericValue = Math.Clamp(y, -128, 127);
        _voxelZ.NumericValue = Math.Clamp(z, -2048, 2047);
        _solarSystem.NumericValue = Math.Clamp(system, 0, 4095);
        _planet.NumericValue = Math.Clamp(planet, 0, 15);
        OnApplyCoordinates(sender, e);
    }

    private async void OnTriggerBattle(object? sender, EventArgs e)
    {
        if (_saveData?.GetObject("PlayerStateData") is not { } playerState)
            return;
        playerState.Set("TimeLastSpaceBattle", 0);
        playerState.Set("WarpsLastSpaceBattle", 0);
        _warpsToNextBattle.NumericValue = 0;
        _timeToNextBattle.Text = UiStrings.Format("player.time_format", 0, 0, 0);
        RaiseDataModified();
        await ShowMessageAsync(UiStrings.Get("player.space_battle_title"), UiStrings.Get("player.space_battle_triggered"));
    }

    private async Task RunSlotOperationAsync(string operation)
    {
        if (_desktopServices is null)
            return;
        string? directory = _saveFilePath is null ? null : Path.GetDirectoryName(_saveFilePath);
        if (directory is null)
        {
            await ShowMessageAsync(UiStrings.Get("player.save_utils_title"), UiStrings.Get("player.no_save_loaded"));
            return;
        }

        int source = _sourceSlot.SelectedIndex;
        int destination = _destinationSlot.SelectedIndex;
        if (source < 0 || (operation != "delete" && destination < 0))
            return;
        if (operation != "delete" && source == destination)
        {
            await ShowMessageAsync(OperationTitle(operation), UiStrings.Get("player.slots_must_differ"));
            return;
        }

        string confirmation = operation switch
        {
            "copy" => UiStrings.Format("player.copy_slot_confirm", source + 1, destination + 1),
            "move" => UiStrings.Format("player.move_slot_confirm", source + 1, destination + 1),
            "swap" => UiStrings.Format("player.swap_slot_confirm", source + 1, destination + 1),
            "delete" => UiStrings.Format("player.delete_slot_confirm", source + 1),
            _ => string.Empty,
        };
        if (!await _desktopServices.ConfirmAsync(OperationTitle(operation), confirmation))
            return;

        try
        {
            SaveFileManager.Platform platform = SaveFileManager.DetectPlatform(directory);
            await Task.Run(() =>
            {
                switch (operation)
                {
                    case "copy": SaveSlotManager.CopySlot(directory, source, destination, platform); break;
                    case "move": SaveSlotManager.MoveSlot(directory, source, destination, platform); break;
                    case "swap": SaveSlotManager.SwapSlots(directory, source, destination, platform); break;
                    case "delete": SaveSlotManager.DeleteSlot(directory, source, platform); break;
                }
            });
            string success = operation switch
            {
                "copy" => UiStrings.Format("player.copy_slot_success", source + 1, destination + 1),
                "move" => UiStrings.Format("player.move_slot_success", source + 1, destination + 1),
                "swap" => UiStrings.Format("player.swap_slot_success", source + 1, destination + 1),
                "delete" => UiStrings.Format("player.delete_slot_success", source + 1),
                _ => string.Empty,
            };
            await ShowMessageAsync(OperationTitle(operation), success);
            bool reload = operation is "move" or "swap" or "delete";
            SaveUtilityCompleted?.Invoke(this, new SaveUtilityCompletedEventArgs(operation, source, operation == "delete" ? null : destination, reload));
            if (reload)
                ReloadRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            string failure = operation switch
            {
                "copy" => UiStrings.Format("player.copy_slot_failed", ex.Message),
                "move" => UiStrings.Format("player.move_slot_failed", ex.Message),
                "swap" => UiStrings.Format("player.swap_slot_failed", ex.Message),
                "delete" => UiStrings.Format("player.delete_slot_failed", ex.Message),
                _ => ex.Message,
            };
            await ShowMessageAsync(OperationTitle(operation), failure);
        }
    }

    private async void OnTransferPlatform(object? sender, EventArgs e)
    {
        if (_desktopServices is null || _saveFilePath is null || _saveData is null)
        {
            await ShowMessageAsync(UiStrings.Get("player.save_utils_title"), UiStrings.Get("player.no_save_loaded"));
            return;
        }
        int destinationSlot = _destinationSlot.SelectedIndex;
        int platformIndex = _destinationPlatform.SelectedIndex;
        if (destinationSlot < 0)
        {
            await ShowMessageAsync(UiStrings.Get("player.transfer_label"), UiStrings.Get("player.transfer_select_dest"));
            return;
        }
        if (platformIndex < 0)
            return;

        string? destination = await _desktopServices.PickFolderAsync(UiStrings.Get("player.transfer_dest_folder"));
        if (string.IsNullOrWhiteSpace(destination))
            return;
        string platformName = _destinationPlatform.SelectedItem?.ToString() ?? string.Empty;
        if (!await _desktopServices.ConfirmAsync(
                UiStrings.Get("player.transfer_cross_title"),
                UiStrings.Format("player.transfer_cross_confirm", platformName, destinationSlot + 1, destination)))
            return;

        try
        {
            SaveFileManager.Platform platform = PlatformFromIndex(platformIndex);
            await Task.Run(() => SaveSlotManager.TransferCrossPlatform(_saveFilePath, destination, destinationSlot, platform));
            await ShowMessageAsync(UiStrings.Get("player.transfer_cross_title"), UiStrings.Get("player.transfer_cross_complete"));
            SaveUtilityCompleted?.Invoke(this, new SaveUtilityCompletedEventArgs("transfer", _sourceSlot.SelectedIndex, destinationSlot, false));
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(UiStrings.Get("player.transfer_cross_title"), UiStrings.Format("player.transfer_cross_failed", ex.Message));
        }
    }

    private async void OnOutfitExport(object? sender, EventArgs e)
    {
        if (_desktopServices is null || SelectedOutfit() is not { } outfit)
            return;
        ExportConfig config = ExportConfig.Instance;
        string name = _outfitSelector.SelectedItem?.ToString() ?? UiStrings.Get("outfits.title");
        string suggested = ExportConfig.BuildFileName(config.OutfitTemplate, config.OutfitExt,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["name"] = name });
        string? path = await _desktopServices.PickSaveFileAsync(
            UiStrings.Get("outfits.export"), suggested, config.OutfitExt.TrimStart('.'), OutfitFileTypes(config.OutfitExt));
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            OutfitLogic.ExportOutfit(outfit, path);
            await ShowMessageAsync(UiStrings.Get("outfits.export"), UiStrings.Get("outfits.export_success"));
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(UiStrings.Get("outfits.export"), ex.Message);
        }
    }

    private async void OnOutfitImport(object? sender, EventArgs e)
    {
        if (_desktopServices is null || _saveData?.GetObject("PlayerStateData")?.GetArray("Outfits") is not { } outfits)
            return;
        int index = _outfitSelector.SelectedIndex;
        if (index < 0)
            return;
        string extension = ExportConfig.Instance.OutfitExt;
        IReadOnlyList<string> paths = await _desktopServices.PickFilesAsync(
            UiStrings.Get("outfits.import"), false, OutfitFileTypes(extension));
        if (paths.Count == 0)
            return;
        try
        {
            OutfitLogic.ImportOutfit(outfits, index, paths[0]);
            RaiseDataModified();
            await ShowMessageAsync(UiStrings.Get("outfits.import"), UiStrings.Get("outfits.import_success"));
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(UiStrings.Get("outfits.import"), ex.Message);
        }
    }

    private async void OnOutfitCopy(object? sender, EventArgs e)
    {
        if (_desktopServices is null || _saveData?.GetObject("PlayerStateData") is not { } playerState || SelectedOutfit() is not { } outfit)
            return;
        if (!await _desktopServices.ConfirmAsync(UiStrings.Get("outfits.copy_confirm_title"), UiStrings.Get("outfits.copy_confirm_msg")))
            return;
        OutfitLogic.CopyToCustomData(outfit, playerState);
        RaiseDataModified();
        await ShowMessageAsync(UiStrings.Get("outfits.copy_confirm_title"), UiStrings.Get("outfits.copy_success"));
    }

    private JsonObject? SelectedOutfit()
    {
        int index = _outfitSelector.SelectedIndex;
        JsonArray? outfits = _saveData?.GetObject("PlayerStateData")?.GetArray("Outfits");
        return outfits is null || index < 0 || index >= outfits.Length ? null : outfits.GetObject(index);
    }

    private void SetAllGuides(bool value)
    {
        foreach (GuideRow row in _guideRows)
        {
            row.Seen = value;
            row.Unlocked = value;
        }
        RaiseDataModified();
    }

    private void SetAllTitles(bool value)
    {
        foreach (TitleRow row in _titleRows)
            row.Unlocked = value;
        RaiseDataModified();
    }

    private Task ShowMessageAsync(string title, string message) =>
        _desktopServices?.ShowErrorAsync(title, message) ?? Task.CompletedTask;

    private static string OperationTitle(string operation) => operation switch
    {
        "copy" => UiStrings.Get("player.copy_slot"),
        "move" => UiStrings.Get("player.move_slot"),
        "swap" => UiStrings.Get("player.swap_slots"),
        "delete" => UiStrings.Get("player.delete_slot"),
        _ => UiStrings.Get("player.save_utils_title"),
    };

    private static SaveFileManager.Platform PlatformFromIndex(int index) => index switch
    {
        0 => SaveFileManager.Platform.Steam,
        1 => SaveFileManager.Platform.GOG,
        2 => SaveFileManager.Platform.XboxGamePass,
        3 => SaveFileManager.Platform.PS4,
        4 => SaveFileManager.Platform.Switch,
        _ => SaveFileManager.Platform.Unknown,
    };

    private static DesktopFileType[] OutfitFileTypes(string extension) =>
    [
        new DesktopFileType(UiStrings.Get("outfits.title"), $"*{extension}"),
        new DesktopFileType("JSON", "*.json"),
        new DesktopFileType("All files", "*.*"),
    ];
}
