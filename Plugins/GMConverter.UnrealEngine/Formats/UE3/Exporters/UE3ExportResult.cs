namespace GMConverter.UnrealEngine.Formats.UE3.Exporters;

/// <summary>The files a UE3 export wrote: the mesh (when one was exported) and any animation PSAs.</summary>
internal sealed record UE3ExportResult(string? MeshPath, IReadOnlyList<string> AnimationPaths);
