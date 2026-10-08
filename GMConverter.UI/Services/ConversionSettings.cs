using GMConverter.SDK.Importers;
using GMConverter.SDK.Options;

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
    float ScaleFactor,
    ModelAxisMode AxisMode,
    bool BuildMaterials,
    bool GeneratePhysics,
    string? PhysicsMode,
    float PhysicsMass,
    float CoacdThreshold,
    int MaxConvexPieces,
    int MaxHullVertices,
    int MaxTextureSize,
    bool DeduplicateTextures,
    OptionValues ImporterOptions,
    OptionValues ExporterOptions);
