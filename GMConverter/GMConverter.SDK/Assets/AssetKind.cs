namespace GMConverter.SDK.Assets;

/// <summary>
/// The kinds of asset GMConverter converts. Every importer and exporter handles exactly one kind,
/// and an asset can only be written by an exporter of its own kind.
/// </summary>
public enum AssetKind
{
    /// <summary>A <see cref="Geometry.Model"/>: meshes, materials, and optionally a skeleton, animations and physics.</summary>
    Model,
}
