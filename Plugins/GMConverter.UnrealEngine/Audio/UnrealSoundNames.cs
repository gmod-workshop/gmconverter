using System.Text.RegularExpressions;
using GMConverter.SDK.Audio;
using GMConverter.SDK.Common;

namespace GMConverter.UnrealEngine.Audio;

/// <summary>
/// What sound names reveal. Wwise events carry their mixing in the soundbank hierarchy, which this
/// plugin does not read yet, so the kind of sound comes from the naming conventions sound designers
/// share (<c>FS_</c> for footsteps, <c>VO_</c> or <c>brk_</c> for voice, <c>WPN_</c> for weapons,
/// …), and how far it carries from its kind.
/// </summary>
internal static partial class UnrealSoundNames
{
    // Checked in order, so specific words win over general ones.
    private static readonly (SoundCategory Category, string[] Words)[] _categoryWords =
    [
        (SoundCategory.Footstep, ["fs", "footstep", "footsteps", "step", "steps"]),
        (SoundCategory.Voice, ["vo", "vox", "voice", "dlg", "dialogue", "dialog", "brk", "bark", "barks"]),
        (SoundCategory.Weapon, ["wpn", "weapon", "weapons", "gun", "fire", "shot", "shoot", "blaster", "blasters", "rifle", "pistol", "reload", "mati"]),
        (SoundCategory.Impact, ["imp", "impact", "impacts", "hit", "bull", "bullet", "explosion", "expl", "dst", "destruction", "debris"]),
        (SoundCategory.Music, ["mx", "mus", "music", "stinger"]),
        (SoundCategory.UI, ["ui", "hud", "menu", "button"]),
        (SoundCategory.Ambient, ["amb", "ambience", "ambient", "env", "environment"]),
        (SoundCategory.Movement, ["fly", "foley", "motion", "cloth", "move", "movement", "jump", "land", "servo"]),
    ];

    /// <summary>
    /// A file-friendly clip name from what CUE4Parse calls a media file, such as
    /// <c>FS_B1 (123456)</c> becoming <c>FS_B1_123456</c>.
    /// </summary>
    public static string ClipName(string mediaName)
    {
        var name = NonWordRun().Replace(mediaName, "_").Trim('_');
        return string.IsNullOrEmpty(name) ? "sound" : name;
    }

    /// <summary>
    /// The language of a localised media file from CUE4Parse's name for it, such as
    /// <c>English(US)</c> for <c>brk_alone_b1 (English(US))</c>, or null for media every language
    /// shares, which CUE4Parse names by media id or <c>SFX</c>.
    /// </summary>
    public static string? Language(string mediaName)
    {
        var match = TrailingParentheses().Match(mediaName);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups[1].Value;
        return value.All(char.IsAsciiDigit) || value.Equals("SFX", StringComparison.OrdinalIgnoreCase) ? null : value;
    }

    /// <summary>
    /// The language to keep from <paramref name="languages"/>: English (US) or another English when
    /// there is one, so a voice line plays in one language rather than a random one.
    /// </summary>
    public static string? PreferredLanguage(IEnumerable<string?> languages)
    {
        var available = languages.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return available.FirstOrDefault(language => language.Equals("English(US)", StringComparison.OrdinalIgnoreCase))
            ?? available.FirstOrDefault(language => language.StartsWith("English", StringComparison.OrdinalIgnoreCase))
            ?? available.FirstOrDefault();
    }

    /// <summary>
    /// The kind of sound an event is, from its name and the folders it lives in. The name's first
    /// word is the strongest sign, since teams prefix events by kind (<c>FS_B1</c>,
    /// <c>FLY_B1_AimStart</c>, <c>brk_combat_…</c>); then the folders (<c>/VO/</c>,
    /// <c>/Weapons/</c>); then any other word of the name.
    /// </summary>
    /// <param name="eventName">The event's asset name.</param>
    /// <param name="objectPath">The event's object path, whose folders are also checked.</param>
    public static SoundCategory Category(string eventName, string objectPath)
    {
        var words = NameHelpers.SplitWords(eventName);
        var folders = objectPath.Split('/', StringSplitOptions.RemoveEmptyEntries).SkipLast(1).SelectMany(NameHelpers.SplitWords);
        return Find(words.Take(1)) ?? Find(folders) ?? Find(words) ?? SoundCategory.Other;
    }

    private static SoundCategory? Find(IEnumerable<string> words)
    {
        var candidates = words.ToHashSet(StringComparer.Ordinal);
        foreach (var (category, categoryWords) in _categoryWords)
        {
            if (categoryWords.Any(candidates.Contains))
            {
                return category;
            }
        }

        return null;
    }

    /// <summary>How far a sound of <paramref name="category"/> typically carries in a game, or null for one not placed in the world.</summary>
    public static float? DefaultAttenuationMeters(SoundCategory category) => category switch
    {
        SoundCategory.Weapon => 120f,
        SoundCategory.Impact => 60f,
        SoundCategory.Voice => 40f,
        SoundCategory.Ambient => 40f,
        SoundCategory.Footstep => 25f,
        SoundCategory.Movement => 20f,
        SoundCategory.Music or SoundCategory.UI => null,
        _ => 40f,
    };

    [GeneratedRegex(@"[^A-Za-z0-9]+")]
    private static partial Regex NonWordRun();

    // The last parenthesised part, which may itself hold one pair: "(English(US))".
    [GeneratedRegex(@"\(([^()]*(?:\([^()]*\))?)\)\s*$")]
    private static partial Regex TrailingParentheses();
}
