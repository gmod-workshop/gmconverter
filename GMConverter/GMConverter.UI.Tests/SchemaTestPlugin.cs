using GMConverter.SDK.Plugins;

[assembly: Plugin(typeof(GMConverter.UI.Tests.SchemaTestPlugin))]

namespace GMConverter.UI.Tests;

public sealed class SchemaTestPlugin : IPlugin
{
    public string Id => "test.schema";
    public string DisplayName => "Schema test";

    public void OnLoad(IPluginContext context)
    {
        context.RegisterImporter(new SchemaTestImporter());
        context.RegisterExporter(new SchemaTestExporter());
        context.RegisterGameProfile(new SchemaTestGameProfile());
        context.RegisterGameProfile(new SchemaTestTargetProfile("test.target", "Test target"));
        context.RegisterGameProfile(new SchemaTestTargetProfile("test.target2", "Second test target"));
    }

    public void OnUnload()
    {
    }
}
