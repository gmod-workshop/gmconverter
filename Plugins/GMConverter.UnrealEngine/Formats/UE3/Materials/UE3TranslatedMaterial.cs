using GMConverter.SDK.Materials;

namespace GMConverter.UnrealEngine.Formats.UE3.Materials;

/// <summary>
/// A material translated by <see cref="UE3MaterialTranslator"/>: a <see cref="MaterialGraph"/>'s nodes
/// and outputs, with the UE3 textures its <see cref="MaterialGraphOp.TextureSample"/> nodes number.
/// </summary>
internal sealed record UE3TranslatedMaterial(
    IReadOnlyList<MaterialGraphNode> Nodes,
    IReadOnlyList<UE3ObjectReference> Textures,
    int? Emissive,
    int? Opacity,
    MaterialGraphSurface? Surface);
