using Microsoft.UI.Xaml;

namespace Files.WinUI;

public partial class App : Application
{
    private Window? window;
    public App()
    {
        UnhandledException += (_, args) =>
        {
            LogStartupFailure(args.Exception);
            LogStartupMessage(args.Message);
        };
        if (Environment.GetEnvironmentVariable("FILES_TRACE_STARTUP") == "1")
        {
            DebugSettings.IsXamlResourceReferenceTracingEnabled = true;
            DebugSettings.XamlResourceReferenceFailed += (_, args) => LogStartupMessage(args.Message);
        }
        try { InitializeComponent(); }
        catch (Exception ex) { LogStartupFailure(ex); throw; }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            window = new MainWindow();
            window.Activate();
        }
        catch (Exception ex) { LogStartupFailure(ex); throw; }
    }

    private static void LogStartupFailure(Exception exception) => LogStartupMessage(exception.ToString());

    private static void LogStartupMessage(string message)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "Files", "Logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "startup.log"),
                DateTimeOffset.Now.ToString("O") + Environment.NewLine + message + Environment.NewLine);
        }
        catch (Exception) { /* Preserve the original failure even if diagnostic storage is unavailable. */ }
    }
}
