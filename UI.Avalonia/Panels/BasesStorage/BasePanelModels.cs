using NMSE.Data;
using NMSE.Models;

namespace NMSE.UI.Avalonia.Panels.BasesStorage;

internal sealed class BaseListItem(string displayName, JsonObject data, int dataIndex, bool isFreighterBase = false)
{
    public string DisplayName { get; set; } = displayName;
    public JsonObject Data { get; } = data;
    public int DataIndex { get; set; } = dataIndex;
    public bool IsFreighterBase { get; } = isFreighterBase;
    public override string ToString() => IsFreighterBase
        ? $"[{DataIndex}] [F] {DisplayName}"
        : $"[{DataIndex}] {DisplayName}";
}

internal sealed class BaseObjectItem(string objectId, JsonObject data, int index)
{
    public string ObjectId { get; set; } = objectId;
    public JsonObject Data { get; } = data;
    public int Index { get; } = index;
    public override string ToString() => $"[{Index}] {ObjectId}";
}

internal sealed class NpcWorkerItem(JsonObject data, int index)
{
    public JsonObject Data { get; } = data;
    public int Index { get; } = index;
    public override string ToString() => Index switch
    {
        0 => UiStrings.Get("base.worker_armorer"),
        1 => UiStrings.Get("base.worker_farmer"),
        2 => UiStrings.Get("base.worker_overseer"),
        3 => UiStrings.Get("base.worker_technician"),
        4 => UiStrings.Get("base.worker_scientist"),
        _ => UiStrings.Format("base.worker_n", Index)
    };
}

internal sealed record NpcRaceItem(string InternalName, string Filename, string DisplayName)
{
    public override string ToString() => DisplayName;
}

internal static class BaseNpcRaces
{
    private static readonly (string Filename, string Name, string Key)[] Values =
    [
        ("MODELS/COMMON/PLAYER/PLAYERCHARACTER/NPCVYKEEN.SCENE.MBIN", "Vy'keen", "common.race_vykeen"),
        ("MODELS/COMMON/PLAYER/PLAYERCHARACTER/NPCKORVAX.SCENE.MBIN", "Korvax", "common.race_korvax"),
        ("MODELS/COMMON/PLAYER/PLAYERCHARACTER/NPCGEK.SCENE.MBIN", "Gek", "common.race_gek"),
        ("MODELS/COMMON/PLAYER/PLAYERCHARACTER/NPCFOURTH.SCENE.MBIN", "Fourth Race", "common.race_fourth"),
        ("MODELS/PLANETS/NPCS/WARRIOR/WARRIOR.SCENE.MBIN", "Vy'keen (Old)", "common.race_vykeen_old"),
        ("MODELS/PLANETS/NPCS/EXPLORER/EXPLORERIPAD.SCENE.MBIN", "Korvax (Old)", "common.race_korvax_old"),
        ("MODELS/PLANETS/NPCS/LOWERORDER/LOWERORDER.SCENE.MBIN", "Gek (Old)", "common.race_gek_old"),
        ("MODELS/PLANETS/NPCS/FOURTHRACE/FOURTHRACE.SCENE.MBIN", "Fourth Race (Old)", "common.race_fourth_old")
    ];

    internal static NpcRaceItem[] Items() => Values.Select(value =>
        new NpcRaceItem(value.Name, value.Filename, UiStrings.Get(value.Key))).ToArray();

    internal static string Lookup(string? filename) => Values.FirstOrDefault(value =>
        value.Filename.Equals(filename, StringComparison.OrdinalIgnoreCase)).Name ?? string.Empty;
}
