using GMConverter.SDK.Plugins;
using GMConverter.SourceEngine.Exporters;
using GMConverter.SourceEngine.Importers;
using GMConverter.SourceEngine.Profiles;

[assembly: Plugin(typeof(GMConverter.SourceEngine.SourceEnginePlugin))]

namespace GMConverter.SourceEngine;

/// <summary>
/// Plugin entry point for Source Engine support. Registers the MDL importer (reads Garry's Mod /
/// Source MDL files via MdlCrowbar) and the MDL exporter (writes the full SMD / QC / VTF / VMT
/// compile workspace via studiomdl + VTFCmd). Both are activated via the host's DI container so
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
        // The first target registered for the MDL exporter is its default.
        foreach (var profile in SourceProfiles.All)
        {
            context.RegisterGameProfile(profile);
        }
    }

    public void OnUnload()
    {
    }
}
