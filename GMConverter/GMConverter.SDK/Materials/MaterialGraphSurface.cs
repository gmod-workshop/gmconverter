namespace GMConverter.SDK.Materials;

/// <summary>
/// A lit <see cref="MaterialGraph"/>'s surface: the nodes giving each of its shading inputs. Colours
/// are linear; scalars are read from their node's first component.
/// </summary>
/// <param name="Shading">How the surface turns light into colour.</param>
/// <param name="BaseColor">Its colour (3): the diffuse colour of non-metals and the reflected colour of metals.</param>
/// <param name="Metallic">How metallic it is (1), 0 to 1.</param>
/// <param name="Specular">How strongly non-metals reflect (1), 0 to 1, where 0.5 is a typical 4% (see <see cref="MaterialShading"/>).</param>
/// <param name="Roughness">How rough it is (1), 0 (mirror) to 1.</param>
/// <param name="Normal">Its normal in tangent space (3), unnormalized; null for the geometric normal.</param>
/// <param name="Reflection">
/// The light it reflects from its surroundings (3), before the shading model weighs it; null for
/// <see cref="MaterialGraphOp.SceneReflection"/> as it is.
/// </param>
public sealed record MaterialGraphSurface(
    MaterialShading Shading,
    int BaseColor,
    int Metallic,
    int Specular,
    int Roughness,
    int? Normal,
    int? Reflection);
