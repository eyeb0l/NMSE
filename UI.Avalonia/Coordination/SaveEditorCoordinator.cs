using NMSE.Models;
using NMSE.UI.Avalonia.Contracts;

namespace NMSE.UI.Avalonia.Coordination;

/// <summary>
/// Owns the deferred panel lifecycle used by the desktop shell. It preserves
/// the current editor ordering: capture a raw baseline before any panel load,
/// load only the active panel, commit only loaded panels, and purge heavy panels
/// after their state has been committed.
/// </summary>
public sealed class SaveEditorCoordinator
{
    private readonly Dictionary<int, PanelRegistration> _registrations = [];
    private readonly HashSet<int> _loaded = [];
    private JsonObject? _saveData;
    private int _activeIndex = -1;

    public event EventHandler? DataModified;

    public IReadOnlyCollection<int> LoadedPanelIndices => _loaded;
    public JsonObject? SaveData => _saveData;

    public void Register(int tabIndex, ISaveEditorPanel panel, bool purgeWhenInactive = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tabIndex);
        ArgumentNullException.ThrowIfNull(panel);
        if (!_registrations.TryAdd(tabIndex, new PanelRegistration(panel, purgeWhenInactive)))
            throw new InvalidOperationException($"A panel is already registered for tab {tabIndex}.");
        panel.DataModified += OnPanelDataModified;
    }

    public void BeginSession(JsonObject saveData, int activeIndex, Action<JsonObject> captureRawBaseline)
    {
        ArgumentNullException.ThrowIfNull(saveData);
        ArgumentNullException.ThrowIfNull(captureRawBaseline);

        _saveData = saveData;
        _loaded.Clear();
        _activeIndex = -1;

        // This must happen before LoadData: several panels intentionally mutate
        // derived values while hydrating their controls.
        captureRawBaseline(saveData);
        Activate(activeIndex);
    }

    public void Activate(int tabIndex)
    {
        if (_saveData is null)
            return;

        if (_activeIndex >= 0 && _activeIndex != tabIndex
            && _registrations.TryGetValue(_activeIndex, out PanelRegistration? previous)
            && previous is not null
            && previous.PurgeWhenInactive
            && _loaded.Remove(_activeIndex))
        {
            previous.Panel.SaveData(_saveData);
            if (previous.Panel is IPurgeableSaveEditorPanel purgeable)
                purgeable.PurgeData();
        }

        _activeIndex = tabIndex;
        if (!_loaded.Add(tabIndex)
            || !_registrations.TryGetValue(tabIndex, out PanelRegistration? current)
            || current is null)
            return;

        current.Panel.LoadData(_saveData);
    }

    public void CommitLoadedPanels()
    {
        if (_saveData is null)
            return;

        foreach (int tabIndex in _loaded.Order())
        {
            if (_registrations.TryGetValue(tabIndex, out PanelRegistration? registration)
                && registration is not null)
                registration.Panel.SaveData(_saveData);
        }
    }

    public void RefreshLoaded(params int[] tabIndices)
    {
        if (_saveData is null)
            return;

        foreach (int tabIndex in tabIndices.Distinct())
        {
            if (_loaded.Contains(tabIndex)
                && _registrations.TryGetValue(tabIndex, out PanelRegistration? registration)
                && registration is not null)
                registration.Panel.LoadData(_saveData);
        }
    }

    public void ApplyUiLocalisation()
    {
        foreach (PanelRegistration registration in _registrations.Values)
            registration.Panel.ApplyUiLocalisation();
    }

    private void OnPanelDataModified(object? sender, EventArgs e) => DataModified?.Invoke(sender, e);

    private sealed record PanelRegistration(ISaveEditorPanel Panel, bool PurgeWhenInactive);
}

public interface IPurgeableSaveEditorPanel
{
    void PurgeData();
}
