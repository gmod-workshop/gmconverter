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
        foreach (var instance in instances)
        {
            parameters.AddInstanceValues(content, instance);
        }

        return parameters;
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
