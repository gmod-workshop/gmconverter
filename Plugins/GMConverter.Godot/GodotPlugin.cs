using GMConverter.Godot.Explorer;
using GMConverter.Godot.Importers;
using GMConverter.SDK.Plugins;

[assembly: Plugin(typeof(GMConverter.Godot.GodotPlugin))]

namespace GMConverter.Godot;

/// <summary>
/// Plugin entry point for games made with Godot 4. Registers the explorer that browses a game's
/// resource pack and the importers for the models and sounds it picks.
/// </summary>
public sealed class GodotPlugin : IPlugin
{
    public string Id => "gmconverter.godot";

    public string DisplayName => "Godot";

    public void OnLoad(IPluginContext context)
    {
        context.RegisterImporter<GodotImporter>();
        context.RegisterImporter<GodotSoundImporter>();
        context.RegisterExplorer<GodotExplorer>();
    }

    public void OnUnload()
    {
    }
}
