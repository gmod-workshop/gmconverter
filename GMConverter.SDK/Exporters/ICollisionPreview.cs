using GMConverter.SDK.Geometry;
using GMConverter.SDK.Options;

namespace GMConverter.SDK.Exporters;

/// <summary>
/// Optional companion to <see cref="IExporter"/> for exporters that generate collision. Hosts
/// call it to draw the collision the export would produce over the model preview, without
/// knowing which options turn collision on or how it is built.
/// </summary>
public interface ICollisionPreview
{
    /// <summary>
    /// Returns the collision meshes the export would write for <paramref name="model"/> with
    /// <paramref name="options"/>, in the model's own space and units, or an empty list when the
    /// options generate no collision.
    /// </summary>
    IReadOnlyList<Mesh> CreateCollisionPreview(Model model, OptionValues options);
}
