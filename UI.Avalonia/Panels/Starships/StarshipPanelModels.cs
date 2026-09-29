using NMSE.Data;
using NMSE.Models;
using global::Avalonia.Controls;

namespace NMSE.UI.Avalonia.Panels.Starships;

public sealed record StarshipImportPayload(JsonObject Ship, JsonObject? Base = null, JsonObject? Customisation = null, bool? UsesLegacyColours = null);

internal sealed record ShipPartChoice(string Display, ShipCustomisationItem? Item)
{
    public override string ToString() => Display;
}

internal sealed record ShipValueChoice(string RawValue, string Display)
{
    public override string ToString() => Display;
}

internal sealed record ShipColourChoice(string Name, System.Drawing.Color Colour)
{
    public override string ToString() => Name;
}

internal sealed record ShipPartControl(ComboBox Combo, ShipCustomisationSlot Slot);
internal sealed record ShipTextureControl(ComboBox Combo, string GroupId);
internal sealed record ShipColourControl(ComboBox Combo, string PaletteName, string ColourAlt, string DisplayPaletteId);
