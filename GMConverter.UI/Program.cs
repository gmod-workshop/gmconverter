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
        // This free open source license is valid only for the open source project at the following URL:
        // https://github.com/gmod-workshop/gmconverter
        // Assembly name: 'GMConverter.UI'
        // The license is valid for all Ab4d.SharpEngine versions that are published before 2027-05-12.
        Ab4d.SharpEngine.Licensing.SetLicense(licenseOwner: "David Katz",
            licenseType: "OpenSourceLicense",
            license: "A543-105E-0047-F209-CC6E-32CD-D155-4F0F-11AD-2706-9E68-7A5B-4945-D56B-9F96-C0CB-A45F-9339-DB9E-F4CE-C84A-DFAD-82B5-B095-1B");

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
