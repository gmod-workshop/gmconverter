namespace GMConverter.SourceEngine.Particles;

/// <summary>One animation in a sprite sheet: the frames it shows (grid indexes) and how long each lasts.</summary>
/// <param name="Frames">Each frame shown, in order, with its share of the sequence's time.</param>
internal sealed record SpriteSequence(IReadOnlyList<(int Frame, float Duration)> Frames);
