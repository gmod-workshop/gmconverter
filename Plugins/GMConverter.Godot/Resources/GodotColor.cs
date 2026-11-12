namespace GMConverter.Godot.Resources;

/// <summary>A Godot colour with channels from 0 to 1 (more for HDR colours).</summary>
internal readonly record struct GodotColor(float R, float G, float B, float A);
