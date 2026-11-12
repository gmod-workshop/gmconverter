namespace GMConverter.UnrealEngine.Audio;

/// <summary>A child of a <see cref="WwiseNode"/> and how likely a random container is to pick it.</summary>
internal sealed record WwiseChild(WwiseNode Node, float Weight = 1f);
