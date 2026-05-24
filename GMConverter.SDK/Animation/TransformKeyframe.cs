using GMConverter.SDK.Geometry;

namespace GMConverter.SDK.Animation;

public readonly record struct TransformKeyframe(float TimeSeconds, Transform Transform);
