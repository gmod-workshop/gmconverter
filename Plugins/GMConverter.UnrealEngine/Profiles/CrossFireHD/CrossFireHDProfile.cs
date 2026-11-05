using GMConverter.SDK.Animation;
using GMConverter.SDK.Explorer;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Profiles;

namespace GMConverter.UnrealEngine.Profiles.CrossFireHD;

/// <summary>
/// CrossFire HD (Unreal Engine 3), read by the UE3 explorer. It recognises the install and gives
/// the game's first-person clips their roles, which its weapons name the same way throughout
/// (<c>puton</c> draws, <c>fire1</c> and <c>fire2</c> are the two attacks, <c>Put_Down</c> holsters),
/// and labels its first-person arm bones (<c>b_LeftClav</c>, <c>b_RightIndex2</c>, …) so their
/// animations can move onto other arms. The weapon hangs from <c>b_RightWeapon</c>.
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

    private static readonly Dictionary<string, HumanoidBone> _boneRoles = CreateBoneRoles();

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
        if (model.Skeleton is { } skeleton && skeleton.Bones.Any(bone => bone.Humanoid is null && _boneRoles.ContainsKey(bone.Name)))
        {
            model = model with
            {
                Skeleton = new Skeleton([.. skeleton.Bones.Select(bone => bone.Humanoid is null && _boneRoles.TryGetValue(bone.Name, out var role) ? bone with { Humanoid = role } : bone)]),
            };
        }

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

    private static Dictionary<string, HumanoidBone> CreateBoneRoles()
    {
        Dictionary<string, HumanoidBone> roles = new(StringComparer.OrdinalIgnoreCase)
        {
            ["b_Spine2"] = HumanoidBone.Chest,
            ["b_LeftWeapon"] = HumanoidBone.LeftHeldItem,
            ["b_RightWeapon"] = HumanoidBone.RightHeldItem,
        };
        foreach (var side in new[] { "Left", "Right" })
        {
            roles[$"b_{side}Clav"] = Enum.Parse<HumanoidBone>(side + "Clavicle");
            roles[$"b_{side}Arm"] = Enum.Parse<HumanoidBone>(side + "UpperArm");
            roles[$"b_{side}ForeArm"] = Enum.Parse<HumanoidBone>(side + "Forearm");
            roles[$"b_{side}Hand"] = Enum.Parse<HumanoidBone>(side + "Hand");
            foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" })
            {
                for (var joint = 1; joint <= 3; joint++)
                {
                    roles[$"b_{side}{finger}{joint}"] = Enum.Parse<HumanoidBone>($"{side}{finger}{joint}");
                }
            }
        }

        return roles;
    }
}
