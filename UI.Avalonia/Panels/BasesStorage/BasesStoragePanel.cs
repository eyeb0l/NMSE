using Avalonia.Controls;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Contracts;
using NMSE.UI.Avalonia.Services;

namespace NMSE.UI.Avalonia.Panels.BasesStorage;

public sealed class BasesStoragePanel : UserControl, ISaveEditorPanel, IGoToJsonSource
{
    private readonly TabItem _basesTab;
    private readonly TabItem _chestsTab;
    private readonly TabItem _storageTab;

    public BasesEditorPanel Bases { get; }
    public ChestsPanel Chests { get; }
    public StoragePanel Storage { get; }

    public event EventHandler? DataModified;
    public event EventHandler<GoToJsonRequestEventArgs>? GoToJsonRequested;

    public Func<BasesStorageTransferRequest, CancellationToken, Task<BasesStorageTransferContent?>>? ImportAsync
    {
        get => Bases.ImportAsync;
        set { Bases.ImportAsync = value; Chests.ImportAsync = value; Storage.ImportAsync = value; }
    }

    public Func<BasesStorageTransferRequest, string, CancellationToken, Task<bool>>? ExportAsync
    {
        get => Bases.ExportAsync;
        set { Bases.ExportAsync = value; Chests.ExportAsync = value; Storage.ExportAsync = value; }
    }

    public Func<string, string, CancellationToken, Task<bool>>? ConfirmAsync
    {
        get => Bases.ConfirmAsync;
        set => Bases.ConfirmAsync = value;
    }

    public BasesStoragePanel(GameItemDatabase database, AvaloniaIconProvider? icons)
    {
        Bases = new BasesEditorPanel(); Chests = new ChestsPanel(database, icons); Storage = new StoragePanel(database, icons);
        _basesTab = new TabItem { Content = Bases }; _chestsTab = new TabItem { Content = Chests }; _storageTab = new TabItem { Content = Storage };
        Content = new TabControl { ItemsSource = new[] { _basesTab, _chestsTab, _storageTab } };
        Bases.DataModified += ForwardModified; Chests.DataModified += ForwardModified; Storage.DataModified += ForwardModified;
        Bases.GoToJsonRequested += ForwardGoTo; Chests.GoToJsonRequested += ForwardGoTo; Storage.GoToJsonRequested += ForwardGoTo;
        ApplyUiLocalisation();
    }

    public void LoadData(JsonObject saveData) { Bases.LoadData(saveData); Chests.LoadData(saveData); Storage.LoadData(saveData); }
    public void SaveData(JsonObject saveData) { Bases.SaveData(saveData); Chests.SaveData(saveData); Storage.SaveData(saveData); }
    public void ApplyUiLocalisation()
    {
        _basesTab.Header = UiStrings.Get("base.tab_bases"); _chestsTab.Header = UiStrings.Get("base.tab_chests"); _storageTab.Header = UiStrings.Get("base.tab_storage");
        Bases.ApplyUiLocalisation(); Chests.ApplyUiLocalisation(); Storage.ApplyUiLocalisation();
    }
    private void ForwardModified(object? sender, EventArgs e) => DataModified?.Invoke(this, EventArgs.Empty);
    private void ForwardGoTo(object? sender, GoToJsonRequestEventArgs e) => GoToJsonRequested?.Invoke(this, e);
}
