using System.Globalization;
using Avalonia.Controls;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Models;
using NMSE.UI.Avalonia.Dialogs;

namespace NMSE.UI.Avalonia.Panels.Companions;

public sealed partial class CompanionPanel
{
    private readonly TextBlock _predictedBattleStats = new();
    private readonly Button _searchBattleSeed = new();
    private readonly Button _resetBattleRecord = new();

    private static ulong? ParseBattleSeed(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return ulong.TryParse(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text,
            text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? NumberStyles.HexNumber : NumberStyles.Integer,
            CultureInfo.InvariantCulture, out ulong value) ? value : null;
    }

    private void UpdateBattlePrediction()
    {
        if (SelectedCompanion is not { } companion) return;
        ulong? species = ParseBattleSeed(companion.GetString("SpeciesSeed"));
        ulong? genus = ParseBattleSeed(companion.GetString("GenusSeed"));
        if (species is null || genus is null) { _predictedBattleStats.Text = "Health / Speed / Combat: N/A"; return; }
        var classes = companion.GetBool("PetBattlerUseCoreStatClassOverrides")
            ? new PetBattleLogic.Classes(ClassValue(_classes[0].SelectedItem?.ToString()), ClassValue(_classes[1].SelectedItem?.ToString()), ClassValue(_classes[2].SelectedItem?.ToString()))
            : PetBattleLogic.RollClasses(species.Value, genus.Value);
        if (!companion.GetBool("PetBattlerUseCoreStatClassOverrides")) _averageClass.Text = PetBattleLogic.ClassLetter(classes.Overall).ToString();
        var stats = PetBattleLogic.PredictCoreStats(species.Value, genus.Value, (float)companion.GetDouble("Scale"), classes,
            (int)(_treats[0].NumericValue ?? 0), (int)(_treats[1].NumericValue ?? 0), (int)(_treats[2].NumericValue ?? 0));
        _predictedBattleStats.Text = $"Health {stats.Health} · Speed {stats.Speed} · Combat {stats.Combat}";
    }

    private async Task SearchBattleSeedAsync()
    {
        if (SelectedCompanion is not { } companion || TopLevel.GetTopLevel(this) is not Window owner) return;
        var candidate = await SeedSearchWindows.SearchPetAsync(owner, companion.GetDouble("Scale"),
            (int)(_treats[0].NumericValue ?? 0), (int)(_treats[1].NumericValue ?? 0), (int)(_treats[2].NumericValue ?? 0));
        if (candidate is not { } result || !ReferenceEquals(companion, SelectedCompanion)) return;
        companion.Set("SpeciesSeed", SeedHelper.FormatSeed(result.SpeciesSeed));
        companion.Set("GenusSeed", SeedHelper.FormatSeed(result.GenusSeed));
        companion.Set("Scale", (double)result.Scale);
        companion.Set("PetBattlerUseCoreStatClassOverrides", false);
        LoadSelectedCompanion(); Modified();
    }

    private async Task ResetBattleRecordAsync()
    {
        if (SelectedCompanion is not { } companion) return;
        if (!await _desktop.ConfirmAsync("Reset battle record", "Reset this pet's battle record and gene edits?")) return;
        PetBattleLogic.ResetBattleRecord(companion);
        LoadSelectedCompanion(); Modified();
    }
}
