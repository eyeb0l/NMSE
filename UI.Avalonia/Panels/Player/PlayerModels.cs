using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;

namespace NMSE.UI.Avalonia.Panels.Player;

public sealed class PlayerStateChangedEventArgs(string playerState) : EventArgs
{
    public string PlayerState { get; } = playerState;
}

public sealed class SaveUtilityCompletedEventArgs(
    string operation,
    int sourceSlot,
    int? destinationSlot,
    bool requiresReload) : EventArgs
{
    public string Operation { get; } = operation;
    public int SourceSlot { get; } = sourceSlot;
    public int? DestinationSlot { get; } = destinationSlot;
    public bool RequiresReload { get; } = requiresReload;
}

internal abstract class PlayerToggleRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

internal sealed class GuideRow : PlayerToggleRow
{
    private bool _seen;
    private bool _unlocked;

    public required string TopicId { get; init; }
    public required string TopicName { get; init; }
    public required string Category { get; init; }
    public Bitmap? Icon { get; init; }

    public bool Seen
    {
        get => _seen;
        set => SetField(ref _seen, value);
    }

    public bool Unlocked
    {
        get => _unlocked;
        set => SetField(ref _unlocked, value);
    }
}

internal sealed class TitleRow : PlayerToggleRow
{
    private bool _unlocked;

    public required string TitleId { get; init; }
    public required string TitleName { get; init; }
    public required string Description { get; init; }

    public bool Unlocked
    {
        get => _unlocked;
        set => SetField(ref _unlocked, value);
    }
}
