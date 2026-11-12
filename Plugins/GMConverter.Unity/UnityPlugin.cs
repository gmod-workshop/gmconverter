using GMConverter.SDK.Plugins;
using GMConverter.Unity.Explorer;
using GMConverter.Unity.Importers;
using GMConverter.Unity.Profiles.LiarsBar;

[assembly: Plugin(typeof(GMConverter.Unity.UnityPlugin))]

namespace GMConverter.Unity;

/// <summary>
/// Plugin entry point for Unity player builds. Registers the explorer that browses a build's
/// serialized files, the importers for the models and sounds it picks, and the Unity games it
/// recognises.
/// </summary>
public sealed class UnityPlugin : IPlugin
{
    public string Id => "gmconverter.unity";

    public string DisplayName => "Unity";

    public void OnLoad(IPluginContext context)
    {
        context.RegisterImporter<UnityImporter>();
        context.RegisterImporter<FsbImporter>();
        context.RegisterExplorer<UnityExplorer>();
        context.RegisterGameProfile(new LiarsBarProfile());
    }

    public void OnUnload()
    {
    }
}
