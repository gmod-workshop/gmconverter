using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;
using GMConverter.SDK.Animation;
using GMConverter.UnrealEngine.Formats.PSA;

namespace GMConverter.UnrealEngine.Archives;

/// <summary>
/// Reads the notifies on an animation sequence that exporters can use. Notify classes are
/// mostly game-specific, so they are recognised by shape rather than name: a class named like a
/// footstep is a footstep (with its socket), one named like firing is a shot, and any other notify
/// that points at a Wwise event or a sound asset plays that sound. Notify states (spans such as
/// motion warping) are skipped.
/// </summary>
internal static class UnrealNotifyReader
{
    private static readonly string[] _soundClasses = ["AkAudioEvent", "SoundWave", "SoundCue", "MetaSoundSource", "SoundBase"];
    private static readonly string[] _boneProperties = ["SocketName", "AttachName", "BoneName"];

    public static IReadOnlyList<PSAClipEvent> Read(UAnimSequenceBase sequence)
    {
        if (!sequence.TryGetValue(out FStructFallback[] notifies, "Notifies"))
        {
            return [];
        }

        List<PSAClipEvent> events = [];
        foreach (var notify in notifies)
        {
            if (notify.GetOrDefault<FPackageIndex>("NotifyStateClass") is { IsNull: false } ||
                notify.GetOrDefault<FPackageIndex>("Notify")?.Load<UObject>() is not { } instance)
            {
                continue;
            }

            var time = notify.GetOrDefault("LinkValue", 0f);
            var bone = ReadBone(instance);
            if (instance.ExportType.Contains("Footstep", StringComparison.OrdinalIgnoreCase))
            {
                events.Add(new PSAClipEvent(time, AnimationEventKind.Footstep, Bone: bone));
            }
            else if (instance.ExportType.Contains("FireWeapon", StringComparison.OrdinalIgnoreCase))
            {
                events.Add(new PSAClipEvent(time, AnimationEventKind.FireWeapon, Bone: bone));
            }
            else if (ReadSound(instance) is { } sound)
            {
                events.Add(new PSAClipEvent(time, AnimationEventKind.Sound, sound, bone));
            }
        }

        return [.. events.OrderBy(clipEvent => clipEvent.Time)];
    }

    // The first property that references a sound asset, skipping editor-preview-only ones.
    private static string? ReadSound(UObject instance)
    {
        foreach (var property in instance.Properties)
        {
            if (property.Name.Text.Contains("Preview", StringComparison.OrdinalIgnoreCase) ||
                property.Tag?.GenericValue is not FPackageIndex { IsNull: false } reference ||
                reference.ResolvedObject is not { } target)
            {
                continue;
            }

            var className = target.Class?.Name.Text ?? string.Empty;
            if (_soundClasses.Any(soundClass => className.Equals(soundClass, StringComparison.OrdinalIgnoreCase)))
            {
                return target.Name.Text;
            }
        }

        return null;
    }

    private static string? ReadBone(UObject instance)
    {
        foreach (var name in _boneProperties)
        {
            if (instance.GetOrDefault(name, default(FName)) is { IsNone: false } bone)
            {
                return bone.Text;
            }
        }

        return null;
    }
}
