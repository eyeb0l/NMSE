using NMSE.IO;
using NMSE.Models;

namespace NMSE.UI.Avalonia.Coordination;

public enum SaveSessionKind
{
    File,
    XboxContainer,
    PlayStationMemoryDat,
}

/// <summary>
/// Describes the concrete platform writer required for one loaded save. This
/// prevents Save As and toolbar state from accidentally changing how an Xbox or
/// PlayStation container is written.
/// </summary>
public sealed record SaveSession(
    SaveSessionKind Kind,
    SaveFileManager.Platform Platform,
    string SourcePath,
    string? ContainerIdentifier = null,
    int ContainerSlotIndex = 0,
    string? AccountPath = null,
    string? NomanSkyTemplatePath = null)
{
    public static SaveSession ForFile(string filePath)
    {
        string fullPath = Path.GetFullPath(filePath);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The save file has no parent directory.", nameof(filePath));
        return new SaveSession(
            SaveSessionKind.File,
            SaveFileManager.DetectPlatform(directory),
            fullPath,
            AccountPath: FindAccountPath(directory),
            NomanSkyTemplatePath: SaveFileManager.IsNomanSkyFile(fullPath) ? fullPath : null);
    }

    public static SaveSession ForXbox(string containersIndexPath, string identifier) => new(
        SaveSessionKind.XboxContainer,
        SaveFileManager.Platform.XboxGamePass,
        Path.GetFullPath(containersIndexPath),
        identifier);

    public static SaveSession ForPlayStationMemoryDat(string memoryDatPath, int slotIndex) => new(
        SaveSessionKind.PlayStationMemoryDat,
        SaveFileManager.Platform.PS4,
        Path.GetFullPath(memoryDatPath),
        ContainerSlotIndex: slotIndex);

    public JsonObject Load() => Kind switch
    {
        SaveSessionKind.File => SaveFileManager.LoadSaveFile(SourcePath),
        SaveSessionKind.XboxContainer => SaveFileManager.LoadXboxSave(
            SourcePath,
            ContainerIdentifier ?? throw new InvalidOperationException("The Xbox slot identifier is missing."))
            ?? throw new InvalidDataException("The Xbox save slot is empty or unreadable."),
        SaveSessionKind.PlayStationMemoryDat => SaveFileManager.LoadPS4MemoryDatSave(SourcePath, ContainerSlotIndex)
            ?? throw new InvalidDataException("The PlayStation memory.dat slot is empty or unreadable."),
        _ => throw new ArgumentOutOfRangeException(),
    };

    public void Save(JsonObject saveData, JsonObject? accountData = null)
    {
        ArgumentNullException.ThrowIfNull(saveData);
        switch (Kind)
        {
            case SaveSessionKind.XboxContainer:
                SaveFileManager.SaveXboxSave(
                    SourcePath,
                    ContainerIdentifier ?? throw new InvalidOperationException("The Xbox slot identifier is missing."),
                    saveData);
                if (accountData is not null)
                    SaveFileManager.SaveXboxAccountData(SourcePath, accountData);
                return;

            case SaveSessionKind.PlayStationMemoryDat:
                SaveFileManager.SavePS4MemoryDatSave(SourcePath, ContainerSlotIndex, saveData);
                if (accountData is not null)
                    SaveFileManager.SavePS4MemoryDatSave(SourcePath, 0, accountData);
                return;

            case SaveSessionKind.File:
                SaveFile(saveData);
                SaveAccountData(accountData);
                return;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public SaveSession SaveAs(string destinationPath)
    {
        string fullPath = Path.GetFullPath(destinationPath);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The save file has no parent directory.", nameof(destinationPath));
        return this with
        {
            Kind = SaveSessionKind.File,
            SourcePath = fullPath,
            ContainerIdentifier = null,
            ContainerSlotIndex = 0,
            AccountPath = FindAccountPath(directory),
        };
    }

    private void SaveFile(JsonObject saveData)
    {
        if (Platform == SaveFileManager.Platform.PS4 && NomanSkyTemplatePath is not null)
        {
            if (!string.Equals(SourcePath, NomanSkyTemplatePath, StringComparison.Ordinal))
                File.Copy(NomanSkyTemplatePath, SourcePath, overwrite: true);
            SaveFileManager.SaveNomanSkyFile(SourcePath, saveData);
            return;
        }

        bool isPlayStationPlainJson = Platform == SaveFileManager.Platform.PS4;
        SaveFileManager.SaveToFile(
            SourcePath,
            saveData,
            compress: !isPlayStationPlainJson,
            writeMeta: true,
            platform: Platform,
            slotIndex: InferSlotIndex(SourcePath));
    }

    private void SaveAccountData(JsonObject? accountData)
    {
        if (accountData is null || string.IsNullOrWhiteSpace(AccountPath))
            return;

        accountData.NameMapper ??= JsonParser.GetDefaultMapper();
        bool writeMeta = Platform == SaveFileManager.Platform.PS4;
        SaveFileManager.SaveToFile(
            AccountPath,
            accountData,
            compress: false,
            writeMeta: writeMeta,
            platform: Platform,
            slotIndex: 0);
    }

    private static string? FindAccountPath(string directory)
    {
        string accountData = Path.Combine(directory, "accountdata.hg");
        if (File.Exists(accountData))
            return accountData;
        string playStationAccount = Path.Combine(directory, "savedata00.hg");
        return File.Exists(playStationAccount) ? playStationAccount : null;
    }

    private static int InferSlotIndex(string path)
    {
        string fileName = Path.GetFileNameWithoutExtension(path);
        if (fileName.Equals("save", StringComparison.OrdinalIgnoreCase))
            return 0;
        string digits = new(fileName.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out int fileNumber)
            ? Math.Max(0, (fileNumber - 1) / 2)
            : 0;
    }
}
