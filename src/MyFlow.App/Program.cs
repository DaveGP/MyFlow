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
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    // Also used by the Avalonia designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new MacOSPlatformOptions { ShowInDock = false }) // menu-bar app: no Dock icon
            .LogToTrace();
}
