using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Services;

namespace NMSE.UI.Avalonia.Inventory;

public enum InventorySortMode
{
    None,
    Name,
    Category,
}

public sealed class AutoStackSlotRequestEventArgs(int x, int y, string itemId) : EventArgs
{
    public int X { get; } = x;
    public int Y { get; } = y;
    public string ItemId { get; } = itemId;
}

public sealed class InventoryTransferEventArgs(JsonObject inventory, string suggestedFileName) : EventArgs
{
    public JsonObject Inventory { get; } = inventory;
    public string SuggestedFileName { get; } = suggestedFileName;
}

public sealed class InventoryImportRequestedEventArgs(string suggestedExtension) : EventArgs
{
    public string SuggestedExtension { get; } = suggestedExtension;
    public JsonObject? ImportedInventory { get; set; }
}

internal sealed class InventoryCellState(int x, int y)
{
    public int X { get; } = x;
    public int Y { get; } = y;
    public int SlotArrayIndex { get; set; } = -1;
    public JsonObject? Slot { get; set; }
    public bool IsActive { get; set; }
    public bool IsSupercharged { get; set; }
    public bool IsPinned { get; set; }
    public bool HasAdjacency { get; set; }
    public Color AdjacencyColor { get; set; } = Colors.Gold;
    public string ItemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public string ItemClass { get; set; } = string.Empty;
    public string ElementSymbol { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Amount { get; set; }
    public int MaxAmount { get; set; }
    public double DamageFactor { get; set; }
    public bool IsChargeable { get; set; }
    public Bitmap? Icon { get; set; }
    public Bitmap? ClassIcon { get; set; }
    public byte[]? OriginalBinaryIdBody { get; set; }
    public string? CorvetteName { get; set; }
    public string? CorvetteIcon { get; set; }
    public bool HasItem => Slot is not null && ItemId is not ("" or "^" or "^YOURSLOTITEM");
}

internal sealed class CorvettePartState(string id)
{
    public string Id { get; } = id;
    public bool Claimed { get; set; }
}

internal static partial class InventoryJson
{
    [GeneratedRegex(@"#\d{5,}$")]
    private static partial Regex VariantRegex();

    [GeneratedRegex(@"^\^[0-9A-Fa-f]{12}$")]
    private static partial Regex TechPackRegex();

    public static string EnsureCaret(string value)
    {
        string trimmed = value.Trim();
        return trimmed.StartsWith('^') ? trimmed : $"^{trimmed}";
    }

    public static (string BaseId, string Variant) SplitVariant(string itemId)
    {
        Match match = VariantRegex().Match(itemId);
        return match.Success ? (itemId[..match.Index], match.Value) : (itemId, string.Empty);
    }

    public static bool IsTechPackHash(string itemId) => TechPackRegex().IsMatch(SplitVariant(itemId).BaseId);

    public static string ExtractItemId(JsonObject slot, out byte[]? binaryBody)
    {
        object? raw = slot.GetObject("Id")?.Get("Id") ?? slot.Get("Id");
        binaryBody = raw is BinaryData binary ? ExtractBinaryBody(binary) : null;
        return raw is BinaryData data ? BinaryToDisplayId(data) : raw as string ?? string.Empty;
    }

    public static void SetItemId(JsonObject slot, string displayId, byte[]? originalBinaryBody)
    {
        object value = BuildIdValue(displayId, originalBinaryBody);
        JsonObject? nested = slot.GetObject("Id");
        if (nested is not null)
            nested.Set("Id", value);
        else
            slot.Set("Id", value);
    }

    public static object BuildIdValue(string displayId, byte[]? binaryBody)
    {
        if (binaryBody is null)
            return displayId;

        int hash = displayId.IndexOf('#', 1);
        string suffix = hash >= 0 ? displayId[hash..] : string.Empty;
        byte[] bytes = new byte[1 + binaryBody.Length + suffix.Length];
        bytes[0] = (byte)'^';
        Array.Copy(binaryBody, 0, bytes, 1, binaryBody.Length);
        Encoding.ASCII.GetBytes(suffix, bytes.AsSpan(1 + binaryBody.Length));
        return new BinaryData(bytes);
    }

