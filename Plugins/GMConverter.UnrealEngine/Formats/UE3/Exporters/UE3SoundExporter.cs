using GMConverter.SDK.Common;
using GMConverter.UnrealEngine.Formats.UE3.Audio;
using GMConverter.UnrealEngine.Formats.UE3.Packages;

namespace GMConverter.UnrealEngine.Formats.UE3.Exporters;

/// <summary>Writes a UE3 SoundCue's audio files and a <see cref="UE3SoundCueSidecar"/> describing how it plays.</summary>
internal static class UE3SoundExporter
{
    public static string Export(UE3GameContent content, UE3Package package, UE3PackageExport export, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var cue = UE3SoundCue.Read(content, package, export);
        Dictionary<string, string> written = [];
        List<UE3SoundCueSidecar.Layer> layers = [];
        foreach (var layer in cue.Layers)
        {
            List<UE3SoundCueSidecar.LayerFile> files = [];
            foreach (var (wave, weight) in layer.Waves)
            {
                if (!written.TryGetValue(wave.Key, out var file))
                {
                    if (UE3SoundWave.Read(wave.Package, wave.Export) is not { } audio)
                    {
                        continue;
                    }

                    file = UniqueFile(written.Values, NameHelpers.SanitizeFileName(wave.Export.ObjectName), audio.Extension);
                    File.WriteAllBytes(Path.Combine(outputDirectory, file), audio.Data);
                    written[wave.Key] = file;
                }

                files.Add(new UE3SoundCueSidecar.LayerFile(file, weight));
            }

            if (files.Count > 0)
            {
                layers.Add(new UE3SoundCueSidecar.Layer(files, layer.Volume.Min, layer.Volume.Max, layer.Pitch.Min, layer.Pitch.Max, layer.AttenuationMeters, layer.Loops));
            }
        }

        if (layers.Count == 0)
        {
            throw new GMConverterException($"UE3 sound cue {export.ObjectName} plays no audio that could be read.");
        }

        var path = Path.Combine(outputDirectory, cue.Name + UE3SoundCueSidecar.Extension);
        new UE3SoundCueSidecar(cue.Name, cue.Category, layers).Write(path);
        return path;
    }

    private static string UniqueFile(IEnumerable<string> taken, string stem, string extension)
    {
        var used = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = stem + extension;
        for (var suffix = 2; used.Contains(name); suffix++)
        {
            name = FormattableString.Invariant($"{stem}_{suffix}{extension}");
        }

        return name;
    }
}
