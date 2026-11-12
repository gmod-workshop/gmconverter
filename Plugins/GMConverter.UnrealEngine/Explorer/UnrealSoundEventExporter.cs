using System.Globalization;
using CUE4Parse.UE4.Assets.Exports.Wwise;
using CUE4Parse.UE4.Wwise;
using GMConverter.SDK.Common;
using GMConverter.SDK.Explorer;
using GMConverter.UnrealEngine.Archives;
using GMConverter.UnrealEngine.Audio;
using GMConverter.UnrealEngine.Formats.Wwise;

namespace GMConverter.UnrealEngine.Explorer;

/// <summary>
/// Exports a Wwise event: the <c>.wem</c> media it plays and a <see cref="WwiseEventSidecar"/>
/// saying how. CUE4Parse finds the media; the event's soundbanks give how it is mixed, flattened
/// by <see cref="WwiseEventFlattener"/>. Decoding is left to the importer, so resolving needs no
/// external tool and the export stays the game's own data.
/// </summary>
internal static class UnrealSoundEventExporter
{
    private const float _unitsPerMeter = 100f;

    public static ExplorerResolvedEntry Export(string exportRoot, UAkAudioEvent audioEvent, Cue4ParseProviderContext providerContext)
    {
        List<WwiseExtractedSound> sounds;
        using (PerfTimer.Measure("ue4.resolve", "ExtractAudioEventSounds", audioEvent.Name))
        {
            sounds = providerContext.Wwise.ExtractAudioEventSounds(audioEvent);
        }

        // Localised events carry a recording per language; keep one, plus any shared media.
        var language = UnrealSoundNames.PreferredLanguage(sounds.Select(sound => UnrealSoundNames.Language(Path.GetFileName(sound.OutputPath))));
        sounds = [.. sounds.Where(sound => UnrealSoundNames.Language(Path.GetFileName(sound.OutputPath)) is not { } soundLanguage ||
            soundLanguage.Equals(language, StringComparison.OrdinalIgnoreCase))];

        var eventData = ChooseEventData(audioEvent, language);
        var mix = eventData is { } data ? TryReadMix(providerContext, data) : null;
        List<(WwiseExtractedSound Sound, float Weight, float GainDb)> chosen = [];
        if (mix is not null && eventData is { } cooked)
        {
            var byMediaId = MediaIds(sounds, cooked);
            chosen = [.. mix.Entries
                .Where(entry => byMediaId.ContainsKey(entry.MediaId))
                .Select(entry => (byMediaId[entry.MediaId], entry.Weight, entry.GainDb))];
        }

        // Without the mix, or when its media cannot be matched to what CUE4Parse found, every
        // media file plays alike.
        if (chosen.Count == 0)
        {
            mix = null;
            chosen = [.. sounds.Select(sound => (sound, 1f, 0f))];
        }

        Directory.CreateDirectory(exportRoot);
        List<WwiseEventSidecarMedia> media = [];
        HashSet<string> fileNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (var (sound, weight, gainDb) in chosen)
        {
            var bytes = sound.GetData();
            if (bytes.Length == 0)
            {
                continue;
            }

            var extension = sound.Extension.ToLowerInvariant();
            var stem = UnrealSoundNames.ClipName(Path.GetFileName(sound.OutputPath));
            var fileName = $"{stem}.{extension}";
            for (var index = 2; !fileNames.Add(fileName); index++)
            {
                fileName = $"{stem}_{index}.{extension}";
            }

            File.WriteAllBytes(Path.Join(exportRoot, fileName), bytes);
            media.Add(new WwiseEventSidecarMedia(fileName, weight, gainDb));
        }

        if (media.Count == 0)
        {
            throw new GMConverterException($"Wwise event {audioEvent.Name} plays no media this game's files contain.");
        }

        var category = UnrealSoundNames.Category(audioEvent.Name, audioEvent.GetPathName());
        var attenuation = mix?.Mix.AttenuationMeters
            ?? (audioEvent.MaxAttenuationRadius > 0f ? audioEvent.MaxAttenuationRadius / _unitsPerMeter : (float?)null)
            ?? UnrealSoundNames.DefaultAttenuationMeters(category);
        var sidecarPath = Path.Join(exportRoot, audioEvent.Name + WwiseEventSidecar.Extension);
        var mixed = mix?.Mix ?? WwiseMix.None;
        new WwiseEventSidecar(audioEvent.Name, category, attenuation, media)
        {
            VolumeDb = mixed.VolumeDb,
            VolumeMinDb = mixed.VolumeMinDb,
            VolumeMaxDb = mixed.VolumeMaxDb,
            PitchCents = mixed.PitchCents,
            PitchMinCents = mixed.PitchMinCents,
            PitchMaxCents = mixed.PitchMaxCents,
            Selection = mix?.Selection ?? SDK.Audio.SoundSelection.Random,
            Loops = mixed.Loops,
        }.Write(sidecarPath);

        var details = mix is null
            ? $"Resolved Wwise event: {audioEvent.Name} with {media.Count} media file(s); its mixing could not be read."
            : $"Resolved Wwise event: {audioEvent.Name} with {media.Count} media file(s) at {mixed.VolumeDb.ToString("0.#", CultureInfo.InvariantCulture)} dB.";
        return new ExplorerResolvedEntry(sidecarPath, exportRoot, details);
    }

