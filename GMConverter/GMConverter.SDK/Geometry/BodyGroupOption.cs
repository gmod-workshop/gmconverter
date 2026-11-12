namespace GMConverter.SDK.Geometry;

/// <summary>One way a <see cref="BodyGroup"/> can look.</summary>
/// <param name="Name">The option's name, such as the variant it shows.</param>
/// <param name="MeshIndices">The indices into <see cref="Model.Meshes"/> it shows; empty to show nothing.</param>
public sealed record BodyGroupOption(string Name, IReadOnlyList<int> MeshIndices);
