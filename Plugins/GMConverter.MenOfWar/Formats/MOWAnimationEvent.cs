using System.Numerics;

namespace GMConverter.MenOfWar.Formats;

internal sealed record MOWAnimationEvent(
    int EntityIndex,
    MOWAnimationChunkType Type,
    Vector3? Position,
    Quaternion? Rotation);
