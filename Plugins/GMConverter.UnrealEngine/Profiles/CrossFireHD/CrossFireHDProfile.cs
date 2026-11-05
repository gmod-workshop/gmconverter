using GMConverter.SDK.Animation;
using GMConverter.SDK.Explorer;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Profiles;

namespace GMConverter.UnrealEngine.Profiles.CrossFireHD;

/// <summary>
/// CrossFire HD (Unreal Engine 3), read by the UE3 explorer. It recognises the install and gives
/// the game's first-person clips their roles, which its weapons name the same way throughout
/// (<c>puton</c> draws, <c>fire1</c> and <c>fire2</c> are the two attacks, <c>Put_Down</c> holsters).
/// </summary>
internal sealed class CrossFireHDProfile : IImportGameProfile
{
    private const string _executablePattern = "CFHD-*.exe";
    private const int _maxAncestorDepth = 4;

    private static readonly Dictionary<string, AnimationAction> _clipActions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Idle"] = AnimationAction.Idle,
        ["puton"] = AnimationAction.Draw,
        ["Put_Down"] = AnimationAction.Holster,
        ["fire"] = AnimationAction.Attack,
        ["fire1"] = AnimationAction.Attack,
        ["fire2"] = AnimationAction.SecondaryAttack,
        ["reload"] = AnimationAction.Reload,
        ["Inspect"] = AnimationAction.Inspect,
        ["Run"] = AnimationAction.Run,
    };

    public string Id => "crossfirehd";

    public string DisplayName => "CrossFire HD";

    public string Engine => "Unreal Engine 3";

    /// <summary>Exact when the game's <c>Binaries\Win64\CFHD-*.exe</c> sits in the folder or one above it.</summary>
    public GameMatch Detect(ExplorerTarget target)
    {
        var directory = target.IsDirectory ? new DirectoryInfo(target.FullPath) : new FileInfo(target.FullPath).Directory;
        for (var depth = 0; directory is not null && depth <= _maxAncestorDepth; depth++, directory = directory.Parent)
        {
            var binaries = Path.Join(directory.FullName, "Binaries", "Win64");
            if (Directory.Exists(binaries) && Directory.EnumerateFiles(binaries, _executablePattern).Any())
            {
                return GameMatch.Exact;
            }
        }

        return GameMatch.None;
    }

    public Model Prepare(Model model)
    {
        if (model.Animations is not { Count: > 0 } clips)
        {
            return model;
        }

        return model with
        {
            Animations = [.. clips.Select(clip => clip.Role is null && _clipActions.TryGetValue(clip.Name, out var action)
                ? clip with { Role = new AnimationRole(action) }
                : clip)],
        };
    }
}
