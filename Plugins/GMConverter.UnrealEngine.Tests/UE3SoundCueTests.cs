using System.Text;
using GMConverter.SDK.Audio;
using GMConverter.UnrealEngine.Formats.UE3.Audio;
using GMConverter.UnrealEngine.Formats.UE3.Exporters;
using GMConverter.UnrealEngine.Formats.UE3.Packages;

namespace GMConverter.UnrealEngine.Tests;

public sealed class UE3SoundCueTests
{
    private static readonly string[] _classes = ["SoundCue", "SoundNodeMixer", "SoundNodeAttenuation", "SoundNodeRandom", "SoundNodeDelay", "SoundNodeModulator", "SoundNodeWave"];

    [Fact]
    public void CueGraphsFlattenIntoLayers()
    {
        var (content, package) = ReadPackage();

        var cue = UE3SoundCue.Read(content, package, package.Exports[0]);

        Assert.Equal("SND_Test", cue.Name);
        Assert.Equal(SoundCategory.Weapon, cue.Category);
        Assert.Equal(2, cue.Layers.Count);

        // Mixer input 1: attenuation over a random node weighted 1:3.
        var swing = cue.Layers[0];
        Assert.Equal(["WAV_A", "WAV_B"], swing.Waves.Select(wave => wave.Wave.Export.ObjectName));
        Assert.Equal([0.25f, 0.75f], swing.Waves.Select(wave => wave.Weight));
        Assert.Equal(SoundRange.Fixed(0.5f), swing.Volume);
        Assert.Equal(10f, swing.AttenuationMeters);
        Assert.Equal(0f, swing.DelaySeconds);

        // Mixer input 2 at half volume: a delay over a modulator with UE3's default ±5% ranges.
        var clank = cue.Layers[1];
        Assert.Equal("WAV_C", Assert.Single(clank.Waves).Wave.Export.ObjectName);
        Assert.Equal(0.25f * 0.95f, clank.Volume.Min, 5);
        Assert.Equal(0.25f * 1.05f, clank.Volume.Max, 5);
        Assert.Equal(new SoundRange(0.95f, 1.05f), clank.Pitch);
        Assert.Equal(0.3f, clank.DelaySeconds, 5);
        Assert.Null(clank.AttenuationMeters);
    }

    [Fact]
    public void ExportWritesEachWavesOggOnceAndASidecar()
    {
        var (content, package) = ReadPackage();
        var output = PskFixtures.CreateDirectory();

        var path = UE3SoundExporter.Export(content, package, package.Exports[0], output);

        var sidecar = UE3SoundCueSidecar.Read(path);
        Assert.Equal("SND_Test", sidecar.Name);
        Assert.Equal(["WAV_A.ogg", "WAV_B.ogg"], sidecar.Layers[0].Files.Select(file => file.File));
        Assert.Equal(["WAV_C.ogg"], sidecar.Layers[1].Files.Select(file => file.File));
        Assert.StartsWith("OggS", Encoding.ASCII.GetString(File.ReadAllBytes(Path.Join(output, "WAV_A.ogg"))));
        Assert.Equal("SND_Test_2", UE3SoundCue.LayerName(sidecar.Name, 1));
    }

    private static (UE3GameContent Content, UE3Package Package) ReadPackage()
    {
        var directory = PskFixtures.CreateDirectory();
        var path = Path.Join(directory, "sounds");
        File.WriteAllBytes(path, WritePackage());
        var content = UE3GameContent.Open(directory);
        return (content, content.GetPackage(path));
    }

