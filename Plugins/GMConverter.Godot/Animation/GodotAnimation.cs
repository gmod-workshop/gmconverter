using System.Globalization;
using GMConverter.Godot.Resources;

namespace GMConverter.Godot.Animation;

/// <summary>A Godot <c>Animation</c> resource: its length, whether it loops, and the tracks that can move a model.</summary>
internal sealed record GodotAnimation(string Name, float Length, bool Loops, IReadOnlyList<GodotAnimationTrack> Tracks)
{
    public static GodotAnimation Read(GodotResource animation, string name)
    {
        List<GodotAnimationTrack> tracks = [];
        for (var index = 0; animation[string.Create(CultureInfo.InvariantCulture, $"tracks/{index}/type")] is string; index++)
        {
            if (GodotAnimationTrack.Read(animation, index) is { } track)
            {
                tracks.Add(track);
            }
        }

        // Godot leaves the default length (one second) and loop mode (none) unsaved.
        var length = animation["length"] is double saved ? (float)saved : 1f;
        var loops = animation["loop_mode"] is long loopMode && loopMode != 0;
        return new GodotAnimation(name, length, loops, tracks);
    }
}
