using Avalonia;
using NMSE.Core;

namespace NMSE;

internal static class AvaloniaProgram
{
    [STAThread]
    public static void Main(string[] args)
    {
        ApplicationPaths.Current.PrepareUserData();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception) LogCrash(exception);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash(e.Exception);
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            LogCrash(exception);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();

    internal static void LogCrash(Exception exception)
    {
        try
        {
            string path = ApplicationPaths.Current.CrashLogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"""
                === NMSE Avalonia Crash Report ===
                Time (UTC): {DateTime.UtcNow:O}
                Exception: {exception.GetType().FullName}
                Message: {exception.Message}
                Stack Trace:
                {exception}
                ==================================

                """);
        }
        catch
        {
            // Crash logging must never mask the original failure.
        }
    }
}
