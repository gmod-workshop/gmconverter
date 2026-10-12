using System.Numerics;

namespace GMConverter.SDK.Materials;

/// <summary>
/// Colour that cycles From -> To -> From once per Period seconds, starting Phase seconds in.
/// Colours are sRGB in [0, 1].
/// </summary>
public sealed record MaterialColorPulse(Vector3 From, Vector3 To, float Period, float Phase = 0f);
