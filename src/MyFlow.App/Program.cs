using Avalonia;
using MyFlow.Core;

namespace MyFlow.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Log.Init();

        // Command-line test mode: run the pipeline on a WAV file without the UI.
        //   MyFlow --selftest speech.wav [--edit "text to edit"] [--paste]
        if (args.Length > 0 && args[0] == "--selftest")
        {
            return SelfTest.RunAsync(args[1..]).GetAwaiter().GetResult();
        }

        Log.Info($"MyFlow {typeof(Program).Assembly.GetName().Version} starting");
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("RenderTimer") && !args.Contains("--retried"))
        {
            // Seen once when launched while the display was waking up: macOS refused a display
            // link. Wait a moment and start a fresh copy of the app instead of crashing.
            Log.Warn("Display wasn't ready (" + ex.Message + "); relaunching in 5 seconds.");
            Thread.Sleep(5000);
            System.Diagnostics.Process.Start(Environment.ProcessPath!, [.. args, "--retried"]);
            return 0;
        }
    }

    // Also used by the Avalonia designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new MacOSPlatformOptions { ShowInDock = false }) // menu-bar app: no Dock icon
            .LogToTrace();
}
