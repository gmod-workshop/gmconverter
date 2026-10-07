using System.Text;
using GMConverter.SDK.Common;

namespace GMConverter.UnrealEngine.Formats.Unreal;

internal static class UnrealMaterialExporter
{
    // Sidecar keys carrying a constant UV scroll as "U,V" texture sizes per second (invariant
    // culture). Kept out of the texture channels because reference values are name-normalized.
    // UvScroll follows the diffuse chain; EmissiveUvScroll follows the self-illumination mask,
    // which UE2 shaders often pan independently (e.g. bubbles drifting through a liquid).
    internal const string UvScrollKey = "UvScroll";
    internal const string EmissiveUvScrollKey = "EmissiveUvScroll";

    // UE2 TexPanner defaults: PanRate 0.1 and a zero PanDirection, which pans along +U.
    private const float _defaultPanRate = 0.1f;
    private const float _rotatorUnitsPerRadian = 32768f / MathF.PI;

    private static readonly IReadOnlyDictionary<string, string> _shaderChannels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Diffuse"] = "Diffuse",
        ["NormalMap"] = "Normal",
        ["Opacity"] = "Opacity",
        ["Specular"] = "Specular",
        ["SpecularityMask"] = "Specular",
        ["Bumpmap"] = "Normal",
        ["Detail"] = "Diffuse",
        ["SelfIllumination"] = "SelfIllumination",
        ["SelfIlluminationMask"] = "SelfIlluminationMask"
    };

    public static IReadOnlyList<string> ExportMaterials(
        UnrealPackageFile sourcePackage,
        IReadOnlyList<int> materialReferences,
        int materialCount,
        string outputDirectory,
        string searchRoot)
    {
        var resolver = new UnrealPackageResolver(searchRoot);
        List<string> materialNames = new(materialCount);
        HashSet<string> usedMaterialNames = new(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < materialCount; i++)
        {
            var materialReference = i < materialReferences.Count ? materialReferences[i] : 0;
            var material = ResolveMaterial(sourcePackage, materialReference, i, outputDirectory, resolver);
            var materialName = MakeUniqueMaterialName(material.Name, usedMaterialNames, i);
            materialNames.Add(materialName);

            if (material.TextureReferences.Count > 0)
            {
                WriteMaterialSidecar(outputDirectory, materialName, material.TextureReferences);
            }
        }

        return materialNames;
    }

    private static UnrealExportedMaterial ResolveMaterial(
        UnrealPackageFile sourcePackage,
        int materialReference,
        int materialIndex,
        string outputDirectory,
        UnrealPackageResolver resolver)
    {
        if (materialReference == 0)
        {
            return new UnrealExportedMaterial($"material_{materialIndex}", new Dictionary<string, string>());
        }

        var materialObject = resolver.Resolve(sourcePackage, materialReference);
        var materialName = NameHelpers.SanitizeMaterialName(materialObject.ObjectName);
        Dictionary<string, string> textureReferences = new(StringComparer.OrdinalIgnoreCase);
        PopulateTextureReferences(
            materialObject,
            outputDirectory,
            resolver,
            textureReferences,
            []);

        return new UnrealExportedMaterial(materialName, textureReferences);
    }

    private static void PopulateTextureReferences(
        UnrealResolvedObject materialObject,
        string outputDirectory,
        UnrealPackageResolver resolver,
        Dictionary<string, string> textureReferences,
        HashSet<string> visitedObjects)
    {
        var objectKey = GetObjectKey(materialObject);
        if (!visitedObjects.Add(objectKey))
        {
            return;
        }

        if (IsTexture(materialObject))
        {
            var texture = UnrealTextureExporter.ExportTexture(materialObject, outputDirectory);
            if (texture is not null)
            {
                textureReferences.TryAdd("Diffuse", texture.Name);
            }

            return;
        }

        var properties = ReadObjectProperties(materialObject);
        if (properties is null || materialObject.Package is null)
        {
            return;
        }

        if (PopulateWrapperTextureReferences(materialObject, properties, outputDirectory, resolver, textureReferences, visitedObjects))
        {
            return;
        }

        foreach (var (propertyName, channelName) in _shaderChannels)
        {
            var textureReference = properties.FirstObjectReference(propertyName);
            if (textureReference is null || textureReference.Value == 0)
            {
                continue;
            }

            var textureObject = resolver.Resolve(materialObject.Package, textureReference.Value);
            PopulateTextureReference(
                textureObject,
                channelName,
                outputDirectory,
                resolver,
                textureReferences,
                visitedObjects);
        }

        if (textureReferences.Count == 0)
        {
            PopulateFallbackMaterialReference(
                materialObject.Package,
                properties,
                outputDirectory,
                resolver,
                textureReferences,
                visitedObjects);
        }
    }

    private static bool PopulateWrapperTextureReferences(
        UnrealResolvedObject materialObject,
        UnrealPropertyCollection properties,
        string outputDirectory,
        UnrealPackageResolver resolver,
        Dictionary<string, string> textureReferences,
        HashSet<string> visitedObjects)
    {
        if (materialObject.Package is null)
        {
            return false;
        }

        if (materialObject.ClassName.Equals("Combiner", StringComparison.OrdinalIgnoreCase))
        {
            PopulateFirstAvailableReference(
                materialObject.Package,
                properties,
                ["Material2", "Material1", "Mask"],
                "Diffuse",
                outputDirectory,
                resolver,
                textureReferences,
                visitedObjects);
            return true;
        }

        var wrappedMaterialReference = properties.FirstObjectReference("Material");
        if (wrappedMaterialReference is null || wrappedMaterialReference.Value == 0)
        {
            return false;
        }

        RecordTexturePanner(materialObject, properties, "Diffuse", textureReferences);
        PopulateTextureReference(
            resolver.Resolve(materialObject.Package, wrappedMaterialReference.Value),
            "Diffuse",
            outputDirectory,
            resolver,
            textureReferences,
            visitedObjects);

        if (IsFinalBlendTranslucent(materialObject, properties) &&
            textureReferences.TryGetValue("Diffuse", out var diffuseTexture))
        {
            textureReferences.TryAdd("Opacity", diffuseTexture);
        }

        return true;
    }

    private static void PopulateFallbackMaterialReference(
        UnrealPackageFile package,
        UnrealPropertyCollection properties,
        string outputDirectory,
        UnrealPackageResolver resolver,
        Dictionary<string, string> textureReferences,
        HashSet<string> visitedObjects)
    {
        var fallbackMaterialReference = properties.FirstObjectReference("FallbackMaterial");
        if (fallbackMaterialReference is null || fallbackMaterialReference.Value == 0)
        {
            return;
        }

        PopulateTextureReference(
            resolver.Resolve(package, fallbackMaterialReference.Value),
            "Diffuse",
            outputDirectory,
            resolver,
            textureReferences,
            visitedObjects);
    }

    private static void PopulateFirstAvailableReference(
        UnrealPackageFile package,
        UnrealPropertyCollection properties,
        IReadOnlyList<string> propertyNames,
        string channelName,
        string outputDirectory,
        UnrealPackageResolver resolver,
        Dictionary<string, string> textureReferences,
        HashSet<string> visitedObjects)
    {
        foreach (var propertyName in propertyNames)
        {
            var materialReference = properties.FirstObjectReference(propertyName);
            if (materialReference is null || materialReference.Value == 0)
            {
                continue;
            }

            PopulateTextureReference(
                resolver.Resolve(package, materialReference.Value),
                channelName,
                outputDirectory,
                resolver,
                textureReferences,
                visitedObjects);
            if (textureReferences.ContainsKey(channelName))
            {
                return;
            }
        }
    }

    private static bool IsFinalBlendTranslucent(UnrealResolvedObject materialObject, UnrealPropertyCollection properties)
    {
        if (!materialObject.ClassName.Equals("FinalBlend", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var frameBufferBlending = properties.FirstInteger("FrameBufferBlending");
        var alphaTest = properties.FirstInteger("AlphaTest");
        return frameBufferBlending is >= 2 and <= 4 || alphaTest is > 0;
    }

    private static void PopulateTextureReference(
        UnrealResolvedObject materialObject,
        string channelName,
        string outputDirectory,
        UnrealPackageResolver resolver,
        Dictionary<string, string> textureReferences,
        HashSet<string> visitedObjects)
    {
        if (IsTexture(materialObject))
        {
            var texture = UnrealTextureExporter.ExportTexture(materialObject, outputDirectory);
            if (texture is not null)
            {
                textureReferences.TryAdd(channelName, texture.Name);
            }

            return;
        }

        var objectKey = GetObjectKey(materialObject) + ":" + channelName;
        if (!visitedObjects.Add(objectKey))
        {
            return;
        }

        var properties = ReadObjectProperties(materialObject);
        if (properties is null || materialObject.Package is null)
        {
            return;
        }

        var wrappedMaterialReference = properties.FirstObjectReference("Material");
        if (wrappedMaterialReference is not null && wrappedMaterialReference.Value != 0)
        {
            RecordTexturePanner(materialObject, properties, channelName, textureReferences);
            PopulateTextureReference(
                resolver.Resolve(materialObject.Package, wrappedMaterialReference.Value),
                channelName,
                outputDirectory,
                resolver,
                textureReferences,
                visitedObjects);
            return;
        }

        PopulateTextureReferences(materialObject, outputDirectory, resolver, textureReferences, visitedObjects);
    }

    // Only the diffuse and self-illumination-mask chains map to exporter layers that can scroll
    // independently; the first panner found on each wins, matching TryAdd for texture channels.
    private static void RecordTexturePanner(
        UnrealResolvedObject materialObject,
        UnrealPropertyCollection properties,
        string channelName,
        Dictionary<string, string> textureReferences)
    {
        if (!materialObject.ClassName.Equals("TexPanner", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string scrollKey;
        if (channelName.Equals("Diffuse", StringComparison.OrdinalIgnoreCase))
        {
            scrollKey = UvScrollKey;
        }
        else if (channelName.Equals("SelfIlluminationMask", StringComparison.OrdinalIgnoreCase))
        {
            scrollKey = EmissiveUvScrollKey;
        }
        else
        {
            return;
        }

        var rate = properties.FirstFloat("PanRate") ?? _defaultPanRate;
        var yaw = (properties.FirstInteger("PanDirection.Yaw") ?? 0) / _rotatorUnitsPerRadian;
        var u = rate * MathF.Cos(yaw);
        var v = rate * MathF.Sin(yaw);
        if (MathF.Abs(u) < 1e-6f && MathF.Abs(v) < 1e-6f)
        {
            return;
        }

        textureReferences.TryAdd(scrollKey, FormattableString.Invariant($"{u:R},{v:R}"));
    }

    private static UnrealPropertyCollection? ReadObjectProperties(UnrealResolvedObject materialObject)
    {
        if (materialObject.Package is null || materialObject.Export is null)
        {
            return null;
        }

        using var stream = File.OpenRead(materialObject.Package.FilePath);
        using var binaryReader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        var reader = new UnrealObjectReader(materialObject.Package, binaryReader, materialObject.Export);
        try
        {
            return reader.ReadProperties();
        }
        catch (GMConverterException)
        {
            return null;
        }
    }

    private static bool IsTexture(UnrealResolvedObject materialObject)
    {
        return materialObject.ClassName.Contains("Texture", StringComparison.OrdinalIgnoreCase) ||
            materialObject.ClassName.Equals("BitmapMaterial", StringComparison.OrdinalIgnoreCase);
    }

    private static string MakeUniqueMaterialName(string materialName, HashSet<string> usedMaterialNames, int materialIndex)
    {
        if (string.IsNullOrWhiteSpace(materialName))
        {
            materialName = $"material_{materialIndex}";
        }

        var uniqueName = materialName;
        var duplicateIndex = 1;
        while (!usedMaterialNames.Add(uniqueName))
        {
            uniqueName = $"{materialName}_{duplicateIndex}";
            duplicateIndex++;
        }

        return uniqueName;
    }

    private static void WriteMaterialSidecar(
        string outputDirectory,
        string materialName,
        IReadOnlyDictionary<string, string> textureReferences)
    {
        var outputPath = Path.Combine(outputDirectory, materialName + ".mat");
        List<string> lines = [];
        foreach (var (channel, textureName) in textureReferences)
        {
            lines.Add($"{channel}={textureName}");
        }

        File.WriteAllLines(outputPath, lines);
    }

    private static string GetObjectKey(UnrealResolvedObject materialObject)
    {
        return materialObject.Package is null || materialObject.Export is null
            ? $"{materialObject.ClassName}:{materialObject.ObjectName}"
            : $"{materialObject.Package.FilePath}:{materialObject.Export.SerialOffset}:{materialObject.Export.SerialSize}";
    }

    private sealed record UnrealExportedMaterial(string Name, IReadOnlyDictionary<string, string> TextureReferences);
}