    // An uncompressed v872 package: SND_Test (class WeaponSounds) plays a mixer of
    // [attenuation -> random(A x1, B x3)] and, at half volume, [delay 0.2-0.4 -> modulator -> C].
    private static byte[] WritePackage()
    {
        List<string> names = ["None", "Core", "Engine", "Package", "Class", "FirstNode", "ObjectProperty", "VolumeMultiplier", "FloatProperty",
            "SoundClass", "NameProperty", "WeaponSounds", "ChildNodes", "ArrayProperty", "InputVolume", "RadiusMax", "Weights", "DelayMin", "DelayMax",
            "SND_Test", "SoundNodeMixer_0", "SoundNodeAttenuation_0", "SoundNodeRandom_0", "SoundNodeDelay_0", "SoundNodeModulator_0", "WAV_A", "WAV_B", "WAV_C", .. _classes];
        int Name(string name) => names.IndexOf(name);

        var body = new MemoryStream();
        var writer = new BinaryWriter(body);
        void WriteName(string name)
        {
            writer.Write(Name(name));
            writer.Write(0);
        }

        void Tag(string name, string type, int size)
        {
            WriteName(name);
            WriteName(type);
            writer.Write(size);
            writer.Write(0);
        }

        void Object(string name, int index)
        {
            Tag(name, "ObjectProperty", 4);
            writer.Write(index);
        }

        void Float(string name, float value)
        {
            Tag(name, "FloatProperty", 4);
            writer.Write(value);
        }

        void Objects(string name, params int[] values)
        {
            Tag(name, "ArrayProperty", 4 + 4 * values.Length);
            writer.Write(values.Length);
            foreach (var value in values)
            {
                writer.Write(value);
            }
        }

        void Floats(string name, params float[] values)
        {
            Tag(name, "ArrayProperty", 4 + 4 * values.Length);
            writer.Write(values.Length);
            foreach (var value in values)
            {
                writer.Write(value);
            }
        }

        const int headerOffset = 4 + 4 + 4 + 4 + 5 + 4 + 4 * 7 + 4 * 4 + 16 + 4 + 4 + 4 + 4 + 4;
        var nameOffset = headerOffset;
        foreach (var name in names)
        {
            writer.Write(name.Length + 1);
            writer.Write(Encoding.ASCII.GetBytes(name));
            writer.Write((byte)0);
            writer.Write(0x0007001000000000UL);
        }

        // Import 1 is Engine; imports 2.. are its classes, so class N is import -(N + 2).
        var importOffset = headerOffset + (int)body.Position;
        WriteName("Core");
        WriteName("Package");
        writer.Write(0);
        WriteName("Engine");
        foreach (var className in _classes)
        {
            WriteName("Core");
            WriteName("Class");
            writer.Write(-1);
            WriteName(className);
        }

        int Class(string name) => -(Array.IndexOf(_classes, name) + 2);
        (string Name, int Class, int Outer)[] exports =
        [
            ("SND_Test", Class("SoundCue"), 0),
            ("SoundNodeMixer_0", Class("SoundNodeMixer"), 1),
            ("SoundNodeAttenuation_0", Class("SoundNodeAttenuation"), 1),
            ("SoundNodeRandom_0", Class("SoundNodeRandom"), 1),
            ("SoundNodeDelay_0", Class("SoundNodeDelay"), 1),
            ("SoundNodeModulator_0", Class("SoundNodeModulator"), 1),
            ("WAV_A", Class("SoundNodeWave"), 0),
            ("WAV_B", Class("SoundNodeWave"), 0),
            ("WAV_C", Class("SoundNodeWave"), 0),
        ];
        const int exportSize = 4 * 3 + 8 + 4 + 8 + 4 + 4 + 4 + 4 + 16 + 4;
        var exportOffset = headerOffset + (int)body.Position;
        var exportTable = body.Position;
        writer.Write(new byte[exportSize * exports.Length]);

        List<(int Offset, int Size)> serials = [];
        void Serial(Action properties)
        {
            var start = headerOffset + (int)body.Position;
            writer.Write(0); // net index
            properties();
            WriteName("None");
            serials.Add((start, headerOffset + (int)body.Position - start));
        }

        Serial(() =>
        {
            Object("FirstNode", 2);
            Float("VolumeMultiplier", 0.5f);
            Tag("SoundClass", "NameProperty", 8);
            WriteName("WeaponSounds");
        });
        Serial(() =>
        {
            Floats("InputVolume", 1f, 0.5f);
            Objects("ChildNodes", 3, 5);
        });
        Serial(() =>
        {
            Float("RadiusMax", 1000f);
            Objects("ChildNodes", 4);
        });
        Serial(() =>
        {
            Floats("Weights", 1f, 3f);
            Objects("ChildNodes", 7, 8);
        });
        Serial(() =>
        {
            Float("DelayMin", 0.2f);
            Float("DelayMax", 0.4f);
            Objects("ChildNodes", 6);
        });
        Serial(() => Objects("ChildNodes", 9));
        for (var wave = 0; wave < 3; wave++)
        {
            Serial(() => { });
            // Raw PCM (empty), then the PC Ogg stream; the bulk data follows the properties.
            var (offset, size) = serials[^1];
            writer.Write(new byte[16]);
            var ogg = Encoding.ASCII.GetBytes("OggS-fake-stream");
            writer.Write(0u);
            writer.Write(ogg.Length);
            writer.Write(ogg.Length);
            writer.Write(headerOffset + (int)body.Position + 4);
            writer.Write(ogg);
            serials[^1] = (offset, headerOffset + (int)body.Position - offset);
        }

        var end = body.Position;
        body.Position = exportTable;
        for (var index = 0; index < exports.Length; index++)
        {
            writer.Write(exports[index].Class);
            writer.Write(0); // super
            writer.Write(exports[index].Outer);
            WriteName(exports[index].Name);
            writer.Write(0); // archetype
            writer.Write(0UL); // flags
            writer.Write(serials[index].Size);
            writer.Write(serials[index].Offset);
            writer.Write(new byte[exportSize - 4 * 3 - 8 - 4 - 8 - 4 - 4]);
        }

        body.Position = end;
        var bodyBytes = body.ToArray();
        var firstSerial = serials[0].Offset;

        var file = new MemoryStream();
        var summary = new BinaryWriter(file);
        summary.Write(UE3Compression.PackageTag);
        summary.Write((ushort)872);
        summary.Write((ushort)5);
        summary.Write(firstSerial);
        summary.Write(5);
        summary.Write(Encoding.ASCII.GetBytes("None\0"));
        summary.Write(0u);
        summary.Write(names.Count);
        summary.Write(nameOffset);
        summary.Write(exports.Length);
        summary.Write(exportOffset);
        summary.Write(1 + _classes.Length);
        summary.Write(importOffset);
        summary.Write(firstSerial); // depends
        summary.Write(new byte[4 * 4]);
        summary.Write(new byte[16]);
        summary.Write(0); // generations
        summary.Write(10897);
        summary.Write(0);
        summary.Write(0u);
        summary.Write(0);
        summary.Write(bodyBytes);
        return file.ToArray();
    }
}
