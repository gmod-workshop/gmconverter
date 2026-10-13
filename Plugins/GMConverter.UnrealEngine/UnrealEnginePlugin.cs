using GMConverter.SDK.Plugins;
using GMConverter.UnrealEngine.Explorer;
using GMConverter.UnrealEngine.Formats.Unreal;
using GMConverter.UnrealEngine.Importers;

[assembly: Plugin(typeof(GMConverter.UnrealEngine.UnrealEnginePlugin))]

namespace GMConverter.UnrealEngine;

/// <summary>
/// Plugin entry point for Unreal Engine 4/5 support. Registers the PSK importer (covers UE4/5
/// asset packs exported as PSK/PSKX with optional scene manifests), the UE4 and UE2 archive
/// explorers, and the Unreal games it can detect.
/// </summary>
public sealed class UnrealEnginePlugin : IPlugin
{
    public string Id => "gmconverter.unrealengine";

    public string DisplayName => "Unreal Engine 4/5";

    public void OnLoad(IPluginContext context)
    {
        context.RegisterImporter<PSKImporter>();
        context.RegisterExplorer<UE4Explorer>();
        context.RegisterExplorer<UE2Explorer>();
        foreach (var profile in UnrealGameProfiles.All)
        {
            context.RegisterGameProfile(profile);
        }
    }

    public void OnUnload()
    {
    }
}
