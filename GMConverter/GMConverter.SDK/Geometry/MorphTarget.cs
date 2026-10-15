namespace GMConverter.SDK.Geometry;

/// <summary>
/// A shape a mesh can blend toward, such as an open mouth: each delta moves one of the mesh's
/// vertices, in model space, by as much as it moves at full weight. Meshes of one model that share
/// a name are parts of the same shape, driven together.
/// </summary>
/// <param name="Name">The shape's name, shared by every mesh it moves.</param>
/// <param name="Deltas">The vertices the shape moves; the rest stay where they are.</param>
/// <param name="Role">What the shape is for, when the importer knows it, so exporters can give it the target game's name.</param>
public sealed record MorphTarget(string Name, IReadOnlyList<MorphTargetDelta> Deltas, MorphTargetRole Role = MorphTargetRole.None);
