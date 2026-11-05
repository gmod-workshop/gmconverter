using System.Text.RegularExpressions;

namespace GMConverter.UnrealEngine.Profiles.CrossFireHD;

/// <summary>
/// How CrossFire HD lays out a weapon's first-person view, which its packages don't state:
/// <list type="bullet">
/// <item>arm animations live in <c>AS_FPS_1PC_&lt;Weapon&gt;</c> sets and the weapon's own in <c>AS_FPS_1WP_&lt;Weapon&gt;</c>,
/// with clips of the same name played together;</item>
/// <item>a skin's sets (<c>..._SKIN_&lt;Skin&gt;</c>) only hold the clips it changes, on top of the base weapon's sets;</item>
/// <item>the weapon hangs from the arms' <c>b_RightWeapon</c> bone, and the arms mesh is the one the arm set previews.</item>
/// </list>
/// </summary>
internal static partial class CrossFireHDViewmodels
{
    public const string ArmsSetPrefix = "AS_FPS_1PC_";
    public const string WeaponSetPrefix = "AS_FPS_1WP_";
    public const string WeaponBone = "b_RightWeapon";
    public const string WeaponBonePrefix = "Weapon_";

    /// <summary>True for first-person sets used on dry land (swimming has its own sets and arms).</summary>
    public static bool IsViewmodelSet(string setName, string prefix)
    {
        return setName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            !setName.EndsWith("_Swim", StringComparison.OrdinalIgnoreCase) &&
            !setName.Contains("_Swim_", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The base weapon's set a skin's set builds on, or null when the set isn't a skin's.</summary>
    public static string? GetBaseSetName(string setName)
    {
        var match = SkinSuffix().Match(setName);
        return match.Success ? setName[..match.Index] : null;
    }

    /// <summary>Weapon meshes worth a viewmodel: not the see-through copies some skins use for effects.</summary>
    public static bool IsWeaponMesh(string meshName)
    {
        return !meshName.Contains("Translucen", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("_SKIN_.*$", RegexOptions.IgnoreCase)]
    private static partial Regex SkinSuffix();
}
