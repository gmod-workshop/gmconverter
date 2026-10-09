using System.Numerics;

namespace GMConverter.UnrealEngine.Formats.PSA;

internal readonly record struct PSAKey(Vector3 Location, Quaternion Rotation, float Time);
