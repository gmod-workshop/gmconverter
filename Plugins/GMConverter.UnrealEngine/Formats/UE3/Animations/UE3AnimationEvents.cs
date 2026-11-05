using GMConverter.SDK.Animation;
using GMConverter.SDK.Common;
using GMConverter.UnrealEngine.Formats.PSA;
using GMConverter.UnrealEngine.Formats.UE3.Audio;
using GMConverter.UnrealEngine.Formats.UE3.Packages;

namespace GMConverter.UnrealEngine.Formats.UE3.Animations;

/// <summary>
/// Turns a sequence's notifies into animation events. Sound notifies (any notify class with a
/// <c>SoundCue</c>) play each layer of their cue, offset by the layer's delay, named as the cue's
/// exported sound events are; variants that only play underwater are left out.
/// </summary>
internal sealed class UE3AnimationEvents(UE3GameContent content)
{
    private readonly Dictionary<string, UE3SoundCue?> _cues = new(StringComparer.OrdinalIgnoreCase);

    public List<PSAClipEvent> Read(UE3Package package, UE3AnimSequence sequence)
    {
        List<PSAClipEvent> events = [];
        foreach (var (time, notifyIndex) in sequence.Notifies ?? [])
        {
            if (package.GetExport(notifyIndex) is not { } notify)
            {
                continue;
            }

            UE3Properties properties;
            try
            {
                properties = package.OpenObject(notify).Properties;
            }
            catch (GMConverterException)
            {
                continue;
            }

            if (properties.GetBool("bIsInWaterOnly") == true || Cue(package, properties.GetObject("SoundCue")) is not { } cue)
            {
                continue;
            }

            events.AddRange(cue.Layers.Select((layer, index) =>
                new PSAClipEvent(time + layer.DelaySeconds, AnimationEventKind.Sound, UE3SoundCue.LayerName(cue.Name, index))));
        }

        return events;
    }

    private UE3SoundCue? Cue(UE3Package package, int cueIndex)
    {
        if (cueIndex == 0 || content.Resolve(package, cueIndex) is not { } found)
        {
            return null;
        }

        var key = $"{found.Package.FilePath}|{found.Export.SerialOffset}";
        if (!_cues.TryGetValue(key, out var cue))
        {
            try
            {
                cue = UE3SoundCue.Read(content, found.Package, found.Export);
            }
            catch (GMConverterException)
            {
                cue = null;
            }

            _cues[key] = cue;
        }

        return cue;
    }
}
