using System.Globalization;
using Avalonia.Controls;
using NMSE.Data;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Models;
using NMSE.UI.Avalonia.Dialogs;

namespace NMSE.UI.Avalonia.Inventory;

public sealed partial class InventoryGrid
{
    private Func<JsonObject?>? _getSaveRoot;
    private Action? _beforeTechnologyTransfer;
    private Action? _afterTechnologyTransfer;

    public void ConfigureTechnologyActions(Func<JsonObject?> getSaveRoot, Action beforeTransfer, Action afterTransfer)
    {
        _getSaveRoot = getSaveRoot;
        _beforeTechnologyTransfer = beforeTransfer;
        _afterTechnologyTransfer = afterTransfer;
    }

    private void ResolveTechnologyDetails(InventoryCellState state, NMSE.Data.GameItem? item)
    {
        bool pack = state.ItemType != "Technology" && TechPackLogic.TryResolveTechId(state.ItemId, _database, out _, out _);
        string id = state.ItemId;
        string? seed;
        if (pack && TechPackLogic.TryResolveTechId(id, _database, out string techId, out seed)) item = _database?.GetItem(techId);
        else (_, seed) = ProceduralSeedHelper.Strip(id);
        if (item is null) return;
        string? techName = ProcTechLogic.GenerateTechName(item, seed, ActiveLocalisation);
        if (pack)
            state.Name = ProcTechLogic.GeneratePackName(techName ?? item.Name, item, ActiveLocalisation) ?? state.Name;
        else if (techName is not null) state.Name = techName;
        if (item.IsProcedural && uint.TryParse(seed, out uint number))
        {
            string stats = string.Join("\n", ProcTechLogic.Roll(item, number)
                .Select(roll => ProcTechLogic.FormatRollLine(roll, item.BaseStat, ActiveLocalisation, null)));
            if (stats.Length > 0) state.Description += "\n\n" + stats;
        }
    }

    public NMSE.Data.LocalisationService? ActiveLocalisation { get; set; }

    private async Task SearchTechnologyAsync(InventorySlotCell cell)
    {
        if (cell.State.Slot is not { } slot || TopLevel.GetTopLevel(this) is not Window owner) return;
        var (id, _) = ProceduralSeedHelper.Strip(cell.State.ItemId);
        var item = _database?.GetItem(id);
        if (item is not { IsProcedural: true } || item.StatLevels.Count == 0) return;
        uint? seed = await SeedSearchWindows.SearchTechnologyAsync(owner, item);
        if (seed is null) return;
        slot.Set("Id", CatalogueLogic.EnsureCaretPrefix(id) + "#" + seed.Value.ToString("D5", CultureInfo.InvariantCulture));
        RebuildCells(); RaiseModified();
    }

    private async Task TransferTechnologyAsync(InventorySlotCell cell, bool unpack)
    {
        if (_slots is null || cell.State.Slot is not { } slot || _getSaveRoot?.Invoke()?.GetObject("PlayerStateData") is not { } player
            || TopLevel.GetTopLevel(this) is not Window owner) return;
        _beforeTechnologyTransfer?.Invoke();
        string id; string? seed;
        if (unpack)
        {
            if (!TechPackLogic.TryResolveTechId(cell.State.ItemId, _database, out id, out seed))
            {
                if (!TechPackLogic.TryResolveModuleTech(_database?.GetItem(cell.State.ItemId), out id)) return;
                seed = null;
            }
        }
        else
        {
            (id, seed) = ProceduralSeedHelper.Strip(cell.State.ItemId);
            if (!TechPackLogic.CanPackageTech(_database?.GetItem(id), id, cell.State.DamageFactor, _slots)) return;
        }
        var destinations = TechPackLogic.EnumerateDestinations(player, tech: unpack)
            .Where(destination => TechPackLogic.GetFreePositions(destination.Inventory, destination.Slots).Count > 0).ToArray();
        if (destinations.Length == 0)
        {
            await new MessageDialogWindow("Technology transfer", "No destination has an empty slot.").ShowDialog<bool>(owner);
            return;
        }
        int? selected = await ChoiceDialogWindow.ShowAsync(owner, "Choose destination inventory",
            destinations.Select(destination => UiStrings.Format(destination.NameKey, destination.NameArgs)).ToArray());
        if (selected is null) return;
        var destination = destinations[selected.Value];
        var positions = TechPackLogic.GetFreePositions(destination.Inventory, destination.Slots);
        int? position = await ChoiceDialogWindow.ShowAsync(owner, "Choose empty slot", positions.Select(point => $"({point.X}, {point.Y})").ToArray());
        if (position is null) return;
        var point = positions[position.Value];
        int index = FindSlotIndex(slot);
        bool changed = unpack
            ? _database?.GetItem(id) is { } item && TechPackLogic.TryUnpack(_slots, index, destination, item, id, seed, point.X, point.Y)
            : TechPackLogic.TryPack(_slots, index, id, seed, destination, point.X, point.Y, _database);
        if (!changed) return;
        RebuildCells(); RaiseModified(); _afterTechnologyTransfer?.Invoke();
    }
}
