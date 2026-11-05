using GMConverter.SDK.Plugins;
using GMConverter.UnrealEngine.Explorer;
using GMConverter.UnrealEngine.Importers;
using GMConverter.UnrealEngine.Profiles;
using GMConverter.UnrealEngine.Profiles.CrossFireHD;

[assembly: Plugin(typeof(GMConverter.UnrealEngine.UnrealEnginePlugin))]

namespace GMConverter.UnrealEngine;

/// <summary>
/// Plugin entry point for Unreal Engine support. Registers the PSK importer (covers UE4/5
/// asset packs exported as PSK/PSKX with optional scene manifests), the Wwise event and media
/// importers, the UE4, UE2 and UE3 archive explorers, and the Unreal games it can detect.
/// </summary>
public sealed class UnrealEnginePlugin : IPlugin
{
    public string Id => "gmconverter.unrealengine";

    public string DisplayName => "Unreal Engine 4/5";

    public void OnLoad(IPluginContext context)
    {
        context.RegisterImporter<PSKImporter>();
        context.RegisterImporter<WwiseEventImporter>();
        context.RegisterImporter<WemImporter>();
        context.RegisterImporter<UE3SoundCueImporter>();
        context.RegisterImporter<UE3ParticleImporter>();
        context.RegisterExplorer<UE4Explorer>();
        context.RegisterExplorer<UE2Explorer>();
        context.RegisterExplorer<UE3Explorer>();
        foreach (var profile in UnrealProfiles.All)
        {
            context.RegisterGameProfile(profile);
        }

        context.RegisterGameProfile(new CrossFireHDProfile());
    }

    public void OnUnload()
    {
    }
}
