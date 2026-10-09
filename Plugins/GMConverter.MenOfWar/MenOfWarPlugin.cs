using GMConverter.MenOfWar.Explorer;
using GMConverter.MenOfWar.Importers;
using GMConverter.SDK.Plugins;

[assembly: Plugin(typeof(GMConverter.MenOfWar.MenOfWarPlugin))]

namespace GMConverter.MenOfWar;

/// <summary>
/// Plugin entry point for Men of War support. Registers the MOW model importer (definition and
/// model files with PLY meshes, materials, and animations) and the MOW archive explorer.
/// </summary>
public sealed class MenOfWarPlugin : IPlugin
{
    public string Id => "gmconverter.menofwar";

    public string DisplayName => "Men of War";

    public void OnLoad(IPluginContext context)
    {
        context.RegisterImporter<MOWImporter>();
        context.RegisterExplorer<MOWExplorer>();
    }

    public void OnUnload()
    {
    }
}
