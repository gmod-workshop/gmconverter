using System.Text;
using GMConverter.Godot.Packs;
using GMConverter.SDK.Common;

namespace GMConverter.Godot.Tests;

public sealed class GodotPackTests : IDisposable
{
    private readonly string _directory = Path.Join(Path.GetTempPath(), "GMConverter.Godot.Tests", Guid.NewGuid().ToString("N"));

    public GodotPackTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void ReadsStandAlonePack()
    {
        var path = Path.Join(_directory, "game.pck");
        File.WriteAllBytes(path, BuildPack(0, ("res://audio/shot.ogg.import", "[remap]\n"u8.ToArray()), ("res://data.bin", [1, 2, 3])));

        var pack = GodotPack.Open(path);

        Assert.Equal(new Version(4, 1, 1), pack.EngineVersion);
        Assert.Equal(["res://audio/shot.ogg.import", "res://data.bin"], pack.Entries.Select(entry => entry.Path));
        Assert.Equal([1, 2, 3], pack.Read("res://data.bin"));
    }

    [Fact]
    public void FindsPackAppendedToExecutable()
    {
        var stub = new byte[1000];
        Random.Shared.NextBytes(stub);
        var pack = BuildPack(stub.Length, ("res://data.bin", [7, 8]));
        var path = Path.Join(_directory, "game.exe");
        using (var stream = File.Create(path))
        {
            stream.Write(stub);
            stream.Write(pack);
            stream.Write(BitConverter.GetBytes((long)pack.Length));
            stream.Write("GDPC"u8);
        }

        Assert.True(GodotPack.Contains(path));
        Assert.Equal(path, GodotGameFolder.FindPack(_directory));
        Assert.Equal([7, 8], GodotPack.Open(path).Read("res://data.bin"));
    }

    [Fact]
    public void RejectsFilesWithoutPack()
    {
        var path = Path.Join(_directory, "other.exe");
        File.WriteAllBytes(path, new byte[64]);

        Assert.False(GodotPack.Contains(path));
        Assert.Null(GodotGameFolder.FindPack(_directory));
    }

    [Fact]
    public void RejectsEncryptedDirectory()
    {
        var path = Path.Join(_directory, "game.pck");
        var pack = BuildPack(0, ("res://data.bin", [1]));
        pack[20] = 1; // pack flags: encrypted directory
        File.WriteAllBytes(path, pack);

        Assert.Throws<GMConverterException>(() => GodotPack.Open(path));
    }

    // A format 2 pack whose file base is absolute, starting at `start` in its host file.
    internal static byte[] BuildPack(int start, params (string Path, byte[] Data)[] files)
    {
        using var directory = new MemoryStream();
        using (var writer = new BinaryWriter(directory, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(files.Length);
            var offset = 0L;
            foreach (var (path, data) in files)
            {
                var name = Encoding.UTF8.GetBytes(path);
                var padded = (name.Length + 4) & ~3;
                writer.Write(padded);
                writer.Write(name);
                writer.Write(new byte[padded - name.Length]);
                writer.Write(offset);
                writer.Write((long)data.Length);
                writer.Write(new byte[16]);
                writer.Write(0u);
                offset += data.Length;
            }
        }

        const int headerSize = 4 + (4 * 5) + 8 + (16 * 4);
        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("GDPC"u8);
            writer.Write(2u);
            writer.Write(4u);
            writer.Write(1u);
            writer.Write(1u);
            writer.Write(0u);
            writer.Write((long)(start + headerSize + directory.Length));
            writer.Write(new byte[16 * 4]);
            writer.Write(directory.ToArray());
            foreach (var (_, data) in files)
            {
                writer.Write(data);
            }
        }

        return output.ToArray();
    }
}
