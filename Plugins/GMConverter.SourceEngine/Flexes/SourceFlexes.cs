using System.Globalization;
using System.Numerics;
using System.Text;
using GMConverter.SDK.Geometry;

namespace GMConverter.SourceEngine.Flexes;

/// <summary>
/// Writes morph targets as Source flexes: a VTA file holding the rest shape as frame 0 and each
/// morph at full weight as a frame of its own, and a <c>$model</c> block giving every flex a
/// controller of the same name, ranged 0 to 1. A morph that opens the mouth is named
/// <c>jaw_drop</c>, the flex Half-Life 2's faces open their mouths with and Garry's Mod moves
/// while a player talks on voice chat.
/// </summary>
internal static class SourceFlexes
{
    public const string JawDrop = "jaw_drop";

    /// <summary>The model's morph targets as flexes, one per name across its meshes.</summary>
    public static IReadOnlyList<SourceFlex> Plan(Model model)
    {
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, SourceFlex> flexes = new(StringComparer.Ordinal);
        foreach (var morph in model.Meshes.SelectMany(mesh => mesh.MorphTargets ?? []))
        {
            if (!flexes.ContainsKey(morph.Name) && morph.Deltas.Count > 0)
            {
                var name = UniqueName(used, morph.Role == MorphTargetRole.MouthOpen ? JawDrop : Sanitize(morph.Name));
                flexes[morph.Name] = new SourceFlex(morph.Name, name, flexes.Count + 1);
            }
        }

        return [.. flexes.Values];
    }

    /// <summary>
    /// Writes the VTA. Vertices are numbered across the meshes in order; studiomdl matches frame 0's
    /// to the reference mesh by position, and each flex frame lists only the vertices it moves.
    /// </summary>
    public static void WriteVta(
        StreamWriter writer,
        Model model,
        IReadOnlyList<SourceFlex> flexes,
        float scale,
        Action<StreamWriter> writeNodes,
        Action<StreamWriter> writeReferencePose)
    {
        writer.WriteLine("version 1");
        writeNodes(writer);
        writer.WriteLine("skeleton");
        for (var frame = 0; frame <= flexes.Count; frame++)
        {
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"time {frame}"));
            writeReferencePose(writer);
        }

        writer.WriteLine("end");
        writer.WriteLine("vertexanimation");
        writer.WriteLine("time 0");
        var offset = 0;
        foreach (var mesh in model.Meshes)
        {
            for (var index = 0; index < mesh.Vertices.Count; index++)
            {
                WriteVertex(writer, offset + index, mesh.Vertices[index].Position * scale, mesh.Vertices[index].Normal);
            }

            offset += mesh.Vertices.Count;
        }

        foreach (var flex in flexes)
        {
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"time {flex.Frame}"));
            offset = 0;
            foreach (var mesh in model.Meshes)
            {
                foreach (var delta in mesh.MorphTargets?.Where(morph => morph.Name == flex.MorphName).SelectMany(morph => morph.Deltas) ?? [])
                {
                    var vertex = mesh.Vertices[delta.VertexIndex];
                    var normal = vertex.Normal + delta.Normal;
                    WriteVertex(
                        writer,
                        offset + delta.VertexIndex,
                        (vertex.Position + delta.Position) * scale,
                        normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : vertex.Normal);
                }

                offset += mesh.Vertices.Count;
            }
        }

        writer.WriteLine("end");
    }

    /// <summary>Writes the body as a <c>$model</c> carrying the flexes and their controllers.</summary>
    public static void WriteModel(StreamWriter writer, string smdFileName, string vtaFileName, IReadOnlyList<SourceFlex> flexes)
    {
        // studiomdl only reads a block whose brace is on the command's line.
        writer.WriteLine(FormattableString.Invariant($"$model \"body\" \"{smdFileName}\" {{"));
        writer.WriteLine(FormattableString.Invariant($"    flexfile \"{vtaFileName}\" {{"));
        writer.WriteLine("        defaultflex frame 0");
        foreach (var flex in flexes)
        {
            writer.WriteLine(FormattableString.Invariant($"        flex \"{flex.Name}\" frame {flex.Frame}"));
        }

        writer.WriteLine("    }");
        foreach (var flex in flexes)
        {
            writer.WriteLine(FormattableString.Invariant($"    flexcontroller {(flex.Name == JawDrop ? "mouth" : "morph")} range 0 1 \"{flex.Name}\""));
        }

        foreach (var flex in flexes)
        {
            writer.WriteLine(FormattableString.Invariant($"    %{flex.Name} = {flex.Name}"));
        }

        writer.WriteLine("}");
    }

    private static void WriteVertex(StreamWriter writer, int index, Vector3 position, Vector3 normal)
    {
        writer.WriteLine(FormattableString.Invariant(
            $"{index} {position.X:0.######} {position.Y:0.######} {position.Z:0.######} {normal.X:0.######} {normal.Y:0.######} {normal.Z:0.######}"));
    }

    // QC flex expressions read names as identifiers.
    private static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
        }

        var sanitized = builder.ToString().Trim('_');
        return sanitized.Length == 0 || char.IsAsciiDigit(sanitized[0]) ? "flex_" + sanitized : sanitized;
    }

    private static string UniqueName(HashSet<string> used, string name)
    {
        var candidate = name;
        for (var suffix = 2; !used.Add(candidate); suffix++)
        {
            candidate = string.Create(CultureInfo.InvariantCulture, $"{name}_{suffix}");
        }

        return candidate;
    }
}
