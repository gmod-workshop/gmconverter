using Avalonia;
using System;

namespace GMConverter.UI;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        Ab4d.SharpEngine.Licensing.SetLicense(
            licenseOwner: "David Katz",
            licenseType: "TrialLicense",
            license: "7C75-1AE3-E6E9-1175-20B7-4DAE-A684-8E20-55A5-4A97-C0BD-2A71-FDFD-E052-B391-3D14-6DC2");

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
