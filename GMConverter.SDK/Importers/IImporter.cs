using GMConverter.SDK.Geometry;

namespace GMConverter.SDK.Importers;

public interface IImporter
{
    string InputFormat { get; }

    string InputName { get; }

    object Summarize(string inputPath);

    Model Parse(string inputPath, ModelParseOptions options);
}
