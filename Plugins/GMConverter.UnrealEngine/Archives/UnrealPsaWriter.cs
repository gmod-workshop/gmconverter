using CUE4Parse.UE4.Writers;
using CUE4Parse_Conversion.Writers.ActorX;
using CUE4Parse_Conversion.Writers.ActorX.Structs.Animations;

namespace GMConverter.UnrealEngine.Archives;

/// <summary>Writes a converted animation set as ActorX PSA files.</summary>
internal static class UnrealPsaWriter
{
    /// <summary>
    /// Writes each sequence to <paramref name="directory"/>: the first as <c>name.psa</c>, the rest
    /// (montage and composite segments) as <c>name_SEQn.psa</c>, as CUE4Parse names them. Returns the
    /// first file, or null when the set has no sequences.
    /// </summary>
    public static string? Write(CAnimSet animSet, string directory, string name)
    {
        Directory.CreateDirectory(directory);
        string? first = null;
        for (var index = 0; index < animSet.Sequences.Count; index++)
        {
            var path = Path.Join(directory, index == 0 ? $"{name}.psa" : $"{name}_SEQ{index}.psa");
            using var writer = new FArchiveWriter();
            new ActorXAnim(animSet, index).Save(writer);
            File.WriteAllBytes(path, writer.GetBuffer());
            first ??= path;
        }

        return first;
    }
}
