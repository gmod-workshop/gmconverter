namespace GMConverter.UnrealEngine.Formats.UE3.Particles;

/// <summary>How a cooked distribution's lookup table is laid out.</summary>
/// <param name="Random">Each sample holds a min and a max (a uniform distribution) rather than one value.</param>
/// <param name="Chunk">How many floats each sample takes.</param>
/// <param name="StartTime">The time of the first sample.</param>
/// <param name="TimeScale">Samples per unit of time; 0 when the value doesn't change.</param>
internal readonly record struct UE3DistributionTable(bool Random, int Chunk, float StartTime, float TimeScale);
