using Avalonia;
using GMConverter.Core.Plugins;
using GMConverter.UI.Services;
using Microsoft.Extensions.Logging;

namespace GMConverter.UI;

internal sealed class Program
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

        // Plugin load happens before Avalonia is initialized, so we cannot route plugin diagnostics
        // through UiLogSink (which posts to Dispatcher.UIThread). A simple file-backed logger
        // captures plugin discovery + load failures into %TEMP%\GMConverter.Plugins.log so the
        // user has a place to look when a plugin fails to register importers/exporters/explorers
        // at startup. Piping plugin events into the in-app console once Avalonia is ready is a
        // follow-up.
        using var pluginLoggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new PluginLogFileProvider());
        });
        PluginHost.Initialize(PluginHost.DefaultDirectory, pluginLoggerFactory);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        GC.KeepAlive(typeof(Avalonia.Svg.Skia.Svg).Assembly);

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
