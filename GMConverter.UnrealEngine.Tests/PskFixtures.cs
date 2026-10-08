using System.Numerics;
using System.Text;

namespace GMConverter.UnrealEngine.Tests;

/// <summary>
/// Writes minimal ActorX PSK/PSA files and TGA textures for importer tests. Shared with the
/// host's Unreal-to-Source pipeline tests through a linked compile item.
/// </summary>
public static class PskFixtures
{
    /// <summary>Creates a fresh temporary directory for one test's fixture files.</summary>
    public static string CreateDirectory()
    {
        var directory = Path.Join(Path.GetTempPath(), "GMConverter.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    // Four closed tetrahedra 100 cm apart; all but `invertedShell` use the winding the bacta
    // dispenser's correct parts use (negative signed volume in raw ActorX space).
    public static void WriteTetrahedraFixture(string path, int invertedShell)
    {
        Vector3[] corners = [new(0, 0, 0), new(10, 0, 0), new(0, 10, 0), new(0, 0, 10)];
        int[][] negativeVolumeFaces = [[0, 1, 2], [0, 3, 1], [0, 2, 3], [1, 3, 2]];
        using var writer = new BinaryWriter(File.Create(path));
        WriteSection(writer, "ACTRHEAD", 0, 0, _ => { });
        WriteSection(writer, "PNTS0000", 12, 16, w =>
        {
            for (var shell = 0; shell < 4; shell++)
            {
                foreach (var corner in corners)
                {
                    WriteVector(w, corner.X + (shell * 100f), corner.Y, corner.Z);
                }
            }
        });
        WriteSection(writer, "VTXW0000", 16, 16, w =>
        {
            for (var point = 0; point < 16; point++)
            {
                w.Write(point);
                w.Write(0f);
                w.Write(0f);
                w.Write(0);
            }
        });
        WriteSection(writer, "FACE0000", 12, 16, w =>
        {
            for (var shell = 0; shell < 4; shell++)
            {
                foreach (var order in negativeVolumeFaces.Select(int[] (face) => shell == invertedShell ? [.. face.Reverse()] : face))
                {
                    foreach (var corner in order)
                    {
                        w.Write((ushort)((shell * 4) + corner));
                    }

                    w.Write((ushort)0);
                    w.Write(1);
                }
            }
        });
        WriteSection(writer, "MATT0000", 88, 1, w =>
        {
            WriteFixedString(w, "test", 64);
            w.Write(new byte[24]);
        });
        WriteSection(writer, "REFSKELT", 120, 1, WriteBone);
        WriteSection(writer, "RAWWEIGHTS", 12, 16, w =>
        {
            for (var point = 0; point < 16; point++)
            {
                w.Write(1f);
                w.Write(point);
                w.Write(0);
            }
        });
    }

    // UModel writes 32-bit TGAs whose image descriptor declares zero alpha bits even when the
    // fourth channel carries real opacity; decoders that trust the header discard it.
    public static void WriteUnlabeledAlphaTga(string path, byte alpha, int size = 2, byte blue = 240, byte green = 220, byte red = 80)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write([0, 0, 2]);
        writer.Write(new byte[9]);
        writer.Write((ushort)size);
        writer.Write((ushort)size);
        writer.Write((byte)32);
        writer.Write((byte)0x20);
        for (var index = 0; index < size * size; index++)
        {
            writer.Write([blue, green, red, alpha]);
        }
    }

    public static (string Psk, string Psa) WriteFixture(Quaternion? rootRotation = null)
    {
        void WriteRootBone(BinaryWriter writer) => WriteBone(writer, rootRotation ?? Quaternion.Identity);

        var directory = CreateDirectory();
        var psk = Path.Join(directory, "triangle.psk");
        var psa = Path.Join(directory, "triangle.psa");
        using (var writer = new BinaryWriter(File.Create(psk)))
        {
            WriteSection(writer, "ACTRHEAD", 0, 0, _ => { });
            WriteSection(writer, "PNTS0000", 12, 3, w =>
            {
                WriteVector(w, 10, 20, 30);
                WriteVector(w, 110, 20, 30);
                WriteVector(w, 10, 120, 30);
            });
            WriteSection(writer, "VTXW0000", 16, 3, w =>
            {
                for (var index = 0; index < 3; index++)
                {
                    w.Write(index);
                    w.Write(index == 1 ? 1f : 0f);
                    w.Write(index == 2 ? 1f : 0f);
                    w.Write(0);
                }
            });
            WriteSection(writer, "FACE0000", 12, 1, w =>
            {
                w.Write((ushort)0);
                w.Write((ushort)1);
                w.Write((ushort)2);
                w.Write((ushort)0);
                w.Write(1);
            });
            WriteSection(writer, "MATT0000", 88, 1, w =>
            {
                WriteFixedString(w, "test", 64);
                w.Write(new byte[24]);
            });
            WriteSection(writer, "REFSKELT", 120, 1, WriteRootBone);
            WriteSection(writer, "RAWWEIGHTS", 12, 3, w =>
            {
                for (var index = 0; index < 3; index++)
                {
                    w.Write(1f);
                    w.Write(index);
                    w.Write(0);
                }
            });
        }
        using (var writer = new BinaryWriter(File.Create(psa)))
        {
            WriteSection(writer, "ANIMHEAD", 0, 0, _ => { });
            WriteSection(writer, "BONENAMES", 120, 1, WriteRootBone);
            WriteSection(writer, "ANIMINFO", 168, 1, w =>
            {
                WriteFixedString(w, "move", 64);
                WriteFixedString(w, "test", 64);
                w.Write(1);
                w.Write(0);
                w.Write(0);
                w.Write(0);
                w.Write(0f);
                w.Write(1f);
                w.Write(30f);
                w.Write(0);
                w.Write(0);
                w.Write(1);
            });
            WriteSection(writer, "ANIMKEYS", 32, 1, w =>
            {
                WriteVector(w, 50, 0, 0);
                WriteVector(w, 0, 0, 0);
                w.Write(1f);
                w.Write(1f / 30f);
            });
            WriteSection(writer, "SCALEKEYS", 16, 1, w =>
            {
                WriteVector(w, 2, 3, 4);
                w.Write(1f / 30f);
            });
        }
        return (psk, psa);
    }

    private static void WriteBone(BinaryWriter writer)
    {
        WriteBone(writer, Quaternion.Identity);
    }

    private static void WriteBone(BinaryWriter writer, Quaternion rotation)
    {
        WriteFixedString(writer, "root", 64);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        WriteVector(writer, rotation.X, rotation.Y, rotation.Z);
        writer.Write(rotation.W);
        WriteVector(writer, 25, 0, 0);
        writer.Write(1f);
        WriteVector(writer, 1, 1, 1);
    }

    private static void WriteVector(BinaryWriter writer, float x, float y, float z)
    {
        writer.Write(x);
        writer.Write(y);
        writer.Write(z);
    }

    private static void WriteFixedString(BinaryWriter writer, string value, int size)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        writer.Write(bytes);
        writer.Write(new byte[size - bytes.Length]);
    }

    private static void WriteSection(BinaryWriter writer, string name, int size, int count, Action<BinaryWriter> write)
    {
        WriteFixedString(writer, name, 20);
        writer.Write(0);
        writer.Write(size);
        writer.Write(count);
        write(writer);
    }
}
