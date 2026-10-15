using GMConverter.Godot.Packs;
using GMConverter.Godot.Resources;
using GMConverter.Godot.Scenes;

namespace GMConverter.Godot.Animation;

/// <summary>Finds the animations the <c>AnimationPlayer</c> nodes of an instantiated scene can play.</summary>
internal static class GodotAnimationPlayers
{
    public static IReadOnlyList<GodotPlayedAnimation> Find(GodotResourceLoader loader, GodotSceneNode root)
    {
        List<GodotPlayedAnimation> animations = [];
        foreach (var player in root.Descendants().Where(node => node.Type == "AnimationPlayer"))
        {
            // Track paths start at the player's root node, its parent unless it says otherwise.
            var rootPath = player["root_node"] is GodotNodePath path ? path.Names : [".."];
            if (player.Find(rootPath) is not { } animationRoot || player["libraries"] is not Dictionary<object, object?> libraries)
            {
                continue;
            }

            foreach (var (libraryName, libraryReference) in libraries)
            {
                if (loader.Get(libraryReference)?["_data"] is not Dictionary<object, object?> data)
                {
                    continue;
                }

                foreach (var (animationName, animationReference) in data)
                {
                    if (loader.Get(animationReference) is { Type: "Animation" } animation)
                    {
                        // Animations of a named library are played as "library/animation".
                        var name = libraryName is string { Length: > 0 } prefix ? $"{prefix}/{animationName}" : $"{animationName}";
                        animations.Add(new GodotPlayedAnimation(player, animationRoot, GodotAnimation.Read(animation, name)));
                    }
                }
            }
        }

        return animations;
    }
}
