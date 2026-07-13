using Avalonia.Controls;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Contracts;
using NMSE.UI.Avalonia.Services;

namespace NMSE.UI.Avalonia.Panels.Fleet;

/// <summary>Groups the Freighter, Frigates, and Squadron editors under one save panel.</summary>
public sealed class FleetPanel : UserControl, ISaveEditorPanel, IGoToJsonSource
{
    private readonly TabItem _freighterTab;
    private readonly TabItem _frigateTab;
    private readonly TabItem _squadronTab;

    public FreighterPanel Freighter { get; }
    public FrigatePanel Frigates { get; }
    public SquadronPanel Squadron { get; }

    public event EventHandler? DataModified;
    public event EventHandler<GoToJsonRequestEventArgs>? GoToJsonRequested;

    public Func<FleetTransferRequest, CancellationToken, Task<FleetTransferContent?>>? ImportAsync
    {
        get => Freighter.ImportAsync;
        set { Freighter.ImportAsync = value; Frigates.ImportAsync = value; Squadron.ImportAsync = value; }
    }

    public Func<FleetTransferRequest, string, CancellationToken, Task<bool>>? ExportAsync
    {
        get => Freighter.ExportAsync;
        set { Freighter.ExportAsync = value; Frigates.ExportAsync = value; Squadron.ExportAsync = value; }
    }

    public Func<string, string, CancellationToken, Task<bool>>? ConfirmAsync
    {
        get => Freighter.ConfirmAsync;
        set { Freighter.ConfirmAsync = value; Frigates.ConfirmAsync = value; Squadron.ConfirmAsync = value; }
    }

    public FleetPanel(GameItemDatabase database, AvaloniaIconProvider? icons)
    {
        Freighter = new FreighterPanel(database, icons);
        Frigates = new FrigatePanel();
        Squadron = new SquadronPanel();
        _freighterTab = new TabItem { Header = "Freighter", Content = Freighter };
        _frigateTab = new TabItem { Header = "Frigates", Content = Frigates };
        _squadronTab = new TabItem { Header = "Squadron", Content = Squadron };
        Content = new TabControl { ItemsSource = new[] { _freighterTab, _frigateTab, _squadronTab } };

        Freighter.DataModified += ForwardModified;
        Frigates.DataModified += ForwardModified;
        Squadron.DataModified += ForwardModified;
        Freighter.GoToJsonRequested += ForwardGoTo;
        Frigates.GoToJsonRequested += ForwardGoTo;
        Squadron.GoToJsonRequested += ForwardGoTo;
        ApplyUiLocalisation();
    }

    public void LoadData(JsonObject saveData)
    {
        Freighter.LoadData(saveData);
        Frigates.LoadData(saveData);
        Squadron.LoadData(saveData);
    }

    public void SaveData(JsonObject saveData)
    {
        Freighter.SaveData(saveData);
        Frigates.SaveData(saveData);
        Squadron.SaveData(saveData);
    }

    public void ApplyUiLocalisation()
    {
        _freighterTab.Header = UiStrings.Get("fleet.tab_freighter");
        _frigateTab.Header = UiStrings.Get("fleet.tab_frigates");
        _squadronTab.Header = UiStrings.Get("fleet.tab_squadron");
        Freighter.ApplyUiLocalisation();
        Frigates.ApplyUiLocalisation();
        Squadron.ApplyUiLocalisation();
    }

    private void ForwardModified(object? sender, EventArgs e) => DataModified?.Invoke(this, EventArgs.Empty);
    private void ForwardGoTo(object? sender, GoToJsonRequestEventArgs e) => GoToJsonRequested?.Invoke(this, e);
}
