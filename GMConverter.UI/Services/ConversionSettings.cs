using GMConverter.Importers;

namespace GMConverter.UI.Services;

internal sealed record ConversionSettings(
    string InputFormat,
    string OutputFormat,
    string InputPath,
    string? OutputPath,
    string? BaseName,
    string? ModelPath,
    string? StudioMdlPath,
    string? VtfCmdPath,
    string? MaterialDirectory,
    string? AnimationPath,
    float ScaleFactor,
    ModelAxisMode AxisMode,
    bool BuildMaterials,
    bool GeneratePhysics,
    string? PhysicsMode,
    float PhysicsMass,
    float CoacdThreshold,
    int MaxConvexPieces,
    int MaxHullVertices);
