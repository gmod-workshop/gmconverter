using System.Text;
using GMConverter.UnrealEngine.Formats.UE3.Packages;
using GMConverter.UnrealEngine.Formats.UE3.Textures;

namespace GMConverter.UnrealEngine.Formats.UE3.Audio;

/// <summary>
/// A UE3 SoundNodeWave's audio as stored: the PC-compressed Ogg Vorbis stream when present, otherwise
/// the raw WAV. Console streams that follow are ignored.
/// </summary>
internal static class UE3SoundWave
{
    /// <summary>The wave's audio file bytes and their extension (<c>.ogg</c> or <c>.wav</c>), or null when it holds none.</summary>
    public static (byte[] Data, string Extension)? Read(UE3Package package, UE3PackageExport export)
    {
        var (archive, _) = package.OpenObject(export);
        var raw = UE3BulkData.Read(archive);
        var compressed = UE3BulkData.Read(archive);
        foreach (var bulk in new[] { compressed, raw })
        {
            if (bulk.IsExternal || bulk.IsEmpty || bulk.SizeOnDisk <= 0)
            {
                continue;
            }

            var data = package.GetBytes(bulk.InlineOffset, bulk.SizeOnDisk).ToArray();
            var magic = Encoding.ASCII.GetString(data, 0, Math.Min(4, data.Length));
            if (magic is "OggS" or "RIFF")
            {
                return (data, magic == "OggS" ? ".ogg" : ".wav");
            }
        }

        return null;
    }
}
