namespace NMSE.UI.Avalonia.Contracts;

public interface IGoToJsonSource
{
    event EventHandler<GoToJsonRequestEventArgs>? GoToJsonRequested;
}

public sealed class GoToJsonRequestEventArgs(string jsonPath) : EventArgs
{
    public string JsonPath { get; } = jsonPath;
}
