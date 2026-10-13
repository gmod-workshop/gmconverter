using GMConverter.SourceEngine.Materials;
using GMConverter.SourceEngine.Physics;

namespace GMConverter.SourceEngine.Exporters;

internal sealed record MDLExportOptions(
    string ModelPath,
    string? StudioMdlPath,
    string? VtfCmdPath,
    bool BuildMaterials,
    bool Lods,
    PhysicsOptions? Physics,
    MaterialOptimizationOptions? MaterialOptimization = null);
