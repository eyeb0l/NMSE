using NMSE.Models;

namespace NMSE.UI.Avalonia.Contracts;

public interface ISaveEditorPanel
{
    event EventHandler? DataModified;

    void LoadData(JsonObject saveData);
    void SaveData(JsonObject saveData);
    void ApplyUiLocalisation();
}
