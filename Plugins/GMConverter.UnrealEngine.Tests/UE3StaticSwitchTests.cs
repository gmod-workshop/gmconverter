using System.Text;
using GMConverter.UnrealEngine.Formats.UE3.Materials;
using GMConverter.UnrealEngine.Formats.UE3.Packages;

namespace GMConverter.UnrealEngine.Tests;

public sealed class UE3StaticSwitchTests
{
    private static readonly string[] _classes = ["Material", "MaterialExpressionStaticSwitchParameter", "MaterialInstanceConstant"];
    private static readonly byte[] _guidA = [.. Enumerable.Range(0x10, 16).Select(value => (byte)value)];
    private static readonly byte[] _guidB = [.. Enumerable.Range(0x40, 16).Select(value => (byte)value)];

    [Fact]
    public void InstancesOverrideStaticSwitchesFromTheirNativeParameters()
    {
        var (content, package) = ReadPackage();

        var parameters = UE3MaterialParameters.Collect(content, new UE3ObjectReference(package, package.Exports[3]));

        // MI_Test overrides Use_A on; Use_B keeps its expression's default, so it isn't listed.
        Assert.Equal(new Dictionary<string, bool> { ["Use_A"] = true }, parameters.StaticSwitches);
    }

    [Fact]
    public void NearerInstancesWinAndInheritTheRest()
    {
        var (content, package) = ReadPackage();

        var parameters = UE3MaterialParameters.Collect(content, new UE3ObjectReference(package, package.Exports[4]));

        // MI_Child overrides Use_B off and leaves Use_A to its parent, MI_Test.
        Assert.False(parameters.StaticSwitches["Use_B"]);
        Assert.True(parameters.StaticSwitches["Use_A"]);
    }

    [Fact]
    public void InstancesWithoutAStaticPermutationHaveNoSwitches()
    {
        var (content, package) = ReadPackage();

        var parameters = UE3MaterialParameters.Collect(content, new UE3ObjectReference(package, package.Exports[5]));

        Assert.Empty(parameters.StaticSwitches);
    }

    private static (UE3GameContent Content, UE3Package Package) ReadPackage()
    {
        var directory = PskFixtures.CreateDirectory();
        var path = Path.Join(directory, "materials");
        File.WriteAllBytes(path, WritePackage());
        var content = UE3GameContent.Open(directory);
        return (content, content.GetPackage(path));
    }

    // An uncompressed v872 package: M_Base with switches Use_A and Use_B; MI_Test (static permutation:
    // a decoy entry naming another switch with Use_A's GUID, Use_A on and overridden, Use_B not
    // overridden); MI_Child of MI_Test (Use_B off and overridden); MI_Plain, whose tail looks like
    // switches but has no static permutation.
    private static byte[] WritePackage()
    {
        List<string> names = ["None", "Core", "Engine", "Package", "Class", "Expressions", "ArrayProperty", "ParameterName", "NameProperty",
            "ExpressionGUID", "StructProperty", "Guid", "DefaultValue", "BoolProperty", "Parent", "ObjectProperty", "bHasStaticPermutationResource",
            "Use_A", "Use_B", "Use_Wrong", "M_Base", "Switch_A", "Switch_B", "MI_Test", "MI_Child", "MI_Plain", .. _classes];
        int Name(string name) => names.IndexOf(name);

        using var body = new MemoryStream();
        using var writer = new BinaryWriter(body);
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

        void Switch(string name, int value, int overridden, byte[] guid)
        {
            WriteName(name);
            writer.Write(value);
            writer.Write(overridden);
            writer.Write(guid);
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
            ("M_Base", Class("Material"), 0),
            ("Switch_A", Class("MaterialExpressionStaticSwitchParameter"), 1),
            ("Switch_B", Class("MaterialExpressionStaticSwitchParameter"), 1),
            ("MI_Test", Class("MaterialInstanceConstant"), 0),
            ("MI_Child", Class("MaterialInstanceConstant"), 0),
            ("MI_Plain", Class("MaterialInstanceConstant"), 0),
        ];
        const int exportSize = 4 * 3 + 8 + 4 + 8 + 4 + 4 + 4 + 4 + 16 + 4;
        var exportOffset = headerOffset + (int)body.Position;
        var exportTable = body.Position;
        writer.Write(new byte[exportSize * exports.Length]);

        List<(int Offset, int Size)> serials = [];
        void Serial(Action properties, Action? native = null)
        {
            var start = headerOffset + (int)body.Position;
            writer.Write(0); // net index
            properties();
            WriteName("None");
            native?.Invoke();
            serials.Add((start, headerOffset + (int)body.Position - start));
        }

        void Instance(int parent, bool staticPermutation)
        {
            Tag("Parent", "ObjectProperty", 4);
            writer.Write(parent);
            if (staticPermutation)
            {
                Tag("bHasStaticPermutationResource", "BoolProperty", 0);
                writer.Write((byte)1);
            }
        }

        void SwitchExpression(string name, byte[] guid, bool defaultValue)
        {
            Tag("ParameterName", "NameProperty", 8);
            WriteName(name);
            Tag("ExpressionGUID", "StructProperty", 16);
            WriteName("Guid");
            writer.Write(guid);
            Tag("DefaultValue", "BoolProperty", 0);
            writer.Write((byte)(defaultValue ? 1 : 0));
        }

        Serial(() =>
        {
            Tag("Expressions", "ArrayProperty", 12);
            writer.Write(2);
            writer.Write(2);
            writer.Write(3);
        });
        Serial(() => SwitchExpression("Use_A", _guidA, false));
        Serial(() => SwitchExpression("Use_B", _guidB, true));
        Serial(() => Instance(1, staticPermutation: true), () =>
        {
            writer.Write(new byte[12]);
            Switch("Use_Wrong", 0, 1, _guidA);
            writer.Write(2);
            Switch("Use_A", 1, 1, _guidA);
            Switch("Use_B", 0, 0, _guidB);
        });
        Serial(() => Instance(4, staticPermutation: true), () =>
        {
            writer.Write(2);
            Switch("Use_A", 0, 0, _guidA);
            Switch("Use_B", 0, 1, _guidB);
        });
        Serial(() => Instance(1, staticPermutation: false), () => Switch("Use_A", 1, 1, _guidA));

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

        using var file = new MemoryStream();
        using var summary = new BinaryWriter(file);
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
