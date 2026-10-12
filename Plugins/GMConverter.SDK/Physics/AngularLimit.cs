namespace GMConverter.SDK.Physics;

/// <summary>The range a joint may rotate through about one axis. Equal ends lock the axis.</summary>
public sealed record AngularLimit(float MinDegrees, float MaxDegrees);