    public static string BinaryToDisplayId(BinaryData data)
    {
        byte[] bytes = data.ToByteArray();
        if (bytes.Length == 0 || bytes[0] != (byte)'^')
            return data.ToString();

        var result = new StringBuilder("^");
        bool afterHash = false;
        foreach (byte value in bytes.AsSpan(1))
        {
            if (value == (byte)'#')
            {
                result.Append('#');
                afterHash = true;
            }
            else if (afterHash)
            {
                result.Append((char)value);
            }
            else
            {
                result.Append(value.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return result.ToString();
    }

    public static byte[]? ExtractBinaryBody(BinaryData data)
    {
        byte[] bytes = data.ToByteArray();
        if (bytes.Length == 0 || bytes[0] != (byte)'^')
            return null;
        int hash = Array.IndexOf(bytes, (byte)'#', 1);
        int end = hash >= 0 ? hash : bytes.Length;
        return bytes[1..end];
    }

    public static JsonObject CreateSlot(
        string itemId,
        string inventoryType,
        int amount,
        int maxAmount,
        double damage,
        int x,
        int y,
        byte[]? binaryBody = null)
    {
        var type = new JsonObject();
        type.Add("InventoryType", inventoryType);
        var index = new JsonObject();
        index.Add("X", x);
        index.Add("Y", y);
        var slot = new JsonObject();
        slot.Add("Type", type);
        slot.Add("Id", BuildIdValue(itemId, binaryBody));
        slot.Add("Amount", amount);
        slot.Add("MaxAmount", maxAmount);
        slot.Add("DamageFactor", damage);
        slot.Add("FullyInstalled", true);
        slot.Add("AddedAutomatically", false);
        slot.Add("Index", index);
        return slot;
    }

    public static JsonObject DuplicateSlot(JsonObject source, int x, int y) =>
        InventorySlotHelper.DuplicateSlot(source, x, y);

    public static bool TryParseHtmlColor(string value, out Color color)
    {
        color = Colors.Gold;
        try
        {
            color = Color.Parse(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

internal static class InventoryItemResolver
{
    public static (GameItem? Item, string Name, string? Icon, string? Class) Resolve(
        GameItemDatabase? database,
        string itemId,
        bool preferLowerName)
    {
        (string baseId, string variant) = InventoryJson.SplitVariant(itemId);
        if (database is null)
            return (null, FormatUnknown(baseId, variant), null, null);

        GameItem? item = database.GetItem(itemId) ?? database.GetItem(baseId);
        item ??= database.GetItem(itemId.TrimStart('^')) ?? database.GetItem(baseId.TrimStart('^'));
        string? icon = null;
        string? itemClass = TechPacks.GetClassById(baseId);
        if (item is null && InventoryJson.IsTechPackHash(baseId) && TechPacks.Dictionary.TryGetValue(baseId, out TechPacks.TechPack? pack))
        {
            item = database.GetItem(pack.Id) ?? database.GetItem(pack.Id.TrimStart('^'));
            icon = pack.Icon;
            itemClass = pack.Class;
        }

        if (item is null)
            return (null, FormatUnknown(baseId, variant), icon, itemClass);

        string name = preferLowerName && !string.IsNullOrWhiteSpace(item.NameLower) ? item.NameLower : item.Name;
        if (string.IsNullOrWhiteSpace(name))
            name = baseId;
        return (item, variant.Length == 0 ? name : $"{name} [{variant}]", icon, itemClass);
    }

    public static string ResolveClass(GameItem? item, string? techPackClass) =>
        !string.IsNullOrWhiteSpace(techPackClass) ? techPackClass : item?.QualityToClass() ?? item?.RarityToClass() ?? string.Empty;

    private static string FormatUnknown(string baseId, string variant) =>
        variant.Length == 0 ? baseId : $"{baseId} [{variant}]";
}
