namespace GMConverter.MenOfWar.Formats;

internal sealed record MOWAnimationFrame(
    ushort Time,
    IReadOnlyList<MOWAnimationEvent> Events);
