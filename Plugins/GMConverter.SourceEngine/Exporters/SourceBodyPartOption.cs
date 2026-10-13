using GMConverter.SDK.Geometry;

namespace GMConverter.SourceEngine.Exporters;

/// <summary>One option of a <see cref="SourceBodyPart"/>.</summary>
/// <param name="SmdFileName">The reference SMD it is written to, or null for a <c>blank</c> option that shows nothing.</param>
/// <param name="Meshes">The meshes it shows.</param>
internal sealed record SourceBodyPartOption(string? SmdFileName, IReadOnlyList<Mesh> Meshes);
