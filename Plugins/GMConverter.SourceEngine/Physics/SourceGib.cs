using GMConverter.SDK.Geometry;

namespace GMConverter.SourceEngine.Physics;

/// <summary>One piece a model breaks into.</summary>
/// <param name="BoneName">The bone the piece is cut from and placed on when it spawns.</param>
/// <param name="Model">The piece as a one-bone model in that bone's space, with its body's collision.</param>
/// <param name="Volume">The volume of the piece's collision hulls, in cubic metres.</param>
internal sealed record SourceGib(string BoneName, Model Model, float Volume);
