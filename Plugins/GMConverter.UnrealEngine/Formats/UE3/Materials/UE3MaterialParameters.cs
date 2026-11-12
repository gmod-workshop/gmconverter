using System.Buffers.Binary;
using System.Numerics;
using GMConverter.UnrealEngine.Formats.UE3.Packages;

namespace GMConverter.UnrealEngine.Formats.UE3.Materials;

/// <summary>
/// The parameter values a material instance chain sets, with the nearest instance winning, and the
/// base material at the end of the chain.
/// </summary>
internal sealed class UE3MaterialParameters
{
    private const int _maximumChainLength = 16;

    private UE3MaterialParameters(UE3ObjectReference material, UE3ObjectReference? baseMaterial)
    {
        Material = material;
        BaseMaterial = baseMaterial;
    }

    /// <summary>The material or instance the mesh uses.</summary>
    public UE3ObjectReference Material { get; }

    /// <summary>The <c>Material</c> at the end of the instance chain, holding the expression graph; null when it couldn't be found.</summary>
    public UE3ObjectReference? BaseMaterial { get; }

    public Dictionary<string, float> Scalars { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Vector4> Vectors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, UE3ObjectReference> Textures { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, bool> StaticSwitches { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static UE3MaterialParameters Collect(UE3GameContent content, UE3ObjectReference material)
    {
        UE3ObjectReference? current = material;
        List<UE3ObjectReference> instances = [];
        for (var depth = 0; depth < _maximumChainLength && current is { } item; depth++)
        {
            if (item.ClassName.Equals("Material", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            instances.Add(item);
            var (_, properties) = item.Package.OpenObject(item.Export);
            current = content.Resolve(item.Package, properties.GetObject("Parent")) is { } parent
                ? new UE3ObjectReference(parent.Package, parent.Export)
                : null;
        }

        var parameters = new UE3MaterialParameters(
            material,
            current is { } baseMaterial && baseMaterial.ClassName.Equals("Material", StringComparison.OrdinalIgnoreCase) ? baseMaterial : null);
        var switches = parameters.BaseMaterial is { } graph ? SwitchExpressions(graph) : [];
        foreach (var instance in instances)
        {
            parameters.AddInstanceValues(content, instance);
            parameters.AddStaticSwitches(instance, switches);
        }

        return parameters;
    }

    // The base material's static switch parameters, each with the GUID that names its expression.
    private static List<(string Name, byte[] Guid)> SwitchExpressions(UE3ObjectReference baseMaterial)
    {
        var package = baseMaterial.Package;
        List<(string, byte[])> switches = [];
        foreach (var index in package.OpenObject(baseMaterial.Export).Properties.GetObjectArray("Expressions"))
        {
            if (package.GetExport(index) is not { } node || package.GetClassName(node) != "MaterialExpressionStaticSwitchParameter")
            {
                continue;
            }

            var properties = package.OpenObject(node).Properties;
            if (properties.GetName("ParameterName") is { } name && properties.Find("ExpressionGUID") is { Type: "StructProperty", Size: 16 } guid)
            {
                switches.Add((name, package.GetBytes(guid.ValueOffset, 16).ToArray()));
            }
        }

        return switches;
    }

    // A cooked instance with its own static permutation keeps its static parameters after its tagged
    // properties, outside them: each switch as its name, value, override flag (all 32-bit) and its
    // expression's GUID. Only overridden switches count; the rest stay at the expression's default.
    private void AddStaticSwitches(UE3ObjectReference instance, List<(string Name, byte[] Guid)> switches)
    {
        var package = instance.Package;
        var (archive, properties) = package.OpenObject(instance.Export);
        if (switches.Count == 0 || properties.GetBool("bHasStaticPermutationResource") != true)
        {
            return;
        }

        var start = archive.Position;
        var tail = archive.Data.AsSpan(start, instance.Export.SerialOffset + instance.Export.SerialSize - start);
        foreach (var (name, guid) in switches)
        {
            for (var at = tail.IndexOf(guid); at >= 0; at = NextIndexOf(tail, guid, at))
            {
                if (at < 16 || !IsName(package, tail[(at - 16)..], name))
                {
                    continue;
                }

                var value = BinaryPrimitives.ReadInt32LittleEndian(tail[(at - 8)..]);
                var overridden = BinaryPrimitives.ReadInt32LittleEndian(tail[(at - 4)..]);
                if (value is 0 or 1 && overridden is 0 or 1)
                {
                    if (overridden == 1)
                    {
                        StaticSwitches.TryAdd(name, value == 1);
                    }

                    break;
                }
            }
        }
    }

    private static int NextIndexOf(ReadOnlySpan<byte> data, ReadOnlySpan<byte> value, int previous)
    {
        var next = data[(previous + 1)..].IndexOf(value);
        return next < 0 ? -1 : previous + 1 + next;
    }

    private static bool IsName(UE3Package package, ReadOnlySpan<byte> data, string name)
    {
        var index = BinaryPrimitives.ReadInt32LittleEndian(data);
        return BinaryPrimitives.ReadInt32LittleEndian(data[4..]) == 0 && index >= 0 && index < package.Names.Count &&
            package.Names[index].Equals(name, StringComparison.OrdinalIgnoreCase);
    }

    // Values from nearer instances were added first, so later (further) ones never overwrite them.
    private void AddInstanceValues(UE3GameContent content, UE3ObjectReference instance)
    {
        var package = instance.Package;
        var version = package.Summary.FileVersion;
        var (_, properties) = package.OpenObject(instance.Export);
        foreach (var value in properties.GetStructArray("ScalarParameterValues", version))
        {
            if (value.GetName("ParameterName") is { } name && value.GetFloat("ParameterValue") is { } scalar)
            {
                Scalars.TryAdd(name, scalar);
            }
        }

        foreach (var value in properties.GetStructArray("VectorParameterValues", version))
        {
            if (value.GetName("ParameterName") is { } name && value.Find("ParameterValue") is { Size: 16 } color)
            {
                var archive = new UE3Archive(package.GetBytes(color.ValueOffset, 16).ToArray(), package.Names);
                Vectors.TryAdd(name, new Vector4(archive.ReadSingle(), archive.ReadSingle(), archive.ReadSingle(), archive.ReadSingle()));
            }
        }

        foreach (var value in properties.GetStructArray("TextureParameterValues", version))
        {
            if (value.GetName("ParameterName") is { } name &&
                content.Resolve(package, value.GetObject("ParameterValue")) is { } texture)
            {
                Textures.TryAdd(name, new UE3ObjectReference(texture.Package, texture.Export));
            }
        }
    }
}
