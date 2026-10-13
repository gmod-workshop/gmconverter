using GMConverter.SDK.Animation;
using GMConverter.SDK.Common;

namespace GMConverter.Core.Animation;

/// <summary>
/// Reads and writes animation roles as text, for CLI options and the UI: an action, optionally
/// followed by a weapon stance and a direction, separated by commas, such as <c>run</c>,
/// <c>run,rifle,forward</c> or <c>turn,left</c>. <c>none</c> means no role. Words are the enum
/// names in lowercase with hyphens between words (<c>combat-idle</c>); case and hyphens are
/// ignored when reading.
/// </summary>
internal static class AnimationRoleText
{
    public const string None = "none";

    /// <summary>Every action as role text, in declaration order.</summary>
    public static IReadOnlyList<string> Actions { get; } = [.. Enum.GetValues<AnimationAction>().Select(action => Format(action))];

    public static string Format(AnimationRole? role)
    {
        if (role is null)
        {
            return None;
        }

        List<string> parts = [Format(role.Action)];
        if (role.Stance is { } stance)
        {
            parts.Add(Format(stance));
        }

        if (role.Direction is { } direction)
        {
            parts.Add(Format(direction));
        }

        return string.Join(',', parts);
    }

    /// <summary>Parses role text; <c>none</c> gives null.</summary>
    public static AnimationRole? Parse(string text)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            throw new GMConverterException("An animation role is empty.");
        }

        if (parts.Length == 1 && string.Equals(parts[0], None, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!TryParse<AnimationAction>(parts[0], out var action))
        {
            throw new GMConverterException($"Unknown animation action '{parts[0]}'. Actions: {string.Join(", ", Actions)}, or {None}.");
        }

        WeaponStance? stance = null;
        MoveDirection? direction = null;
        foreach (var part in parts.Skip(1))
        {
            if (stance is null && TryParse<WeaponStance>(part, out var parsedStance))
            {
                stance = parsedStance;
            }
            else if (direction is null && TryParse<MoveDirection>(part, out var parsedDirection))
            {
                direction = parsedDirection;
            }
            else
            {
                throw new GMConverterException(
                    $"Unknown or repeated role detail '{part}'. Stances: {string.Join(", ", Enum.GetValues<WeaponStance>().Select(value => Format(value)))}. " +
                    $"Directions: {string.Join(", ", Enum.GetValues<MoveDirection>().Select(value => Format(value)))}.");
            }
        }

        return new AnimationRole(action, stance, direction);
    }

    /// <summary>
    /// Parses <c>clip=role</c> overrides, such as <c>A_B1_2HRifle_Jog_F=run</c>. The clip name is
    /// everything before the last <c>=</c>.
    /// </summary>
    public static KeyValuePair<string, AnimationRole?> ParseOverride(string text)
    {
        var separator = text.LastIndexOf('=');
        if (separator <= 0 || separator == text.Length - 1)
        {
            throw new GMConverterException($"Expected an animation role override as clip=role, such as Walk_F=walk: {text}");
        }

        return new(text[..separator].Trim(), Parse(text[(separator + 1)..]));
    }

    private static string Format<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var name = value.ToString();
        var builder = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }

    private static bool TryParse<TEnum>(string text, out TEnum value)
        where TEnum : struct, Enum
    {
        return Enum.TryParse(text.Replace("-", string.Empty, StringComparison.Ordinal), ignoreCase: true, out value) &&
            Enum.IsDefined(value);
    }
}
