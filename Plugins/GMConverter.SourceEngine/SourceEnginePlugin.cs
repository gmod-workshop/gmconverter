using GMConverter.SDK.Plugins;
using GMConverter.SourceEngine.Exporters;
using GMConverter.SourceEngine.Importers;
using GMConverter.SourceEngine.Profiles;

[assembly: Plugin(typeof(GMConverter.SourceEngine.SourceEnginePlugin))]

namespace GMConverter.SourceEngine;

/// <summary>
/// Plugin entry point for Source Engine support. Registers the MDL importer (reads Garry's Mod /
/// Source MDL files via MdlCrowbar), the MDL exporter (writes the full SMD / QC / VTF / VMT
/// compile workspace via studiomdl + VTFCmd), and the WAV importer and exporter for sounds. Both are activated via the host's DI container so
/// their <c>ITextureFactory</c> dependency is wired automatically.
/// </summary>
public sealed class SourceEnginePlugin : IPlugin
{
    public string Id => "gmconverter.sourceengine";

    public string DisplayName => "Source Engine";

    public void OnLoad(IPluginContext context)
    {
        context.RegisterImporter<MDLImporter>();
        context.RegisterExporter<MDLExporter>();
        context.RegisterImporter<WAVImporter>();
        context.RegisterExporter<WAVExporter>();
        // The first target registered for each exporter is its default.
        foreach (var profile in SourceProfiles.All)
        {
            context.RegisterGameProfile(profile);
        }

        foreach (var profile in SourceSoundProfiles.All)
        {
            context.RegisterGameProfile(profile);
        }
    }

    public void OnUnload()
    {
    }
}
