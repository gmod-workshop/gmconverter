namespace GMConverter.SDK.Geometry;

/// <summary>
/// A simpler version of a model's meshes for when it is small on screen. It shares the model's
/// materials, skeleton and everything else; only the meshes are replaced.
/// </summary>
/// <param name="ScreenSize">
/// How big the model is on screen when this level takes over: its bounding sphere's diameter as a
/// fraction of the screen's width, as in Unreal's LOD screen sizes. Smaller is further away.
/// </param>
/// <param name="Meshes">The meshes that replace the model's own at this level.</param>
public sealed record ModelLod(float ScreenSize, IReadOnlyList<Mesh> Meshes);
