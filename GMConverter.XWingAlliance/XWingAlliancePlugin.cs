using GMConverter.SDK.Plugins;
using GMConverter.XWingAlliance.Importers;

[assembly: Plugin(typeof(GMConverter.XWingAlliance.XWingAlliancePlugin))]

namespace GMConverter.XWingAlliance;

/// <summary>
/// Plugin entry point for X-Wing Alliance support. Registers the OPT importer, which reads
/// ship models through JeremyAnsel.Xwa.Opt.
/// </summary>
public sealed class XWingAlliancePlugin : IPlugin
{
    public string Id => "gmconverter.xwingalliance";

    public string DisplayName => "X-Wing Alliance";

    public void OnLoad(IPluginContext context)
    {
        context.RegisterImporter<OPTImporter>();
    }

    public void OnUnload()
    {
    }
}
