namespace GMConverter.UnrealEngine.Formats.UE3.Materials;

/// <summary>A compiled material input: its per-texel evaluator and the textures it samples.</summary>
internal sealed record UE3MaterialOutput(UE3MaterialGraph.Evaluator Evaluate, bool Varies, IReadOnlyList<UE3MaterialTexture> Textures);