    // The cooked event for the chosen language, or the only one an unlocalised event has.
    private static FWwiseEventCookedData? ChooseEventData(UAkAudioEvent audioEvent, string? language)
    {
        var entries = audioEvent.EventCookedData?.EventLanguageMap
            .Where(pair => pair.Value is not null)
            .ToList() ?? [];
        var match = entries.FirstOrDefault(pair => language is not null &&
            pair.Key.LanguageName.Text.Equals(language, StringComparison.OrdinalIgnoreCase));
        return (match.Value ?? entries.FirstOrDefault().Value) is { } eventData ? eventData : null;
    }

    private static WwiseEventMix? TryReadMix(Cue4ParseProviderContext providerContext, FWwiseEventCookedData eventData)
    {
        try
        {
            using var scope = PerfTimer.Measure("ue4.resolve", "ReadWwiseMix", eventData.DebugName.Text);
            var root = WwiseHierarchyReader.Load(providerContext.Provider, eventData)?.ReadEvent(eventData.EventId);
            return root is null ? null : WwiseEventFlattener.Flatten(root, WwiseMix.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            PerfTimer.Log("ue4.wwise", $"mix of {eventData.DebugName} unreadable: {ex.Message}");
            return null;
        }
    }

    // The media id of each sound. CUE4Parse names media it found through the soundbanks after
    // their id ("FS_B1 (492522486)") and media cooked into the event after their debug name and
    // language ("brk_alone (English(US))"), whose id the cooked data gives.
    private static Dictionary<uint, WwiseExtractedSound> MediaIds(IEnumerable<WwiseExtractedSound> sounds, FWwiseEventCookedData eventData)
    {
        var cookedIds = eventData.Media
            .Concat(eventData.AudioNodes.Values.OfType<FWwiseAudioNodeCookedData>().SelectMany(node => node.Media))
            .Concat(eventData.SwitchContainerLeaves.SelectMany(leaf => leaf.Media))
            .GroupBy(media => MediaStem(media), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().MediaId, StringComparer.OrdinalIgnoreCase);

        Dictionary<uint, WwiseExtractedSound> byId = [];
        foreach (var sound in sounds)
        {
            var name = Path.GetFileName(sound.OutputPath);
            var open = name.LastIndexOf(" (", StringComparison.Ordinal);
            var stem = open < 0 ? name : name[..open];
            var trailing = open < 0 ? string.Empty : name[(open + 2)..].TrimEnd(')');
            if (uint.TryParse(trailing, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
                cookedIds.TryGetValue(stem, out id))
            {
                byId.TryAdd(id, sound);
            }
        }

        return byId;
    }

    private static string MediaStem(FWwiseMediaCookedData media)
    {
        var debugName = media.DebugName.IsNone ? string.Empty : media.DebugName.Text;
        if (!string.IsNullOrEmpty(debugName))
        {
            var dot = debugName.LastIndexOf('.');
            return dot < 0 ? debugName : debugName[..dot];
        }

        var path = media.MediaPathName.IsNone ? media.PackagedFile?.PathName.ToString() ?? string.Empty : media.MediaPathName.Text;
        return Path.GetFileNameWithoutExtension(path);
    }
}
