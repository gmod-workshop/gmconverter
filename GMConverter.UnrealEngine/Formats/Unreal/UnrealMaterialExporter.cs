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

    // Scroll of the self-illumination colour itself (e.g. a flash pattern panning through a fixed
    // glow mask), in the same units as UvScroll.
    internal const string SelfIlluminationUvScrollKey = "SelfIlluminationUvScroll";

    // A Combiner on the self-illumination chain that mixes a ConstantColor into the texture:
    // SelfIlluminationColor is "R,G,B" in 0-255 and SelfIlluminationColorOperation says how it
    // combines with the texture ("Add", "Multiply", "Multiply2X" or "Multiply4X").
    internal const string SelfIlluminationColorKey = "SelfIlluminationColor";
    internal const string SelfIlluminationColorOperationKey = "SelfIlluminationColorOperation";

    // Alternate materials for the same mesh (e.g. a dispenser's On/Warm/Off/Dest states), as a
    // comma-separated list of material names that each have their own sidecar.
    internal const string VariantsKey = "Variants";

    // Sidecar keys for how the material blends: Blend is "Translucent", "Masked" or "Additive",
    // and AlphaRef is the masked cutoff in 0-255 (UE2 keeps pixels whose alpha exceeds it).
    internal const string BlendKey = "Blend";
    internal const string AlphaRefKey = "AlphaRef";

    // UE2 Shader.OutputBlending (OB_*) and FinalBlend.FrameBufferBlending (FB_*) values.
    private const int _outputBlendingMasked = 1;
    private const int _outputBlendingTranslucent = 3;
    private const int _outputBlendingBrighten = 5;
    private const int _frameBufferOverwrite = 0;
    private const int _frameBufferAlphaBlend = 2;
    private const int _frameBufferAlphaModulate = 3;
    private const int _frameBufferTranslucent = 4;
    private const int _frameBufferBrighten = 6;

    // UE2 Combiner.CombineOperation (CO_*) values.
    private const int _combineMultiply = 2;
    private const int _combineAdd = 3;
    private const int _combineAlphaBlendWithMask = 5;

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
        var materialObjects = new UnrealResolvedObject?[materialCount];
        var materials = new UnrealExportedMaterial[materialCount];

        for (var i = 0; i < materialCount; i++)
        {
            var materialReference = i < materialReferences.Count ? materialReferences[i] : 0;
            materialObjects[i] = materialReference == 0 ? null : resolver.Resolve(sourcePackage, materialReference);
            materials[i] = ResolveMaterial(materialObjects[i], i, outputDirectory, resolver);
            materialNames.Add(MakeUniqueMaterialName(materials[i].Name, usedMaterialNames, i));
        }

        // Variants are looked up after every mesh material has its name, so a sibling the mesh
        // also uses directly stays a plain material instead of becoming a skin of another.
        var meshMaterialKeys = materialObjects.OfType<UnrealResolvedObject>().Select(GetObjectKey).ToHashSet();
        for (var i = 0; i < materialCount; i++)
        {
            var textureReferences = new Dictionary<string, string>(materials[i].TextureReferences, StringComparer.OrdinalIgnoreCase);
            List<string> variantNames = [];
            foreach (var variantObject in FindVariantObjects(materialObjects[i]).Where(variant => !meshMaterialKeys.Contains(GetObjectKey(variant))))
            {
                var variant = ResolveMaterial(variantObject, i, outputDirectory, resolver);
                if (!SharesDiffuse(variant, materials[i]))
                {
                    continue;
                }

                var variantName = MakeUniqueMaterialName(variant.Name, usedMaterialNames, i);
                WriteMaterialSidecar(outputDirectory, variantName, variant.TextureReferences);
                variantNames.Add(variantName);
            }

            if (variantNames.Count > 0)
            {
                textureReferences[VariantsKey] = string.Join(',', variantNames);
            }

            if (textureReferences.Count > 0)
            {
                WriteMaterialSidecar(outputDirectory, materialNames[i], textureReferences);
            }
        }

        return materialNames;
    }

    // UE2 games keep a mesh's alternate looks as sibling materials that the game swaps in at run
    // time, e.g. Republic Commando's BDDispenser_Off next to BDDispenser_On, _Warm and _Dest. They
    // are the top-level materials in the same package group whose names share the stem before the
    // last underscore; callers also require the same diffuse texture, which keeps unrelated parts
    // that merely share a prefix (Foo_Body, Foo_Head) apart.
    private static IEnumerable<UnrealResolvedObject> FindVariantObjects(UnrealResolvedObject? materialObject)
    {
        if (materialObject?.Package is not { } package || materialObject.Export is not { } export)
        {
            return [];
        }

        var stemLength = export.ObjectName.LastIndexOf('_');
        if (stemLength <= 0)
        {
            return [];
        }

        var prefix = export.ObjectName[..(stemLength + 1)];
        return package.Exports
            .Where(candidate =>
                candidate != export &&
                candidate.PackageIndex == export.PackageIndex &&
                candidate.ObjectName.Length > prefix.Length &&
                candidate.ObjectName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                IsTopLevelMaterialClass(package.GetClassName(candidate)))
            .OrderBy(candidate => candidate.ObjectName, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => new UnrealResolvedObject(candidate.ObjectName, package.GetClassName(candidate), package, candidate));
    }

    private static bool IsTopLevelMaterialClass(string className)
    {
        return className.Equals("Shader", StringComparison.OrdinalIgnoreCase) ||
            className.Equals("Combiner", StringComparison.OrdinalIgnoreCase) ||
            className.Equals("FinalBlend", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SharesDiffuse(UnrealExportedMaterial variant, UnrealExportedMaterial material)
    {
        return variant.TextureReferences.TryGetValue("Diffuse", out var variantDiffuse) &&
            material.TextureReferences.TryGetValue("Diffuse", out var diffuse) &&
            variantDiffuse.Equals(diffuse, StringComparison.OrdinalIgnoreCase);
    }

    private static UnrealExportedMaterial ResolveMaterial(
        UnrealResolvedObject? materialObject,
        int materialIndex,
        string outputDirectory,
        UnrealPackageResolver resolver)
    {
        if (materialObject is null)
        {
            return new UnrealExportedMaterial($"material_{materialIndex}", new Dictionary<string, string>());
        }

        var materialName = NameHelpers.SanitizeMaterialName(materialObject.ObjectName);
        Dictionary<string, string> textureReferences = new(StringComparer.OrdinalIgnoreCase);
        PopulateTextureReferences(
            materialObject,
            outputDirectory,
            resolver,
            textureReferences,
            []);

        // Alpha-blended and masked materials read coverage from the diffuse alpha; UE2 often
        // leaves the Opacity channel empty and relies on the diffuse texture's own alpha.
        if (textureReferences.TryGetValue(BlendKey, out var blend) &&
            blend is "Translucent" or "Masked" &&
            textureReferences.TryGetValue("Diffuse", out var diffuse))
        {
            textureReferences.TryAdd("Opacity", diffuse);
        }

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

        RecordBlend(materialObject, properties, textureReferences);
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
            PopulateCombinerReferences(materialObject.Package, properties, "Diffuse", outputDirectory, resolver, textureReferences, visitedObjects);
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

    // Two Combiner forms are kept apart instead of flattened to one texture: a masked blend on the
    // diffuse (e.g. damage decals over a body texture) records both layers and the mask for the
    // importer to composite, and a ConstantColor added to or multiplied with the self-illumination
    // texture (e.g. a state tint over a flash pattern) records the colour and operation. Anything
    // else falls back to the first operand that resolves to a texture.
    private static void PopulateCombinerReferences(
        UnrealPackageFile package,
        UnrealPropertyCollection properties,
        string channelName,
        string outputDirectory,
        UnrealPackageResolver resolver,
        Dictionary<string, string> textureReferences,
        HashSet<string> visitedObjects)
    {
        var operation = properties.FirstInteger("CombineOperation");
        var material1 = ResolveOperand(package, properties, "Material1", resolver);
        var material2 = ResolveOperand(package, properties, "Material2", resolver);
        var mask = ResolveOperand(package, properties, "Mask", resolver);

        if (operation == _combineAlphaBlendWithMask &&
            channelName.Equals("Diffuse", StringComparison.OrdinalIgnoreCase) &&
            material1 is not null && material2 is not null && mask is not null)
        {
            var (under, over) = properties.FirstInteger("InvertMask") is > 0 ? (material2, material1) : (material1, material2);
            PopulateTextureReference(under, "Diffuse", outputDirectory, resolver, textureReferences, visitedObjects);
            PopulateTextureReference(over, "DiffuseOverlay", outputDirectory, resolver, textureReferences, visitedObjects);
            PopulateTextureReference(mask, "DiffuseOverlayMask", outputDirectory, resolver, textureReferences, visitedObjects);
            return;
        }

        if (operation is _combineAdd or _combineMultiply &&
            channelName.Equals("SelfIllumination", StringComparison.OrdinalIgnoreCase) &&
            IsConstantColor(material1) != IsConstantColor(material2))
        {
            var (constant, texture) = IsConstantColor(material1) ? (material1!, material2) : (material2!, material1);
            var color = ReadObjectProperties(constant);
            if (texture is not null && color is not null)
            {
                textureReferences.TryAdd(SelfIlluminationColorKey, FormattableString.Invariant(
                    $"{color.FirstInteger("Color.R") ?? 0},{color.FirstInteger("Color.G") ?? 0},{color.FirstInteger("Color.B") ?? 0}"));

                // UE2 applies Modulate2X/4X to the multiply only.
                textureReferences.TryAdd(SelfIlluminationColorOperationKey, operation == _combineAdd
                    ? "Add"
                    : properties.FirstInteger("Modulate4X") is > 0 ? "Multiply4X"
                    : properties.FirstInteger("Modulate2X") is > 0 ? "Multiply2X"
                    : "Multiply");
                PopulateTextureReference(texture, channelName, outputDirectory, resolver, textureReferences, visitedObjects);
                return;
            }
        }

        PopulateFirstAvailableReference(
            package,
            properties,
            ["Material2", "Material1", "Mask"],
            channelName,
            outputDirectory,
            resolver,
            textureReferences,
            visitedObjects);
    }

    private static UnrealResolvedObject? ResolveOperand(
        UnrealPackageFile package,
        UnrealPropertyCollection properties,
        string propertyName,
        UnrealPackageResolver resolver)
    {
        var reference = properties.FirstObjectReference(propertyName);
        return reference is null || reference.Value == 0 ? null : resolver.Resolve(package, reference.Value);
    }

    private static bool IsConstantColor(UnrealResolvedObject? materialObject)
    {
        return materialObject?.ClassName.Equals("ConstantColor", StringComparison.OrdinalIgnoreCase) == true;
    }

    // The outermost Shader or FinalBlend decides; TryAdd keeps the first one recorded.
    private static void RecordBlend(
        UnrealResolvedObject materialObject,
        UnrealPropertyCollection properties,
        Dictionary<string, string> textureReferences)
    {
        string? blend = null;
        if (materialObject.ClassName.Equals("FinalBlend", StringComparison.OrdinalIgnoreCase))
        {
            var alphaTest = properties.FirstInteger("AlphaTest") is > 0;
            blend = (properties.FirstInteger("FrameBufferBlending") ?? _frameBufferOverwrite) switch
            {
                _frameBufferTranslucent or _frameBufferBrighten => "Additive",
                _frameBufferAlphaBlend or _frameBufferAlphaModulate => "Translucent",
                _frameBufferOverwrite when alphaTest => "Masked",
                _ => null
            };
        }
        else if (materialObject.ClassName.Equals("Shader", StringComparison.OrdinalIgnoreCase))
        {
            blend = (properties.FirstInteger("OutputBlending") ?? 0) switch
            {
                _outputBlendingMasked => "Masked",
                _outputBlendingTranslucent => "Translucent",
                _outputBlendingBrighten => "Additive",
                _ => null
            };
        }

        if (blend is null || !textureReferences.TryAdd(BlendKey, blend))
        {
            return;
        }

        if (blend == "Masked")
        {
            textureReferences.TryAdd(
                AlphaRefKey,
                (properties.FirstInteger("AlphaRef") ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
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
            if (channelName.Equals("Diffuse", StringComparison.OrdinalIgnoreCase))
            {
                RecordBlend(materialObject, properties, textureReferences);
            }

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

        // A Combiner inside a channel stays in that channel; treating it as a whole material would
        // file its texture (and any panner inside it) under Diffuse.
        if (materialObject.ClassName.Equals("Combiner", StringComparison.OrdinalIgnoreCase))
        {
            PopulateCombinerReferences(materialObject.Package, properties, channelName, outputDirectory, resolver, textureReferences, visitedObjects);
            return;
        }

        // Other nested materials (e.g. a Shader used as a diffuse) are only meaningful as a whole
        // material on the diffuse chain; elsewhere there is no channel to map them onto.
        if (channelName.Equals("Diffuse", StringComparison.OrdinalIgnoreCase))
        {
            PopulateTextureReferences(materialObject, outputDirectory, resolver, textureReferences, visitedObjects);
        }
    }

    // Only the diffuse, self-illumination and self-illumination-mask chains map to exporter layers
    // that can scroll independently; the first panner found on each wins, matching TryAdd for
    // texture channels.
    private static void RecordTexturePanner(
        UnrealResolvedObject materialObject,
        UnrealPropertyCollection properties,
        string channelName,
        Dictionary<string, string> textureReferences)
    {
        var isPanner = materialObject.ClassName.Equals("TexPanner", StringComparison.OrdinalIgnoreCase);
        var isPanner2D = materialObject.ClassName.Equals("TexPanner2D", StringComparison.OrdinalIgnoreCase);
        if (!isPanner && !isPanner2D)
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
        else if (channelName.Equals("SelfIllumination", StringComparison.OrdinalIgnoreCase))
        {
            scrollKey = SelfIlluminationUvScrollKey;
        }
        else
        {
            return;
        }

        float u;
        float v;
        if (isPanner2D)
        {
            // Republic Commando's TexPanner2D stores per-axis speeds directly.
            u = properties.FirstFloat("SpeedU") ?? 0f;
            v = properties.FirstFloat("SpeedV") ?? 0f;
        }
        else
        {
            var rate = properties.FirstFloat("PanRate") ?? _defaultPanRate;
            var yaw = (properties.FirstInteger("PanDirection.Yaw") ?? 0) / _rotatorUnitsPerRadian;
            u = rate * MathF.Cos(yaw);
            v = rate * MathF.Sin(yaw);
        }

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
