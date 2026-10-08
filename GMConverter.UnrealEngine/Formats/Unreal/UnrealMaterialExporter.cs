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

    // Static and animated texture-coordinate transforms beyond a plain scroll (TexScaler,
    // TexOscillator, TexRotator), as ";"-separated "name=values" segments; the first segment of a
    // name wins, so the outermost modifier decides. UvTransform follows the diffuse chain and
    // DetailUvTransform the detail layer (where scrolls also land, as "scroll=u,v"). Segments:
    // scale=u,v; center=u,v; rotation=degrees; rotationrate=degrees per second; and
    // u= / v=kind,amplitude,rate,phase with kind pan, stretch or jitter.
    internal const string UvTransformKey = "UvTransform";
    internal const string DetailUvTransformKey = "DetailUvTransform";

    // A Combiner that adds or multiplies two textures on the diffuse chain: Detail is the second
    // texture and DetailBlend how it combines ("Add", "Multiply", "Multiply2X" or "Multiply4X").
    internal const string DetailBlendKey = "DetailBlend";

    // A FadeColor on the self-illumination chain: SelfIlluminationColor holds Color1 and this key
    // "R,G,B,period,phase" for Color2 and the timing, in seconds.
    internal const string SelfIlluminationFadeKey = "SelfIlluminationFade";

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
    private const float _rotatorUnitsPerDegree = 65536f / 360f;

    // UE2 TexOscillator defaults and EOscillationType (OT_*) values.
    private const float _defaultOscillationRate = 1f;
    private const float _defaultOscillationAmplitude = 0.1f;
    private const int _oscillationStretch = 1;
    private const int _oscillationStretchRepeat = 2;
    private const int _oscillationJitter = 3;

    // UE2 TexRotator.TexRotationType: TR_ConstantlyRotating spins at Rotation per second; fixed
    // and oscillating rotations are written as their fixed angle.
    private const int _rotationConstant = 1;

    // Texture size assumed for a TexRotator pivot (given in texels) when the rotated material is
    // not itself a texture.
    private const int _defaultTextureSize = 256;
    private const int _maxChainDepth = 8;

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
        // also uses directly stays a plain material instead of becoming a skin of another, and
        // a sibling the material is built from (e.g. the Combiner inside a FinalBlend) is a
        // building block rather than an alternative.
        var meshMaterialKeys = materialObjects.OfType<UnrealResolvedObject>().Select(GetObjectKey).ToHashSet();
        for (var i = 0; i < materialCount; i++)
        {
            var textureReferences = new Dictionary<string, string>(materials[i].TextureReferences, StringComparer.OrdinalIgnoreCase);
            List<string> variantNames = [];
            foreach (var variantObject in FindVariantObjects(materialObjects[i])
                .Where(variant => !meshMaterialKeys.Contains(GetObjectKey(variant)) && !materials[i].Uses(variant)))
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
        HashSet<string> visitedObjects = [];
        PopulateTextureReferences(
            materialObject,
            outputDirectory,
            resolver,
            textureReferences,
            visitedObjects);

        // Alpha-blended and masked materials read coverage from the diffuse alpha; UE2 often
        // leaves the Opacity channel empty and relies on the diffuse texture's own alpha.
        if (textureReferences.TryGetValue(BlendKey, out var blend) &&
            blend is "Translucent" or "Masked" &&
            textureReferences.TryGetValue("Diffuse", out var diffuse))
        {
            textureReferences.TryAdd("Opacity", diffuse);
        }

        return new UnrealExportedMaterial(materialName, textureReferences, visitedObjects);
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
        RecordUvModifier(materialObject, properties, "Diffuse", resolver, textureReferences);
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
                textureReferences.TryAdd(SelfIlluminationColorKey, FormatColor(color, "Color"));

                textureReferences.TryAdd(SelfIlluminationColorOperationKey, operation == _combineAdd ? "Add" : MultiplyOperation(properties));
                PopulateTextureReference(texture, channelName, outputDirectory, resolver, textureReferences, visitedObjects);
                return;
            }
        }

        // Two textures added or multiplied on the diffuse: one is the base and the other becomes a
        // detail layer with its own coordinates; the operand carrying a texture modifier is the
        // layer. An operand read from another UV channel (a lightmap) can't be layered over UV 0,
        // so only the other operand is kept.
        if (operation is _combineAdd or _combineMultiply &&
            channelName.Equals("Diffuse", StringComparison.OrdinalIgnoreCase) &&
            material1 is not null && material2 is not null &&
            !IsColorSource(material1) && !IsColorSource(material2))
        {
            var secondary1 = UsesSecondaryUvChannel(material1, resolver);
            var secondary2 = UsesSecondaryUvChannel(material2, resolver);
            if (secondary1 != secondary2)
            {
                PopulateTextureReference(secondary1 ? material2 : material1, "Diffuse", outputDirectory, resolver, textureReferences, visitedObjects);
                return;
            }

            var (baseOperand, layer) = IsUvModifier(material1) && !IsUvModifier(material2) ? (material2, material1) : (material1, material2);
            PopulateTextureReference(baseOperand, "Diffuse", outputDirectory, resolver, textureReferences, visitedObjects);
            PopulateTextureReference(layer, "Detail", outputDirectory, resolver, textureReferences, visitedObjects);
            if (textureReferences.ContainsKey("Detail"))
            {
                textureReferences.TryAdd(DetailBlendKey, operation == _combineAdd ? "Add" : MultiplyOperation(properties));
            }

            return;
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

    // UE2 applies Modulate2X/4X to the multiply only.
    private static string MultiplyOperation(UnrealPropertyCollection properties)
    {
        return properties.FirstInteger("Modulate4X") is > 0 ? "Multiply4X"
            : properties.FirstInteger("Modulate2X") is > 0 ? "Multiply2X"
            : "Multiply";
    }

    private static bool IsColorSource(UnrealResolvedObject materialObject)
    {
        return materialObject.ClassName.Equals("ConstantColor", StringComparison.OrdinalIgnoreCase) ||
            materialObject.ClassName.Equals("FadeColor", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUvModifier(UnrealResolvedObject materialObject)
    {
        return materialObject.ClassName is "TexPanner" or "TexPanner2D" or "TexScaler" or "TexOscillator" or "TexOscillatorTriggered" or "TexRotator";
    }

    // Whether a TexCoordSource down the material's Material chain reads a UV channel other than 0.
    private static bool UsesSecondaryUvChannel(UnrealResolvedObject materialObject, UnrealPackageResolver resolver)
    {
        var current = materialObject;
        for (var depth = 0; depth < _maxChainDepth && !IsTexture(current); depth++)
        {
            var properties = ReadObjectProperties(current);
            if (properties is null || current.Package is null)
            {
                return false;
            }

            if (current.ClassName.Equals("TexCoordSource", StringComparison.OrdinalIgnoreCase))
            {
                return properties.FirstInteger("SourceChannel") is > 0;
            }

            var wrapped = properties.FirstObjectReference("Material");
            if (wrapped is null || wrapped.Value == 0)
            {
                return false;
            }

            current = resolver.Resolve(current.Package, wrapped.Value);
        }

        return false;
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

        // A plain or pulsing colour as the glow: the importer uses it in place of a texture.
        if (channelName.Equals("SelfIllumination", StringComparison.OrdinalIgnoreCase) && IsColorSource(materialObject))
        {
            var fade = materialObject.ClassName.Equals("FadeColor", StringComparison.OrdinalIgnoreCase);
            textureReferences.TryAdd(SelfIlluminationColorKey, FormatColor(properties, fade ? "Color1" : "Color"));
            textureReferences.TryAdd(SelfIlluminationColorOperationKey, "Replace");
            if (fade)
            {
                textureReferences.TryAdd(SelfIlluminationFadeKey, FormatColor(properties, "Color2") + FormattableString.Invariant(
                    $",{properties.FirstFloat("FadePeriod") ?? 1f:R},{properties.FirstFloat("FadePhase") ?? 0f:R}"));
            }

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
            RecordUvModifier(materialObject, properties, channelName, resolver, textureReferences);
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
        else if (channelName.Equals("Detail", StringComparison.OrdinalIgnoreCase))
        {
            scrollKey = DetailUvTransformKey;
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

        if (scrollKey == DetailUvTransformKey)
        {
            AppendTransform(textureReferences, scrollKey, FormattableString.Invariant($"scroll={u:R},{v:R}"));
            return;
        }

        textureReferences.TryAdd(scrollKey, FormattableString.Invariant($"{u:R},{v:R}"));
    }

    // TexScaler, TexOscillator and TexRotator on the diffuse or detail chain.
    private static void RecordUvModifier(
        UnrealResolvedObject materialObject,
        UnrealPropertyCollection properties,
        string channelName,
        UnrealPackageResolver resolver,
        Dictionary<string, string> textureReferences)
    {
        string key;
        if (channelName.Equals("Diffuse", StringComparison.OrdinalIgnoreCase))
        {
            key = UvTransformKey;
        }
        else if (channelName.Equals("Detail", StringComparison.OrdinalIgnoreCase))
        {
            key = DetailUvTransformKey;
        }
        else
        {
            return;
        }

        switch (materialObject.ClassName)
        {
            case "TexScaler":
                var scaleU = properties.FirstFloat("UScale") ?? 1f;
                var scaleV = properties.FirstFloat("VScale") ?? 1f;
                if (scaleU != 1f || scaleV != 1f)
                {
                    AppendTransform(textureReferences, key, FormattableString.Invariant($"scale={scaleU:R},{scaleV:R}"));
                }

                break;
            case "TexOscillator" or "TexOscillatorTriggered":
                RecordOscillation(properties, "U", key, textureReferences);
                RecordOscillation(properties, "V", key, textureReferences);
                break;
            case "TexRotator":
                var degrees = (properties.FirstInteger("Rotation.Yaw") ?? 0) / _rotatorUnitsPerDegree;
                AppendTransform(textureReferences, key, FormattableString.Invariant(
                    $"{(properties.FirstInteger("TexRotationType") == _rotationConstant ? "rotationrate" : "rotation")}={degrees:R}"));

                // The pivot is given in texels of the rotated texture.
                var offsetU = properties.FirstFloat("UOffset") ?? 0f;
                var offsetV = properties.FirstFloat("VOffset") ?? 0f;
                if (offsetU != 0f || offsetV != 0f)
                {
                    var (width, height) = WrappedTextureSize(materialObject, resolver);
                    AppendTransform(textureReferences, key, FormattableString.Invariant($"center={offsetU / width:R},{offsetV / height:R}"));
                }

                break;
        }
    }

    // OT_Stretch pivots on the texture centre and OT_StretchRepeat on its origin.
    private static void RecordOscillation(UnrealPropertyCollection properties, string axis, string key, Dictionary<string, string> textureReferences)
    {
        var amplitude = properties.FirstFloat($"{axis}OscillationAmplitude") ?? _defaultOscillationAmplitude;
        if (amplitude == 0f)
        {
            return;
        }

        var type = properties.FirstInteger($"{axis}OscillationType") ?? 0;
        var kind = type switch
        {
            _oscillationStretch or _oscillationStretchRepeat => "stretch",
            _oscillationJitter => "jitter",
            _ => "pan"
        };
        var rate = properties.FirstFloat($"{axis}OscillationRate") ?? _defaultOscillationRate;
        var phase = properties.FirstFloat($"{axis}OscillationPhase") ?? 0f;
        AppendTransform(textureReferences, key, FormattableString.Invariant($"{axis.ToLowerInvariant()}={kind},{amplitude:R},{rate:R},{phase:R}"));
        if (type == _oscillationStretch)
        {
            AppendTransform(textureReferences, key, "center=0.5,0.5");
        }
    }

    private static (float Width, float Height) WrappedTextureSize(UnrealResolvedObject materialObject, UnrealPackageResolver resolver)
    {
        var current = materialObject;
        for (var depth = 0; depth < _maxChainDepth && current.Package is not null; depth++)
        {
            var properties = ReadObjectProperties(current);
            if (properties is null)
            {
                break;
            }

            if (IsTexture(current))
            {
                return (properties.FirstInteger("USize") ?? _defaultTextureSize, properties.FirstInteger("VSize") ?? _defaultTextureSize);
            }

            var wrapped = properties.FirstObjectReference("Material");
            if (wrapped is null || wrapped.Value == 0)
            {
                break;
            }

            current = resolver.Resolve(current.Package, wrapped.Value);
        }

        return (_defaultTextureSize, _defaultTextureSize);
    }

    private static void AppendTransform(Dictionary<string, string> textureReferences, string key, string segment)
    {
        textureReferences[key] = textureReferences.TryGetValue(key, out var existing) ? $"{existing};{segment}" : segment;
    }

    private static string FormatColor(UnrealPropertyCollection properties, string name)
    {
        return FormattableString.Invariant(
            $"{properties.FirstInteger($"{name}.R") ?? 0},{properties.FirstInteger($"{name}.G") ?? 0},{properties.FirstInteger($"{name}.B") ?? 0}");
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

    // ChainKeys are the object keys visited while resolving the material (GetObjectKey, optionally
    // suffixed with ":<channel>"): every building block the material is made of.
    private sealed record UnrealExportedMaterial(
        string Name,
        IReadOnlyDictionary<string, string> TextureReferences,
        IReadOnlySet<string>? ChainKeys = null)
    {
        public bool Uses(UnrealResolvedObject materialObject)
        {
            var key = GetObjectKey(materialObject);
            return ChainKeys?.Any(chainKey => chainKey == key || chainKey.StartsWith(key + ":", StringComparison.Ordinal)) == true;
        }
    }
}
