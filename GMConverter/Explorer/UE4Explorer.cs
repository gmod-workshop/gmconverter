using System.Globalization;
using System.Text.Json;
using NewtonsoftJson = Newtonsoft.Json;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.GameTypes.FN.Assets.Exports;
using CUE4Parse.GameTypes.FN.Assets.Exports.DataAssets;
using CUE4Parse.UE4.AssetRegistry;
using CUE4Parse.UE4.AssetRegistry.Objects;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.Component;
using CUE4Parse.UE4.Assets.Exports.Component.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.Component.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Animations;
using CUE4Parse_Conversion.Meshes;
using CUE4Parse_Conversion.Textures;
using CUE4Parse_Conversion.UEFormat.Enums;
using GMConverter.Common;
using GMConverter.Formats.PSK;
using GMConverter.Formats.Unreal;

namespace GMConverter.Explorer;

internal sealed class UE4Explorer : IExplorer
{
    private const int _maxPackageProbeCount = 5000;
    private const int _maxResolveDepth = 8;
    private const int _maxResolveVisitedCount = 128;

    private static readonly string[] _meshPropertyNames =
    [
        "AuxiliaryMesh",
        "ActorSaveRecord",
        "ComponentTemplate",
        "HeroDefinition",
        "InheritableComponentHandler",
        "LeftHandMesh",
        "LevelSaveRecord",
        "Mesh",
        "OverrideMesh",
        "PickupSkeletalMesh",
        "PickupStaticMesh",
        "PlaysetPropLevelSaveRecordCollection",
        "SimpleConstructionScript",
        "SkeletalMesh",
        "StaticMesh",
        "WeaponDefinition",
        "WeaponMeshOffhandOverride",
        "WeaponMeshOverride"
    ];
    private static readonly string[] _placeholderMeshPathTerms =
    [
        "Blockout",
        "Graybox",
        "Greybox",
        "WorldGrid"
    ];

    private static readonly HashSet<string> _archiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pak",
        ".utoc"
    };
    private static readonly JsonSerializerOptions _sceneManifestJsonOptions = new() { WriteIndented = true };

    private readonly Dictionary<string, Cue4ParseProviderContext> _providerCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();

    public string Id => "ue4";

    public string DisplayName => "Unreal Engine 4/5 archives";

    public bool Supports(ExplorerTarget target)
    {
        return target.IsDirectory && Cue4ParseProviderFactory.LooksLikeArchiveRoot(target.FullPath) ||
            target.IsFile && _archiveExtensions.Contains(Path.GetExtension(target.FullPath));
    }

    public IReadOnlyList<ExplorerFileEntry> Scan(ExplorerTarget target)
    {
        var root = Cue4ParseProviderFactory.ResolveArchiveDirectory(target.FullPath);
        var providerContext = GetProvider(root);
        var provider = providerContext.Provider;
        var assetRegistryEntries = EnumerateAssetRegistryEntries(providerContext, out var assetRegistryStats).ToArray();
        if (assetRegistryEntries.Length > 0)
        {
            return assetRegistryEntries
                .OrderBy(entry => entry.DisplayPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var packageFiles = provider.Files.Values.Where(IsPackageFile).Take(_maxPackageProbeCount + 1).ToArray();
        if (packageFiles.Length > _maxPackageProbeCount)
        {
            var lockDetails = providerContext.Profile.GetUnavailableEncryptedArchiveMessage(provider);
            var lockSuffix = lockDetails is null ? string.Empty : $" {lockDetails}";
            var registrySuffix = assetRegistryStats.CreateFailureSuffix();
            throw new GMConverterException(
                $"UE4/5 archive contains more than {_maxPackageProbeCount:N0} packages and no usable AssetRegistry.bin. " +
                $"{registrySuffix}" +
                $"A registry is required for large archive scans.{lockSuffix}");
        }

        if (packageFiles.Length == 0 &&
            providerContext.Profile.GetUnavailableEncryptedArchiveMessage(provider) is { } unavailableArchiveMessage)
        {
            throw new GMConverterException(
                "No readable UE4/UE5 package files or asset registries were available after mounting. " +
                "The readable containers did not expose browseable assets, and additional encrypted containers are still locked. " +
                unavailableArchiveMessage);
        }

        return packageFiles
            .SelectMany(file => EnumerateMeshEntries(provider, root, file))
            .OrderBy(entry => entry.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public ExplorerResolvedEntry ResolveEntry(ExplorerFileEntry fileEntry)
    {
        if (fileEntry.ArchivePath is null || fileEntry.ArchiveEntryPath is null)
        {
            throw new GMConverterException("UE4/5 archive entry is missing asset metadata.");
        }

        var provider = GetProvider(fileEntry.ArchivePath).Provider;
        var exportRoot = GetExportRoot(fileEntry);
        ResetExportRoot(fileEntry, exportRoot);

        var sourceExport = provider.LoadPackageObject(fileEntry.ArchiveEntryPath);

        if (IsExportableAnimationObject(sourceExport))
        {
            try
            {
                return ExportResolvedAnimation(fileEntry, exportRoot, sourceExport);
            }
            catch (Exception ex)
            {
                throw new GMConverterException(
                    $"UE4/5 animation export failed for {fileEntry.ArchiveEntryPath}. {ex}");
            }
        }

        ResolvedUnrealMesh[] resolvedExports;
        try
        {
            resolvedExports = ResolveMeshExports(sourceExport, provider);
        }
        catch (Exception ex)
        {
            throw new GMConverterException(
                $"UE4/5 archive scene resolution failed for {fileEntry.ArchiveEntryPath}. {ex}");
        }

        if (resolvedExports.Length == 0)
        {
            throw new GMConverterException(CreateUnsupportedMeshMessage(sourceExport, fileEntry.ArchiveEntryPath));
        }

        return ExportResolvedScene(fileEntry, exportRoot, resolvedExports);
    }

    private static bool IsExportableAnimationObject(UObject export)
    {
        return export is UAnimSequence or UAnimMontage or UAnimComposite;
    }

    private static ExplorerResolvedEntry ExportResolvedAnimation(
        ExplorerFileEntry fileEntry,
        string exportRoot,
        UObject animExport)
    {
        var animRoot = Path.Combine(exportRoot, "__anim");
        Directory.CreateDirectory(animRoot);

        var options = CreateExporterOptions();
        AnimExporter animExporter = animExport switch
        {
            UAnimSequence animSeq => new AnimExporter(animSeq, options),
            UAnimMontage animMontage => new AnimExporter(animMontage, options),
            UAnimComposite animComposite => new AnimExporter(animComposite, options),
            _ => throw new GMConverterException($"Unsupported animation type for PSA export: {animExport.ExportType}")
        };

        animExporter.TryWriteToDir(new DirectoryInfo(animRoot), out _, out _);
        WaitForExportTree(animRoot);

        var psaPath = Directory
            .EnumerateFiles(animRoot, "*.psa", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?? throw new GMConverterException(
                $"CUE4Parse did not export a PSA animation for: {fileEntry.ArchiveEntryPath}");

        var details = $"Resolved UE animation: {Path.GetFileName(psaPath)}. " +
            "Export a matching SkeletalMesh separately and use Set Animation to pair them.";
        return new ExplorerResolvedEntry(psaPath, exportRoot, AnimationPath: psaPath, Details: details);
    }

    private static ExplorerResolvedEntry ExportResolvedScene(
        ExplorerFileEntry fileEntry,
        string exportRoot,
        IReadOnlyList<ResolvedUnrealMesh> resolvedExports)
    {
        List<UnrealSceneManifestEntry> manifestEntries = [];
        List<UnrealScenePartSummary> partSummaries = [];

        for (var i = 0; i < resolvedExports.Count; i++)
        {
            var resolvedExport = resolvedExports[i];
            var partRoot = Path.Combine(exportRoot, "__parts", i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
            var exportedPart = ExportResolvedMeshPart(fileEntry, resolvedExport, partRoot);
            try
            {
                WriteResolvedMaterialOverrides(resolvedExport, exportedPart.MeshPath);
                WriteTextureDataOverrides(resolvedExport, exportedPart.MeshPath, exportRoot);
            }
            catch (Exception ex)
            {
                throw new GMConverterException(
                    $"UE4/5 archive material override export failed for {fileEntry.ArchiveEntryPath}. " +
                    $"Resolved export type: {resolvedExport.Export.ExportType}. {ex}");
            }

            manifestEntries.Add(new UnrealSceneManifestEntry(
                Path.GetRelativePath(exportRoot, exportedPart.MeshPath),
                resolvedExport.Transform));
            partSummaries.Add(CreatePartSummary(resolvedExport, exportedPart));
        }

        var manifestPath = Path.Combine(exportRoot, SanitizeObjectPath(fileEntry.ArchiveEntryPath ?? "UnrealScene") + ".ue4scene");
        var manifest = new UnrealSceneManifest(1, Path.GetFileNameWithoutExtension(manifestPath), manifestEntries);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, _sceneManifestJsonOptions));
        WaitForExportTree(exportRoot);
        return new ExplorerResolvedEntry(manifestPath, exportRoot, Details: CreateResolveDetails(exportRoot, partSummaries));
    }

    private static UnrealMeshExportResult ExportResolvedMeshPart(
        ExplorerFileEntry fileEntry,
        ResolvedUnrealMesh resolvedExport,
        string partRoot)
    {
        try
        {
            return ExportResolvedMeshPart(fileEntry, resolvedExport, partRoot, exportMaterials: true, usedMeshOnlyFallback: false);
        }
        catch (Exception ex) when (IsTextureDecodeFailure(ex))
        {
            var fallbackRoot = partRoot + "_mesh_only";
            return ExportResolvedMeshPart(fileEntry, resolvedExport, fallbackRoot, exportMaterials: false, usedMeshOnlyFallback: true);
        }
    }

    private static UnrealMeshExportResult ExportResolvedMeshPart(
        ExplorerFileEntry fileEntry,
        ResolvedUnrealMesh resolvedExport,
        string partRoot,
        bool exportMaterials,
        bool usedMeshOnlyFallback)
    {
        Directory.CreateDirectory(partRoot);
        using (ApplyMaterialOverrides(resolvedExport.Export, resolvedExport.OverrideMaterials))
        {
            var exporter = new Exporter(resolvedExport.Export, CreateExporterOptions(exportMaterials));
            try
            {
                exporter.TryWriteToDir(new DirectoryInfo(partRoot), out _, out _);
            }
            catch (Exception ex)
            {
                throw new GMConverterException(
                    $"CUE4Parse failed to export resolved mesh for {fileEntry.ArchiveEntryPath}. " +
                    $"Resolved export type: {resolvedExport.Export.ExportType}. {ex.Message}");
            }
        }

        var meshPath = WaitForExportedMesh(partRoot)
            ?? throw new GMConverterException($"CUE4Parse did not export a PSK/PSKX mesh for: {fileEntry.ArchiveEntryPath}");
        return new UnrealMeshExportResult(meshPath, usedMeshOnlyFallback);
    }

    private static bool IsTextureDecodeFailure(Exception ex)
    {
        return ex.ToString().Contains("Detex decompression failed", StringComparison.OrdinalIgnoreCase) ||
            ex.ToString().Contains("texture", StringComparison.OrdinalIgnoreCase);
    }

    private static UnrealScenePartSummary CreatePartSummary(
        ResolvedUnrealMesh resolvedExport,
        UnrealMeshExportResult exportedPart)
    {
        var materialCount = 0;
        try
        {
            materialCount = PSKFile.Read(exportedPart.MeshPath).Materials.Count;
        }
        catch
        {
            materialCount = 0;
        }

        return new UnrealScenePartSummary(
            resolvedExport.Export.Name,
            resolvedExport.Export.ExportType,
            materialCount,
            resolvedExport.TextureData?.Count ?? 0,
            resolvedExport.TextureData is { Count: > 0 }
                ? string.Join(',', resolvedExport.TextureData.Keys.Order())
                : string.Empty,
            resolvedExport.Transform.IsIdentity()
                ? string.Empty
                : resolvedExport.Transform.ToDiagnosticString(),
            resolvedExport.CollectionSource,
            exportedPart.UsedMeshOnlyFallback);
    }

    private static string CreateResolveDetails(string exportRoot, IReadOnlyList<UnrealScenePartSummary> partSummaries)
    {
        var materialFiles = Directory.EnumerateFiles(exportRoot, "*.json", SearchOption.AllDirectories).Count();
        var textureFiles = Directory.EnumerateFiles(exportRoot, "*", SearchOption.AllDirectories)
            .Count(path => Path.GetExtension(path) is ".png" or ".tga" or ".dds" or ".bmp" or ".jpg" or ".jpeg");
        var meshOnlyFallbacks = partSummaries.Count(part => part.UsedMeshOnlyFallback);

        // Write a full per-part transform log alongside the manifest so we can diagnose mesh
        // positioning issues — which traversal path produced each part, with what local transform.
        try
        {
            var transformLogPath = Path.Combine(exportRoot, "__parts_transforms.log");
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < partSummaries.Count; i++)
            {
                var part = partSummaries[i];
                sb.Append(i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture)).Append('\t');
                sb.Append(part.ExportType).Append('\t').Append(part.Name).Append('\t');
                sb.Append("source=").Append(part.CollectionSource.Length == 0 ? "<empty>" : part.CollectionSource).Append('\t');
                sb.Append("transform=").Append(part.TransformDetails.Length == 0 ? "identity" : part.TransformDetails);
                sb.AppendLine();
            }
            File.WriteAllText(transformLogPath, sb.ToString());
        }
        catch
        {
            // Best-effort diagnostic; never block the export.
        }

        var previewParts = partSummaries
            .Take(8)
            .Select(part => $"{part.ExportType}:{part.Name} materials={part.MaterialCount} textureData={part.TextureDataCount}" +
                (string.IsNullOrWhiteSpace(part.TextureDataSlots) ? string.Empty : $" slots={part.TextureDataSlots}") +
                (string.IsNullOrWhiteSpace(part.TransformDetails) ? string.Empty : $" transform={part.TransformDetails}") +
                (string.IsNullOrWhiteSpace(part.CollectionSource) ? string.Empty : $" src={part.CollectionSource}") +
                (part.UsedMeshOnlyFallback ? " mesh-only" : string.Empty));
        var suffix = partSummaries.Count > 8 ? $" + {partSummaries.Count - 8} more" : string.Empty;
        return $"Resolved UE scene: {partSummaries.Count} mesh part(s), {materialFiles} material file(s), " +
            $"{textureFiles} texture file(s), {meshOnlyFallbacks} mesh-only fallback(s). Parts: {string.Join("; ", previewParts)}{suffix}.";
    }

    private static void WriteTextureDataOverrides(ResolvedUnrealMesh resolvedExport, string meshPath, string exportRoot)
    {
        if (resolvedExport.TextureData is not { Count: > 0 })
        {
            return;
        }

        var overrideDirectory = Path.GetDirectoryName(meshPath) ?? exportRoot;
        Directory.CreateDirectory(overrideDirectory);

        // PSK material entries are section-ordered, but Fortnite TextureData is keyed by UE material
        // slot index. Resolve slot N -> material name via the slot-indexed materialInterfaces array
        // (with component-level overrides applied) so the sidecar lands on the correct material.
        using var overrideScope = ApplyMaterialOverrides(resolvedExport.Export, resolvedExport.OverrideMaterials);
        var materialInterfaces = GetResolvedMaterialInterfaces(resolvedExport.Export);

        foreach (var (materialIndex, textureData) in resolvedExport.TextureData)
        {
            if (materialIndex < 0 || materialIndex >= materialInterfaces.Length)
            {
                continue;
            }

            var materialName = materialInterfaces[materialIndex]?.Name;
            if (string.IsNullOrWhiteSpace(materialName))
            {
                continue;
            }
            Dictionary<string, string> textures = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> textureCache = new(StringComparer.OrdinalIgnoreCase);

            // When OverrideMaterial is set it replaces the base material at this slot. Resolve its
            // textures first so the direct Diffuse/Normal/Specular fields can overlay them below.
            if (!textureData.OverrideMaterial.IsNull &&
                TryLoadPackageIndex(textureData.OverrideMaterial, out var overrideExport) &&
                overrideExport is UMaterialInterface overrideMaterial)
            {
                var parameters = new CMaterialParams2();
                overrideMaterial.GetParams(parameters, EMaterialFormat.AllLayers);
                DumpMaterialTextureParameters(
                    parameters,
                    overrideDirectory,
                    materialName,
                    Path.GetFileNameWithoutExtension(meshPath),
                    textures,
                    textureCache);
            }

            // Direct texture references from the TextureData entry are explicit UE assignments and
            // take precedence — record them under canonical channel keys so the importer's direct
            // slot lookup picks them before falling through to the parameter-name scoring path.
            AddTextureReference(textures, "Diffuse", textureData.Diffuse, overrideDirectory);
            AddTextureReference(textures, "Normal", textureData.Normal, overrideDirectory);
            AddTextureReference(textures, "Specular", textureData.Specular, overrideDirectory);

            if (textures.Count == 0)
            {
                continue;
            }

            var materialPath = GetLocalMaterialSidecarPath(overrideDirectory, materialName);
            File.WriteAllText(
                materialPath,
                JsonSerializer.Serialize(new UnrealMaterialTextureOverride(textures), _sceneManifestJsonOptions));
        }
    }

    private static void WriteResolvedMaterialOverrides(ResolvedUnrealMesh resolvedExport, string meshPath)
    {
        var outputDirectory = Path.GetDirectoryName(meshPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return;
        }

        Directory.CreateDirectory(outputDirectory);
        using (ApplyMaterialOverrides(resolvedExport.Export, resolvedExport.OverrideMaterials))
        {
            // PSK materials are section-ordered (one entry per mesh section), while materialInterfaces
            // is slot-ordered. Iterate the slot list directly so each sidecar pairs the right material
            // with the textures resolved from its parameters. The downstream PSK importer keys sidecars
            // by material name, so duplicate sections sharing a material just rewrite the same file.
            var materialInterfaces = GetResolvedMaterialInterfaces(resolvedExport.Export);
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var material in materialInterfaces)
            {
                if (material is null || !written.Add(material.Name))
                {
                    continue;
                }

                var parameters = new CMaterialParams2();
                material.GetParams(parameters, EMaterialFormat.AllLayers);

                Dictionary<string, string> textures = new(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, string> textureCache = new(StringComparer.OrdinalIgnoreCase);
                DumpMaterialTextureParameters(
                    parameters,
                    outputDirectory,
                    material.Name,
                    Path.GetFileNameWithoutExtension(meshPath),
                    textures,
                    textureCache);

                var colors = SelectMaterialColors(parameters, material.Name);
                if (textures.Count == 0 && colors.Count == 0)
                {
                    continue;
                }

                var materialPath = GetLocalMaterialSidecarPath(outputDirectory, material.Name);
                var sidecar = new UnrealMaterialTextureOverride(
                    textures,
                    colors.Count == 0 ? null : new UnrealMaterialParameterOverride(colors));
                File.WriteAllText(materialPath, JsonSerializer.Serialize(sidecar, _sceneManifestJsonOptions));

                WriteMaterialDebugDump(outputDirectory, material, parameters, textures);
            }

            // After per-material sidecars are written, bake any multi-layer materials into single
            // composite textures. The baker reads CUE4Parse mesh UV1 to evaluate FortnitePorting's
            // mask formula per pixel, samples the appropriate source layer, and rewrites the sidecar
            // to point Diffuse/Normals/SpecularMasks at the baked output. Single-layer materials are
            // skipped (no-op).
            try
            {
                var materialInterfacesForBake = GetResolvedMaterialInterfaces(resolvedExport.Export);
                MultiLayerBaker.BakeMultiLayerMaterials(
                    resolvedExport.Export,
                    materialInterfacesForBake,
                    outputDirectory,
                    _sceneManifestJsonOptions);
            }
            catch
            {
                // Baking is best-effort. If it fails, the original (single-layer) sidecar references
                // remain in place — multi-layer parts will look like FortnitePorting's "layer 1 only"
                // approximation rather than the full blend, which is no worse than before this pass.
            }
        }
    }

    // Companion .debug.json next to each material sidecar with the full parameter dump and
    // the parent (master) material chain. Lets us add master-specific alias mappings without
    // re-running the export pipeline — paste the file content into a chat and we can write
    // a FortnitePorting-style class for that master.
    private static void WriteMaterialDebugDump(
        string outputDirectory,
        UMaterialInterface material,
        CMaterialParams2 parameters,
        Dictionary<string, string> resolvedTextures)
    {
        try
        {
            var parentChain = new List<string>();
            var current = material;
            while (current is not null)
            {
                parentChain.Add(current.GetPathName());
                current = (current as UMaterialInstanceConstant)?.Parent as UMaterialInterface;
                if (parentChain.Count >= 16)
                {
                    break;
                }
            }

            var debug = new UnrealMaterialDebugDump(
                MaterialName: material.Name,
                MaterialPath: material.GetPathName(),
                ParentChain: parentChain,
                BlendMode: parameters.BlendMode.ToString(),
                ShadingModel: parameters.ShadingModel.ToString(),
                TextureParameterNames: parameters.Textures
                    .Where(pair => pair.Value is UTexture2D)
                    .ToDictionary(pair => pair.Key, pair => ((UTexture2D)pair.Value).GetPathName(), StringComparer.OrdinalIgnoreCase),
                ScalarParameters: new Dictionary<string, float>(parameters.Scalars, StringComparer.OrdinalIgnoreCase),
                SwitchParameters: new Dictionary<string, bool>(parameters.Switches, StringComparer.OrdinalIgnoreCase),
                ResolvedTextures: new Dictionary<string, string>(resolvedTextures, StringComparer.OrdinalIgnoreCase));

            var debugPath = Path.Combine(
                outputDirectory,
                NameHelpers.SanitizeMaterialName(material.Name) + ".debug.json");
            File.WriteAllText(debugPath, JsonSerializer.Serialize(debug, _sceneManifestJsonOptions));
        }
        catch
        {
            // Debug dumps must never fail an export.
        }
    }

    private static UMaterialInterface?[] GetResolvedMaterialInterfaces(UObject export)
    {
        return export switch
        {
            UStaticMesh staticMesh => staticMesh.Materials is { Length: > 0 }
                ? [.. staticMesh.Materials.Select(material => LoadMaterialInterface(material))]
                : staticMesh.StaticMaterials?
                    .Select(material => LoadMaterialInterface(material.MaterialInterface))
                    .ToArray() ?? [],
            USkeletalMesh skeletalMesh => skeletalMesh.Materials is { Length: > 0 }
                ? [.. skeletalMesh.Materials.Select(material => LoadMaterialInterface(material))]
                : skeletalMesh.SkeletalMaterials?
                    .Select(material => LoadMaterialInterface(material.Material))
                    .ToArray() ?? [],
            _ => []
        };
    }

    private static UMaterialInterface? LoadMaterialInterface(ResolvedObject? material)
    {
        try
        {
            return material?.Load<UMaterialInterface>();
        }
        catch
        {
            return null;
        }
    }

    // Decode every UTexture2D referenced by the material into the sidecar's Textures dict, keyed by
    // parameter name, then promote recognized parameter names to canonical PBR channel keys
    // (Diffuse/Normals/SpecularMasks/Emission). Promotion runs two passes:
    //   1. Alias table (FortnitePorting DefaultMappings + a few master-specific tables) — handles the
    //      ~5% of master materials with bespoke parameter naming.
    //   2. Texture-asset-name regex from CUE4Parse's CMaterialParams2 — handles the ~95% of Fortnite
    //      content that follows the _D/_N/_S/_E texture-suffix convention. When multiple textures
    //      match a channel's regex we score by mesh+material token overlap so the LAAT wing material
    //      gets `T_NobleCrest_Wing_D` instead of `T_NobleCrest_Generic_D`.
    // This combination gives "mostly automatic" coverage without needing a hand-curated class per
    // master material the way FortnitePorting's Blender plugin does.
    private static void DumpMaterialTextureParameters(
        CMaterialParams2 parameters,
        string outputDirectory,
        string materialName,
        string meshName,
        Dictionary<string, string> textures,
        Dictionary<string, string> writtenTextureCache)
    {
        foreach (var (paramName, unrealMaterial) in parameters.Textures)
        {
            if (string.IsNullOrWhiteSpace(paramName) || unrealMaterial is not UTexture2D texture2D)
            {
                continue;
            }

            if (TryGetOrWriteTexture(texture2D, outputDirectory, writtenTextureCache, out var textureName))
            {
                textures[paramName] = textureName;
            }
        }

        PromoteCanonicalChannelAliases(textures);
        PromoteCanonicalByTextureName(parameters, textures, materialName, meshName);
    }

    // For each canonical channel (Diffuse/Normals/SpecularMasks/Emission), walk the priority-ordered
    // alias list and copy the first matching texture name onto the canonical key. We skip the channel
    // entirely if it's already directly populated (e.g. the material had a literal "Diffuse" parameter)
    // so an explicit name always beats an alias.
    private static void PromoteCanonicalChannelAliases(Dictionary<string, string> textures)
    {
        foreach (var (channel, aliases) in FortniteMaterialAliases.DefaultChannelAliases)
        {
            if (textures.ContainsKey(channel))
            {
                continue;
            }

            foreach (var alias in aliases)
            {
                if (textures.TryGetValue(alias, out var textureName))
                {
                    textures[channel] = textureName;
                    break;
                }
            }
        }
    }

    private static readonly (string Channel, System.Text.RegularExpressions.Regex Pattern)[] _canonicalTextureNamePatterns =
    [
        (FortniteMaterialAliases.DiffuseChannel,
            new System.Text.RegularExpressions.Regex(CMaterialParams2.RegexDiffuse,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled)),
        (FortniteMaterialAliases.NormalsChannel,
            new System.Text.RegularExpressions.Regex(CMaterialParams2.RegexNormals,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled)),
        (FortniteMaterialAliases.SpecularMasksChannel,
            new System.Text.RegularExpressions.Regex(CMaterialParams2.RegexSpecularMasks,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled)),
        (FortniteMaterialAliases.EmissionChannel,
            new System.Text.RegularExpressions.Regex(CMaterialParams2.RegexEmissive,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled)),
    ];

    // Automatic fallback: when a canonical channel still has no entry, look at every UTexture2D the
    // material references and pick the one whose **asset name** matches the channel's regex and best
    // overlaps with the mesh+material tokens. Fortnite naming is `T_<descriptor>_D/N/S/E`, so this
    // resolves correctly when the master material uses bespoke parameter names that the alias table
    // doesn't know about.
    //
    // We only fill MISSING canonical entries — never override an existing one. FModel does the same:
    // it samples `Diffuse[0]` which resolves to the literal `"Diffuse"` parameter via
    // `CMaterialParams2.Diffuse[0]`'s alias list, so once that's set we match FModel's pick. A
    // previous override-when-better-score implementation incorrectly swapped the rear-panel material
    // from `LAATFrontLaser` (its UE-assigned Diffuse) to `LAATRear` (Diffuse_Texture_2) which differs
    // from how FModel renders the same mesh.
    private static void PromoteCanonicalByTextureName(
        CMaterialParams2 parameters,
        Dictionary<string, string> textures,
        string materialName,
        string meshName)
    {
        // Build asset-name -> sidecar-filename map. The same texture may surface under several param
        // names; we want a single regex check per unique texture.
        var byAssetName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (paramName, value) in parameters.Textures)
        {
            if (value is UTexture2D texture &&
                !string.IsNullOrWhiteSpace(texture.Name) &&
                textures.TryGetValue(paramName, out var fileName))
            {
                byAssetName.TryAdd(texture.Name, fileName);
            }
        }

        if (byAssetName.Count == 0)
        {
            return;
        }

        var meshTokens = ExtractNameTokens(meshName);
        var materialTokens = ExtractNameTokens(materialName);

        foreach (var (channel, pattern) in _canonicalTextureNamePatterns)
        {
            if (textures.ContainsKey(channel))
            {
                continue;
            }

            string? bestFileName = null;
            int bestScore = int.MinValue;
            foreach (var (assetName, fileName) in byAssetName)
            {
                if (!pattern.IsMatch(assetName))
                {
                    continue;
                }

                // Skip textures already bound to another canonical channel — avoids `T_Body_S` from
                // simultaneously becoming Diffuse and SpecularMasks when both regexes loosely match.
                if (textures.Values.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Mesh tokens weighted higher than material tokens: the mesh's name is the strongest
                // signal for which texture belongs to *this* slot when many candidates match.
                var score = ScoreNameTokenOverlap(assetName, meshTokens) * 3 +
                    ScoreNameTokenOverlap(assetName, materialTokens);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestFileName = fileName;
                }
            }

            if (bestFileName is not null)
            {
                textures[channel] = bestFileName;
            }
        }
    }

    private static List<string> ExtractNameTokens(string name)
    {
        List<string> tokens = [];
        if (string.IsNullOrWhiteSpace(name))
        {
            return tokens;
        }

        foreach (var rawToken in name.Split(['_', ' ', '-', '.', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Strip common UE prefixes (M_, MI_, SM_, T_) without dropping useful tokens like "T1A".
            if (rawToken.Length <= 2 && (rawToken.Equals("M", StringComparison.OrdinalIgnoreCase) ||
                rawToken.Equals("MI", StringComparison.OrdinalIgnoreCase) ||
                rawToken.Equals("SM", StringComparison.OrdinalIgnoreCase) ||
                rawToken.Equals("SK", StringComparison.OrdinalIgnoreCase) ||
                rawToken.Equals("T", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            foreach (var camel in SplitCamelCase(rawToken))
            {
                if (camel.Length >= 3)
                {
                    tokens.Add(camel.ToLowerInvariant());
                }
            }
        }

        return tokens;
    }

    private static IEnumerable<string> SplitCamelCase(string token)
    {
        var start = 0;
        for (var i = 1; i < token.Length; i++)
        {
            if (char.IsUpper(token[i]) && (char.IsLower(token[i - 1]) || (i + 1 < token.Length && char.IsLower(token[i + 1]))))
            {
                yield return token[start..i];
                start = i;
            }
        }

        yield return token[start..];
    }

    private static int ScoreNameTokenOverlap(string textureAssetName, List<string> tokens)
    {
        if (tokens.Count == 0)
        {
            return 0;
        }

        var lowered = textureAssetName.ToLowerInvariant();
        var score = 0;
        foreach (var token in tokens)
        {
            if (lowered.Contains(token, StringComparison.Ordinal))
            {
                score += Math.Min(token.Length, 12);
            }
        }
        return score;
    }

    private static bool TryGetOrWriteTexture(
        UTexture2D texture,
        string outputDirectory,
        Dictionary<string, string> cache,
        out string textureName)
    {
        var cacheKey = texture.GetPathName();
        if (cache.TryGetValue(cacheKey, out var cached))
        {
            textureName = cached;
            return true;
        }

        try
        {
            if (texture.Decode(ETexturePlatform.DesktopMobile) is not { } bitmap)
            {
                textureName = string.Empty;
                return false;
            }

            var name = CreateTextureOverrideName(texture);
            var imageData = bitmap.Encode(ETextureFormat.Png, true, out var extension);
            File.WriteAllBytes(Path.Combine(outputDirectory, name + "." + extension), imageData);
            cache[cacheKey] = name;
            textureName = name;
            return true;
        }
        catch
        {
            // Missing optional texture bulk data should not prevent the mesh from previewing.
            textureName = string.Empty;
            return false;
        }
    }

    private static Dictionary<string, UnrealMaterialColorOverride> SelectMaterialColors(
        CMaterialParams2 parameters,
        string materialName)
    {
        Dictionary<string, UnrealMaterialColorOverride> colors = new(StringComparer.OrdinalIgnoreCase);
        var layerIndex = GetMaterialLayerIndex(materialName, CMaterialParams2.DiffuseColors.Length);
        AddMaterialColor(colors, "BaseColor", parameters, CMaterialParams2.DiffuseColors[layerIndex]);
        AddMaterialColor(colors, "EmissiveColor", parameters, CMaterialParams2.EmissiveColors[layerIndex]);
        return colors;
    }

    private static void AddMaterialColor(
        Dictionary<string, UnrealMaterialColorOverride> colors,
        string channel,
        CMaterialParams2 parameters,
        string[] names)
    {
        if (!parameters.TryGetLinearColor(out var color, names))
        {
            return;
        }

        colors[channel] = new UnrealMaterialColorOverride(color.R, color.G, color.B, color.A);
    }

    private static int GetMaterialLayerIndex(string materialName, int layerCount)
    {
        var normalized = NameHelpers.SanitizeMaterialName(materialName);
        var index = normalized.EndsWith("_b", StringComparison.OrdinalIgnoreCase) ? 1 :
            normalized.EndsWith("_c", StringComparison.OrdinalIgnoreCase) ? 2 :
            normalized.EndsWith("_d", StringComparison.OrdinalIgnoreCase) ? 3 :
            normalized.EndsWith("_e", StringComparison.OrdinalIgnoreCase) ? 4 :
            normalized.EndsWith("_f", StringComparison.OrdinalIgnoreCase) ? 5 :
            normalized.EndsWith("_g", StringComparison.OrdinalIgnoreCase) ? 6 :
            normalized.EndsWith("_h", StringComparison.OrdinalIgnoreCase) ? 7 : 0;
        return Math.Clamp(index, 0, Math.Max(0, layerCount - 1));
    }

    private static void AddTextureReference(
        Dictionary<string, string> textures,
        string channel,
        FPackageIndex textureIndex,
        string outputDirectory)
    {
        if (textureIndex.IsNull ||
            !TryLoadPackageIndex(textureIndex, out var export) ||
            export is not UTexture2D texture)
        {
            return;
        }

        try
        {
            AddTextureReference(textures, channel, texture, outputDirectory);
        }
        catch
        {
            // Missing optional texture bulk data should not prevent the mesh from previewing.
        }
    }

    private static void AddTextureReference(
        Dictionary<string, string> textures,
        string channel,
        UTexture2D texture,
        string outputDirectory)
    {
        try
        {
            if (texture.Decode(ETexturePlatform.DesktopMobile) is not { } bitmap)
            {
                return;
            }

            var textureName = CreateTextureOverrideName(texture);
            var imageData = bitmap.Encode(ETextureFormat.Png, true, out var extension);
            File.WriteAllBytes(Path.Combine(outputDirectory, textureName + "." + extension), imageData);
            textures[channel] = textureName;
        }
        catch
        {
            // Missing optional texture bulk data should not prevent the mesh from previewing.
        }
    }

    private static string CreateTextureOverrideName(UTexture texture)
    {
        var path = texture.GetPathName();
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path)))[..8];
        return $"{NameHelpers.SanitizeMaterialName(texture.Name)}_{hash.ToLowerInvariant()}";
    }

    private static string GetLocalMaterialSidecarPath(string directory, string materialName)
    {
        return Path.Combine(directory, NameHelpers.SanitizeMaterialName(materialName) + ".json");
    }

    public void ClearCaches()
    {
        lock (_cacheLock)
        {
            _providerCache.Clear();
        }
    }

    private IEnumerable<ExplorerFileEntry> EnumerateMeshEntries(DefaultFileProvider provider, string root, GameFile file)
    {
        if (!provider.TryLoadPackage(file, out var package))
        {
            yield break;
        }

        for (var exportIndex = 0; exportIndex < package.ExportMapLength; exportIndex++)
        {
            var pointer = new FPackageIndex(package, exportIndex + 1).ResolvedObject;
            if (pointer?.Object is null)
            {
                continue;
            }

            var dummy = ((AbstractUePackage)package).ConstructObject(pointer.Class, package);
            var assetClass = dummy switch
            {
                USkeletalMesh => "SkeletalMesh",
                UStaticMesh => "StaticMesh",
                _ => null
            };
            if (assetClass is null || pointer.Object.Value is not UObject export)
            {
                continue;
            }

            var objectPath = export.GetPathName();
            var displayPath = $"{file.Path}/{assetClass}/{SanitizeObjectPath(export.Name)}";
            var details = $"Class {assetClass} | Object {objectPath} | Package {file.Path}";

            yield return new ExplorerFileEntry(
                displayPath,
                objectPath,
                "psk",
                root,
                root,
                root,
                objectPath,
                Id,
                details,
                AssetClass: assetClass);
        }
    }

    private List<ExplorerFileEntry> EnumerateAssetRegistryEntries(
        Cue4ParseProviderContext providerContext,
        out AssetRegistryScanStats stats)
    {
        var scanStats = new AssetRegistryScanStats();
        stats = scanStats;
        List<ExplorerFileEntry> entries = [];
        var provider = providerContext.Provider;

        foreach (var (providerPath, registryFile) in provider.Files.Where(IsAssetRegistryFile))
        {
            scanStats.FoundCount++;
            var reader = registryFile.SafeCreateReader();
            if (reader is null)
            {
                scanStats.UnreadableCount++;
                continue;
            }

            FAssetRegistryState registry;
            try
            {
                registry = new FAssetRegistryState(reader);
                scanStats.ReadableCount++;
            }
            catch
            {
                scanStats.ParseFailureCount++;
                continue;
            }

            foreach (var asset in registry.PreallocatedAssetDataBuffers)
            {
                var registryClass = asset.AssetClass.Text;
                if (!providerContext.Profile.TryGetExplorerAssetClass(registryClass, out var assetClass) &&
                    !IsAnimationRegistryAsset(asset))
                {
                    continue;
                }

                var objectPath = asset.ObjectPath;
                var displayPath = $"{asset.PackageName.Text}/{assetClass}/{SanitizeObjectPath(asset.AssetName.Text)}";
                var isAnimation = IsAnimationAssetClass(assetClass);
                var isExportableAnimation = isAnimation && IsExportableAnimationClass(assetClass);
                var details = isAnimation && !isExportableAnimation
                    ? $"Class {assetClass} | Object {objectPath} | Registry {providerPath} | Browse only"
                    : $"Class {assetClass} | Object {objectPath} | Registry {providerPath}";
                scanStats.SupportedAssetCount++;

                entries.Add(new ExplorerFileEntry(
                    displayPath,
                    objectPath,
                    isAnimation ? "ueanim" : "psk",
                    providerContext.ArchiveDirectory,
                    providerContext.ArchiveDirectory,
                    providerContext.ArchiveDirectory,
                    objectPath,
                    Id,
                    details,
                    IsConvertible: !isAnimation,
                    AssetClass: assetClass));
            }
        }

        return entries;
    }

    private static bool IsAnimationAssetClass(string assetClass)
    {
        return assetClass.Contains("Anim", StringComparison.OrdinalIgnoreCase) ||
            assetClass.Equals("BlendSpace", StringComparison.OrdinalIgnoreCase) ||
            assetClass.Equals("BlendSpace1D", StringComparison.OrdinalIgnoreCase) ||
            assetClass.Equals("PoseAsset", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExportableAnimationClass(string assetClass)
    {
        return assetClass.Equals("AnimSequence", StringComparison.OrdinalIgnoreCase) ||
            assetClass.Equals("AnimMontage", StringComparison.OrdinalIgnoreCase) ||
            assetClass.Equals("AnimComposite", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAnimationRegistryAsset(FAssetData asset)
    {
        return IsAnimationAssetClass(asset.AssetClass.Text) ||
            asset.PackagePath.Text.Contains("/Anim", StringComparison.OrdinalIgnoreCase) ||
            asset.PackageName.Text.Contains("/Anim", StringComparison.OrdinalIgnoreCase) ||
            asset.AssetName.Text.Contains("Anim", StringComparison.OrdinalIgnoreCase);
    }

    private Cue4ParseProviderContext GetProvider(string root)
    {
        var cacheKey = ProviderCacheKey(root);
        lock (_cacheLock)
        {
            if (_providerCache.TryGetValue(cacheKey, out var provider))
            {
                return provider;
            }
        }

        var loadedProvider = Cue4ParseProviderFactory.Create(root);
        lock (_cacheLock)
        {
            _providerCache[cacheKey] = loadedProvider;
        }

        return loadedProvider;
    }

    private static bool IsPackageFile(GameFile file)
    {
        return file.Path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) ||
            file.Path.EndsWith(".umap", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAssetRegistryFile(KeyValuePair<string, GameFile> file)
    {
        return IsAssetRegistryPath(file.Key) || IsAssetRegistryPath(file.Value.Path);
    }

    private static bool IsAssetRegistryPath(string path)
    {
        return path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) &&
            path.Contains("AssetRegistry", StringComparison.OrdinalIgnoreCase) &&
            !path.Contains("Editor", StringComparison.OrdinalIgnoreCase);
    }

    private static ExporterOptions CreateExporterOptions(bool exportMaterials = true)
    {
        return new ExporterOptions
        {
            LodFormat = ELodFormat.FirstLod,
            MeshFormat = EMeshFormat.ActorX,
            AnimFormat = EAnimFormat.ActorX,
            MaterialFormat = EMaterialFormat.AllLayers,
            TextureFormat = ETextureFormat.Png,
            CompressionFormat = EFileCompressionFormat.None,
            Platform = ETexturePlatform.DesktopMobile,
            SocketFormat = ESocketFormat.Bone,
            ExportMorphTargets = true,
            ExportMaterials = exportMaterials
        };
    }

    private static ResolvedUnrealMesh[] ResolveMeshExports(UObject export, DefaultFileProvider provider)
    {
        List<ResolvedUnrealMesh> meshes = [];
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        CollectMeshExports(export, provider, visited, 0, meshes);
        return NormalizeResolvedMeshes(meshes);
    }

    private static ResolvedUnrealMesh[] NormalizeResolvedMeshes(IEnumerable<ResolvedUnrealMesh> meshes)
    {
        var distinctMeshes = meshes
            .DistinctBy(GetResolvedMeshKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var transformFilteredMeshes = RemoveNearDuplicateTransformMeshes(distinctMeshes);
        var placementFilteredMeshes = RemoveOriginDuplicateMeshes(transformFilteredMeshes);
        var nonPlaceholderMeshes = placementFilteredMeshes
            .Where(mesh => !IsPlaceholderMesh(mesh.Export))
            .ToArray();

        return nonPlaceholderMeshes.Length > 0 ? nonPlaceholderMeshes : placementFilteredMeshes;
    }

    private static ResolvedUnrealMesh[] RemoveNearDuplicateTransformMeshes(IReadOnlyList<ResolvedUnrealMesh> meshes)
    {
        List<ResolvedUnrealMesh> filteredMeshes = [];
        var removedAny = false;
        foreach (var group in meshes.GroupBy(GetResolvedMeshTransformPlacementKey, StringComparer.OrdinalIgnoreCase))
        {
            var groupMeshes = group.ToArray();
            if (groupMeshes.Length <= 1)
            {
                filteredMeshes.AddRange(groupMeshes);
                continue;
            }

            List<List<ResolvedUnrealMesh>> rotationGroups = [];
            foreach (var mesh in groupMeshes)
            {
                var rotationGroup = rotationGroups.FirstOrDefault(candidate => AreNearEquivalentRotations(candidate[0].Transform.Rotation, mesh.Transform.Rotation));
                if (rotationGroup is null)
                {
                    rotationGroups.Add([mesh]);
                }
                else
                {
                    rotationGroup.Add(mesh);
                    removedAny = true;
                }
            }

            foreach (var rotationGroup in rotationGroups)
            {
                filteredMeshes.Add(SelectBestTransformDuplicate(rotationGroup));
            }
        }

        return removedAny ? [.. filteredMeshes] : [.. meshes];
    }

    private static ResolvedUnrealMesh[] RemoveOriginDuplicateMeshes(IReadOnlyList<ResolvedUnrealMesh> meshes)
    {
        HashSet<ResolvedUnrealMesh> remove = [];
        foreach (var group in meshes.GroupBy(GetResolvedMeshPlacementKey, StringComparer.OrdinalIgnoreCase))
        {
            var groupMeshes = group.ToArray();
            if (groupMeshes.Length <= 1)
            {
                continue;
            }

            foreach (var originMesh in groupMeshes.Where(mesh => mesh.Transform.IsAtOrigin()))
            {
                if (groupMeshes.Any(mesh => !ReferenceEquals(mesh, originMesh) && !mesh.Transform.IsAtOrigin()))
                {
                    remove.Add(originMesh);
                }
            }
        }

        return remove.Count == 0
            ? [.. meshes]
            : [.. meshes.Where(mesh => !remove.Contains(mesh))];
    }

    private static string GetResolvedMeshKey(ResolvedUnrealMesh mesh)
    {
        var materialKey = mesh.OverrideMaterials is { Length: > 0 }
            ? string.Join(',', mesh.OverrideMaterials.Select(material => material.Index.ToString(CultureInfo.InvariantCulture)))
            : string.Empty;
        return $"{GetExportVisitKey(mesh.Export)}|{mesh.Transform.DedupKey()}|{materialKey}";
    }

    private static string GetResolvedMeshTransformPlacementKey(ResolvedUnrealMesh mesh)
    {
        var materialKey = mesh.OverrideMaterials is { Length: > 0 }
            ? string.Join(',', mesh.OverrideMaterials.Select(material => material.Index.ToString(CultureInfo.InvariantCulture)))
            : string.Empty;
        return $"{GetExportVisitKey(mesh.Export)}|{mesh.Transform.Translation.DedupKey()}|{mesh.Transform.Scale.DedupKey()}|{materialKey}";
    }

    private static string GetResolvedMeshPlacementKey(ResolvedUnrealMesh mesh)
    {
        var materialKey = mesh.OverrideMaterials is { Length: > 0 }
            ? string.Join(',', mesh.OverrideMaterials.Select(material => material.Index.ToString(CultureInfo.InvariantCulture)))
            : string.Empty;
        return $"{GetExportVisitKey(mesh.Export)}|{mesh.Transform.Scale.DedupKey()}|{materialKey}";
    }

    private static bool AreNearEquivalentRotations(UnrealSceneQuaternion a, UnrealSceneQuaternion b)
    {
        return MathF.Abs(a.Dot(b)) > 0.995f;
    }

    private static ResolvedUnrealMesh SelectBestTransformDuplicate(IReadOnlyList<ResolvedUnrealMesh> meshes)
    {
        return meshes
            .OrderBy(mesh => mesh.Transform.Rotation.NormalizationError())
            .ThenByDescending(mesh => MathF.Abs(mesh.Transform.Rotation.W))
            .First();
    }

    private static bool IsPlaceholderMesh(UObject export)
    {
        var exportKey = $"{export.ExportType}:{export.Name}:{GetExportVisitKey(export)}";
        return _placeholderMeshPathTerms.Any(term => exportKey.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static void CollectMeshExports(
        UObject export,
        DefaultFileProvider provider,
        HashSet<string> visited,
        int depth,
        List<ResolvedUnrealMesh> meshes)
    {
        if (export is USkeletalMesh or UStaticMesh)
        {
            meshes.Add(new ResolvedUnrealMesh(export, null, UnrealSceneTransform.Identity, CollectionSource: "direct-mesh"));
            return;
        }

        // Skip component templates that the SCS walk already collected (they marked themselves in
        // visited). Without this, property-walking the BP later re-adds the same component template
        // via the single-arg CollectStaticMeshComponent path, which returns just the template's own
        // RelativeTransform with no SCS parent composition — producing ghost duplicates.
        if (export is UStaticMeshComponent staticMeshComponent)
        {
            if (!visited.Contains(GetExportVisitKey(export)))
            {
                CollectStaticMeshComponent(staticMeshComponent, meshes);
            }
            return;
        }

        if (export is USkeletalMeshComponent skeletalMeshComponent)
        {
            if (!visited.Contains(GetExportVisitKey(export)))
            {
                CollectSkeletalMeshComponent(skeletalMeshComponent, meshes);
            }
            return;
        }

        if (depth >= _maxResolveDepth || visited.Count >= _maxResolveVisitedCount || !visited.Add(GetExportVisitKey(export)))
        {
            return;
        }

        if (export is ULevelSaveRecord levelSaveRecord)
        {
            CollectLevelSaveRecord(levelSaveRecord, provider, visited, depth + 1, meshes);
        }

        if (export is UBlueprintGeneratedClass blueprintGeneratedClass)
        {
            CollectBlueprintGeneratedClass(blueprintGeneratedClass, provider, visited, depth + 1, meshes);
        }

        foreach (var propertyName in _meshPropertyNames)
        {
            if (export.Properties.FirstOrDefault(property => property.Name.Text.Equals(propertyName, StringComparison.OrdinalIgnoreCase)) is { } property)
            {
                CollectMeshExportsFromValue(property.Tag?.GenericValue, provider, visited, depth + 1, meshes);
            }
        }

        foreach (var property in export.Properties)
        {
            CollectMeshExportsFromValue(property.Tag?.GenericValue, provider, visited, depth + 1, meshes);
        }
    }

    private static void CollectLevelSaveRecord(
        ULevelSaveRecord levelSaveRecord,
        DefaultFileProvider provider,
        HashSet<string> visited,
        int depth,
        List<ResolvedUnrealMesh> meshes)
    {
        var firstLevelMeshIndex = meshes.Count;
        var templateRecords = CreateTemplateRecordEntries(levelSaveRecord).ToArray();
        if (templateRecords.Length > 0)
        {
            var templatesById = templateRecords
                .SelectMany(template => template.GetActorInstanceLookupKeys()
                    .Select(key => (Key: key, Template: template)))
                .GroupBy(item => item.Key)
                .ToDictionary(group => group.Key, group => group.First().Template);
            var actorInstances = GetActorInstanceRecords(levelSaveRecord).ToArray();

            if (actorInstances.Length > 0)
            {
                foreach (var actorInstance in actorInstances)
                {
                    if (!templatesById.TryGetValue(actorInstance.TemplateRecordID, out var template))
                    {
                        continue;
                    }

                    var actorTransform = UnrealSceneTransform.From(actorInstance.Transform);
                    DumpActorInstanceTransform(template, actorInstance, actorTransform);
                    CollectTemplateRecordMeshes(
                        levelSaveRecord,
                        template,
                        actorTransform,
                        provider,
                        visited,
                        depth,
                        meshes);
                }
            }
            else
            {
                foreach (var template in templateRecords)
                {
                    CollectTemplateRecordMeshes(
                        levelSaveRecord,
                        template,
                        UnrealSceneTransform.Identity,
                        provider,
                        visited,
                        depth,
                        meshes);
                }
            }
        }

        if (meshes.Count > firstLevelMeshIndex)
        {
            return;
        }

        foreach (var actorData in levelSaveRecord.ActorData ?? [])
        {
            CollectMeshExportsFromValue(actorData, provider, visited, depth + 1, meshes);
        }
    }

    private static IEnumerable<LevelSaveTemplateRecord> CreateTemplateRecordEntries(ULevelSaveRecord levelSaveRecord)
    {
        if (levelSaveRecord.TemplateRecords is null)
        {
            yield break;
        }

        var actorDataIndex = 0;
        foreach (var (templateKey, templateRecord) in levelSaveRecord.TemplateRecords)
        {
            yield return new LevelSaveTemplateRecord(templateKey, actorDataIndex, templateRecord);
            actorDataIndex++;
        }
    }

    private static IEnumerable<FActorInstanceRecord> GetActorInstanceRecords(ULevelSaveRecord levelSaveRecord)
    {
        if (levelSaveRecord.ActorInstanceRecords is not null)
        {
            foreach (var actorInstance in levelSaveRecord.ActorInstanceRecords.Values)
            {
                yield return actorInstance;
            }
        }

        if (levelSaveRecord.VolumeInfoActorRecords is not null)
        {
            foreach (var actorInstance in levelSaveRecord.VolumeInfoActorRecords.Values)
            {
                yield return actorInstance;
            }
        }
    }

    private static void CollectTemplateRecordMeshes(
        ULevelSaveRecord levelSaveRecord,
        LevelSaveTemplateRecord template,
        UnrealSceneTransform actorTransform,
        DefaultFileProvider provider,
        HashSet<string> visited,
        int depth,
        List<ResolvedUnrealMesh> meshes)
    {
        if (!TryLoadSoftObjectPath(template.Record.ActorClass, provider, out var actorClassExport) ||
            actorClassExport is not UBlueprintGeneratedClass blueprintGeneratedClass)
        {
            return;
        }

        var firstMeshIndex = meshes.Count;
        var localVisited = new HashSet<string>(visited, StringComparer.OrdinalIgnoreCase);
        CollectMeshExports(blueprintGeneratedClass, provider, localVisited, depth + 1, meshes);
        if (meshes.Count <= firstMeshIndex)
        {
            return;
        }

        for (var meshIndex = firstMeshIndex; meshIndex < meshes.Count; meshIndex++)
        {
            meshes[meshIndex] = meshes[meshIndex].WithParentTransform(actorTransform);
        }

        try
        {
            if (TryCollectTextureData(levelSaveRecord, template.ActorDataIndex, template.Record, out var textureData))
            {
                meshes[firstMeshIndex] = meshes[firstMeshIndex].WithTextureData(textureData);
            }
        }
        catch
        {
            // TextureData is optional for preview; keep the mesh even when Fortnite actor metadata is partial.
        }
    }

    private static bool TryCollectTextureData(
        ULevelSaveRecord levelSaveRecord,
        int actorDataIndex,
        FActorTemplateRecord templateRecord,
        out Dictionary<int, UBuildingTextureData> textureData)
    {
        textureData = [];
        if (levelSaveRecord.ActorData is null ||
            actorDataIndex < 0 ||
            actorDataIndex >= levelSaveRecord.ActorData.Count)
        {
            return false;
        }

        foreach (var property in levelSaveRecord.ActorData[actorDataIndex].Properties ?? [])
        {
            if (!property.Name.Text.Equals("TextureData", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            UBuildingTextureData? data = null;
            if (templateRecord.bUsingRecordDataReferenceTable)
            {
                if (property.Tag?.GetValue<FPackageIndex>() is { } packageIndex &&
                    packageIndex.Index >= 0 &&
                    templateRecord.ActorDataReferenceTable is not null &&
                    packageIndex.Index < templateRecord.ActorDataReferenceTable.Length)
                {
                    try
                    {
                        data = templateRecord.ActorDataReferenceTable[packageIndex.Index].Load<UBuildingTextureData>();
                    }
                    catch
                    {
                        data = null;
                    }
                }
            }
            else if (property.Tag?.GetValue<FPackageIndex>() is { } textureDataIndex)
            {
                data = TryLoadPackageIndex(textureDataIndex, out var textureDataExport)
                    ? textureDataExport as UBuildingTextureData
                    : null;
            }

            if (data is not null)
            {
                textureData[property.ArrayIndex] = data;
            }
        }

        return textureData.Count > 0;
    }

    private static void CollectBlueprintGeneratedClass(
        UBlueprintGeneratedClass blueprintGeneratedClass,
        DefaultFileProvider provider,
        HashSet<string> visited,
        int depth,
        List<ResolvedUnrealMesh> meshes)
    {
        DumpBlueprintFunctions(blueprintGeneratedClass);
        var firstMeshIndex = meshes.Count;
        if (blueprintGeneratedClass.TryGetValue(out UObject constructionScript, "SimpleConstructionScript"))
        {
            CollectConstructionScript(constructionScript, provider, visited, depth + 1, meshes);
        }

        TagPathSegment(meshes, firstMeshIndex, $"bp:{blueprintGeneratedClass.Name}");

        if (meshes.Count > firstMeshIndex)
        {
            return;
        }

        var ichStart = meshes.Count;
        if (blueprintGeneratedClass.TryGetValue(out UObject inheritableComponentHandler, "InheritableComponentHandler"))
        {
            CollectInheritableComponentHandler(inheritableComponentHandler, provider, visited, depth + 1, meshes);
        }
        TagPathSegment(meshes, ichStart, $"ich:{blueprintGeneratedClass.Name}");

        var cdoStart = meshes.Count;
        if (blueprintGeneratedClass.ClassDefaultObject is { IsNull: false } &&
            TryLoadPackageIndex(blueprintGeneratedClass.ClassDefaultObject, out var classDefaultObject) &&
            classDefaultObject is not null)
        {
            CollectMeshExports(classDefaultObject, provider, visited, depth + 1, meshes);
        }
        TagPathSegment(meshes, cdoStart, $"cdo:{blueprintGeneratedClass.Name}");

        var superStart = meshes.Count;
        if (blueprintGeneratedClass.SuperStruct is { IsNull: false } &&
            TryLoadPackageIndex(blueprintGeneratedClass.SuperStruct, out var superStruct) &&
            superStruct is UBlueprintGeneratedClass superBlueprint)
        {
            CollectMeshExports(superBlueprint, provider, visited, depth + 1, meshes);
        }
        TagPathSegment(meshes, superStart, $"super:{blueprintGeneratedClass.Name}");
    }

    // Prefix every mesh added during a specific batch with a descriptive tag so the part-summary
    // log shows the originating code path. WithSource is no-op when CollectionSource was already
    // set by an inner call, so nested batches show the innermost path.
    private static void TagPathSegment(List<ResolvedUnrealMesh> meshes, int startIndex, string source)
    {
        for (var i = startIndex; i < meshes.Count; i++)
        {
            meshes[i] = meshes[i].WithSource(source);
        }
    }

    // Dumps every UFunction's compiled Kismet bytecode from a BlueprintGeneratedClass so we can
    // diagnose runtime placement logic that the SCS hierarchy alone doesn't capture. UE blueprints
    // can call SetRelativeLocation / AttachToComponent / etc. inside their Construction Script
    // at spawn time — and that's where vehicle-style assets sometimes do their "final" component
    // placement that's missing from the BP's authored SCS positions.
    private static void DumpBlueprintFunctions(UBlueprintGeneratedClass bp)
    {
        try
        {
            var dumpDir = Path.Combine(Path.GetTempPath(), "GMConverter.UI", "BpFunctions");
            Directory.CreateDirectory(dumpDir);
            var dumpPath = Path.Combine(dumpDir, $"{NameHelpers.SanitizeFileName(bp.Name)}.bp.json");
            var serializer = NewtonsoftJson.JsonSerializer.Create(new NewtonsoftJson.JsonSerializerSettings
            {
                Formatting = NewtonsoftJson.Formatting.Indented,
                ReferenceLoopHandling = NewtonsoftJson.ReferenceLoopHandling.Ignore,
                NullValueHandling = NewtonsoftJson.NullValueHandling.Ignore,
            });
            using var writer = new StreamWriter(dumpPath);
            using var jw = new NewtonsoftJson.JsonTextWriter(writer) { Formatting = NewtonsoftJson.Formatting.Indented };
            jw.WriteStartObject();
            jw.WritePropertyName("bp");
            jw.WriteValue(bp.Name);
            jw.WritePropertyName("class");
            jw.WriteValue(bp.GetType().Name);
            jw.WritePropertyName("superStruct");
            jw.WriteValue(bp.SuperStruct?.ResolvedObject?.Name.Text ?? bp.SuperStruct?.Name ?? "<null>");
            jw.WritePropertyName("childrenCount");
            jw.WriteValue(bp.Children?.Length ?? 0);
            jw.WritePropertyName("rawChildren");
            jw.WriteStartArray();
            foreach (var childIndex in bp.Children ?? [])
            {
                jw.WriteStartObject();
                jw.WritePropertyName("index");
                jw.WriteValue(childIndex.Index);
                jw.WritePropertyName("name");
                jw.WriteValue(childIndex.Name ?? "<null>");
                jw.WritePropertyName("resolved");
                jw.WriteValue(childIndex.ResolvedObject?.Name.Text ?? "<unresolved>");
                jw.WritePropertyName("resolvedClass");
                jw.WriteValue(childIndex.ResolvedObject?.Class?.Name.Text ?? "<no-class>");
                var loaded = TryLoadPackageIndex(childIndex, out var loadedChild);
                jw.WritePropertyName("loadedOk");
                jw.WriteValue(loaded);
                jw.WritePropertyName("loadedType");
                jw.WriteValue(loadedChild?.GetType().Name ?? "<null>");
                if (loadedChild is UFunction func)
                {
                    jw.WritePropertyName("isUFunction");
                    jw.WriteValue(true);
                    jw.WritePropertyName("bytecodeCount");
                    jw.WriteValue(func.ScriptBytecode?.Length ?? 0);
                    if (func.ScriptBytecode is { Length: > 0 })
                    {
                        jw.WritePropertyName("bytecode");
                        serializer.Serialize(jw, func.ScriptBytecode);
                    }
                }
                jw.WriteEndObject();
            }
            jw.WriteEndArray();
            jw.WriteEndObject();
        }
        catch
        {
            // Diagnostics must never break export.
        }
    }

    // Appends a line per actor instance to a level-record actor-transform log so we can see the
    // raw (Translation, Rotation, Scale) of every actor we extract — useful to rule out the
    // hypothesis that the visible-but-not-in-SCS positioning lives at the actor placement level.
    private static void DumpActorInstanceTransform(
        LevelSaveTemplateRecord template,
        FActorInstanceRecord actorInstance,
        UnrealSceneTransform composed)
    {
        try
        {
            var dumpDir = Path.Combine(Path.GetTempPath(), "GMConverter.UI", "ActorInstances");
            Directory.CreateDirectory(dumpDir);
            var dumpPath = Path.Combine(dumpDir, "actor_instances.log");
            var actorClassName = template.Record.ActorClass.AssetPathName.Text;
            var line = $"actor={actorClassName}\tinstance={composed.ToDiagnosticString()}\n";
            File.AppendAllText(dumpPath, line);
        }
        catch
        {
            // Diagnostics must never break export.
        }
    }

    // Dumps an SCS hierarchy to a flat log so we can diagnose mesh-positioning issues. For each
    // SCS node we record the variable name (UE's component identifier), the component class,
    // the raw RelativeLocation/Rotation/Scale on the component template, and any socket / parent-
    // variable attachment data. Sockets and parent-variable references hold positioning UE applies
    // at runtime; if the asset uses them, the components' literal RelativeTransform may be identity
    // while the rendered positioning lives in the socket transform of the parent mesh.
    private static void DumpScsHierarchy(USimpleConstructionScript scs)
    {
        try
        {
            var dumpDir = Path.Combine(Path.GetTempPath(), "GMConverter.UI", "Scs");
            Directory.CreateDirectory(dumpDir);
            var dumpPath = Path.Combine(dumpDir, $"{NameHelpers.SanitizeFileName(scs.Name)}.scs.log");
            var sb = new System.Text.StringBuilder();
            sb.Append("scsName=").AppendLine(scs.Name);
            foreach (var root in scs.GetRootNodes())
            {
                DumpScsNodeRecursive(root, parentVariableName: "<root>", depth: 0, sb);
            }
            File.WriteAllText(dumpPath, sb.ToString());
        }
        catch
        {
            // Diagnostics must never break export.
        }
    }

    private static void DumpScsNodeRecursive(USCS_Node node, string parentVariableName, int depth, System.Text.StringBuilder sb)
    {
        var indent = new string(' ', depth * 2);
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        // SCS nodes carry an InternalVariableName property naming the variable in the BP — that's
        // what other nodes use to reference this component for parent-variable attachment.
        var variableName = node.GetOrDefault<FName>("InternalVariableName").Text ?? string.Empty;
        var template = node.GetComponentTemplate();
        var componentClass = template?.Class?.Name ?? "<no-template>";
        var componentName = template?.Name ?? "<unnamed>";

        // Attachment info: ParentComponentOrVariableName (variable name of the SCS parent) and
        // AttachToName (socket on that parent). When AttachToName is set, the component's
        // RelativeTransform is interpreted in socket-local space, not BP-component space.
        var parentVar = node.GetOrDefault<FName>("ParentComponentOrVariableName").Text ?? string.Empty;
        var socket = node.GetOrDefault<FName>("AttachToName").Text ?? string.Empty;

        sb.Append(indent).Append('[').Append(depth.ToString(inv)).Append("] var=")
            .Append(variableName.Length == 0 ? "<unnamed>" : variableName)
            .Append(" parent=").Append(parentVariableName)
            .Append(" parentVar=").Append(parentVar.Length == 0 ? "<none>" : parentVar)
            .Append(" socket=").Append(socket.Length == 0 ? "<none>" : socket)
            .Append(" class=").Append(componentClass)
            .Append(" component=").AppendLine(componentName);

        if (template is USceneComponent sceneComponent)
        {
            var relLoc = sceneComponent.GetOrDefault<FVector>("RelativeLocation");
            var relRot = sceneComponent.GetOrDefault<FRotator>("RelativeRotation");
            var relScale = sceneComponent.GetOrDefault<FVector>("RelativeScale3D");
            var attachParent = sceneComponent.GetAttachParent();
            sb.Append(indent).Append("    relLoc=(").Append(relLoc.X.ToString("0.###", inv)).Append(',')
                .Append(relLoc.Y.ToString("0.###", inv)).Append(',').Append(relLoc.Z.ToString("0.###", inv))
                .Append(") relRot=(").Append(relRot.Pitch.ToString("0.###", inv)).Append(',')
                .Append(relRot.Yaw.ToString("0.###", inv)).Append(',').Append(relRot.Roll.ToString("0.###", inv))
                .Append(") relScale=(").Append(relScale.X.ToString("0.###", inv)).Append(',')
                .Append(relScale.Y.ToString("0.###", inv)).Append(',').Append(relScale.Z.ToString("0.###", inv))
                .Append(") attachParent=").AppendLine(attachParent is null ? "<none>" : attachParent.Name);

            if (template is UStaticMeshComponent staticMesh)
            {
                try
                {
                    var meshIndex = staticMesh.GetStaticMesh();
                    if (!meshIndex.IsNull && TryLoadPackageIndex(meshIndex, out var meshExport) && meshExport is UStaticMesh)
                    {
                        sb.Append(indent).Append("    mesh=").AppendLine(meshExport.Name);
                    }
                }
                catch
                {
                    // skip mesh introspection if it fails
                }
            }
        }

        foreach (var child in node.GetChildNodes())
        {
            DumpScsNodeRecursive(child, variableName.Length == 0 ? componentName : variableName, depth + 1, sb);
        }
    }

    private static void CollectConstructionScript(
        UObject constructionScript,
        DefaultFileProvider provider,
        HashSet<string> visited,
        int depth,
        List<ResolvedUnrealMesh> meshes)
    {
        var firstMeshIndex = meshes.Count;
        if (constructionScript is USimpleConstructionScript simpleConstructionScript)
        {
            try
            {
                DumpScsHierarchy(simpleConstructionScript);
                foreach (var rootNode in simpleConstructionScript.GetRootNodes())
                {
                    CollectScsNode(rootNode, UnrealSceneTransform.Identity, provider, visited, depth + 1, meshes);
                }
            }
            catch
            {
                // Cooked construction scripts can be sparse; fallback property traversal below still covers common cases.
            }
        }

        if (meshes.Count > firstMeshIndex)
        {
            return;
        }

        CollectMeshExportsFromValue(constructionScript.GetOrDefault<UObject[]>("AllNodes", []), provider, visited, depth + 1, meshes);
        if (constructionScript is USimpleConstructionScript fallbackConstructionScript)
        {
            try
            {
                CollectMeshExportsFromValue(fallbackConstructionScript.GetAllNodesRecursive(), provider, visited, depth + 1, meshes);
            }
            catch
            {
                // Cooked construction scripts can be sparse; fallback property traversal below still covers common cases.
            }
        }

        CollectMeshExports(constructionScript, provider, visited, depth + 1, meshes);
    }

    private static void CollectScsNode(
        USCS_Node node,
        UnrealSceneTransform parentTransform,
        DefaultFileProvider provider,
        HashSet<string> visited,
        int depth,
        List<ResolvedUnrealMesh> meshes)
    {
        if (depth >= _maxResolveDepth || visited.Count >= _maxResolveVisitedCount)
        {
            return;
        }

        var componentTransform = parentTransform;
        if (node.GetComponentTemplate() is { } component)
        {
            componentTransform = GetScsComponentTransform(component, parentTransform);
            // Empirical Fortnite-asset Y-axis correction: SCS root-level components consistently
            // import on the wrong side of the actor pivot vs. their in-game placement. The bytecode
            // that would (hypothetically) move them at spawn time is stripped from the cooked
            // .uasset, and no actor-instance rotation exists in the PPID, so we can't derive the
            // right transform from the asset itself. Empirically, mirroring the component across
            // the XZ plane (negating Translation.Y + the rotation's X/Z imaginary parts to keep
            // it a valid rotation under the mirror) lands every flagged part — WingBalls,
            // SideBallPivot, RearDoor — where the in-game LAAT shows it, AND swaps Side_L's
            // Yaw=+5° to Yaw=-5° so the side-door hinge faces the right way. Children inherit
            // through normal composition since we mutate the transform before recursing. Components
            // under _Static are unaffected because _Static's own translation.Y is 0 and its
            // rotation is identity, so the mirror is a no-op for them.
            if (parentTransform.IsIdentity())
            {
                componentTransform = componentTransform with
                {
                    Translation = componentTransform.Translation with
                    {
                        Y = -componentTransform.Translation.Y,
                    },
                    Rotation = componentTransform.Rotation with
                    {
                        X = -componentTransform.Rotation.X,
                        Z = -componentTransform.Rotation.Z,
                    },
                };
            }
            // Mark the component template as visited so a later property-walk in CollectMeshExports
            // doesn't re-add it through the single-arg CollectStaticMeshComponent path — which
            // returns the template's lone RelativeTransform without composing the SCS parent chain
            // and produces ghost duplicates floating at wrong positions.
            visited.Add(GetExportVisitKey(component));
            switch (component)
            {
                case UStaticMeshComponent staticMeshComponent:
                    CollectStaticMeshComponent(staticMeshComponent, componentTransform, meshes, source: "scs-static");
                    break;
                case USkeletalMeshComponent skeletalMeshComponent:
                    CollectSkeletalMeshComponent(skeletalMeshComponent, componentTransform, meshes, source: "scs-skeletal");
                    break;
            }
        }

        foreach (var childNode in node.GetChildNodes())
        {
            CollectScsNode(childNode, componentTransform, provider, visited, depth + 1, meshes);
        }
    }

    private static UnrealSceneTransform GetScsComponentTransform(USceneComponent component, UnrealSceneTransform parentTransform)
    {
        try
        {
            if (component.GetAttachParent() is not null)
            {
                return UnrealSceneTransform.From(component.GetAbsoluteTransform());
            }
        }
        catch
        {
            // Fall back to the SCS hierarchy below for sparse cooked component templates.
        }

        return UnrealSceneTransform.Compose(parentTransform, UnrealSceneTransform.From(component.GetRelativeTransform()));
    }

    private static void CollectInheritableComponentHandler(
        UObject inheritableComponentHandler,
        DefaultFileProvider provider,
        HashSet<string> visited,
        int depth,
        List<ResolvedUnrealMesh> meshes)
    {
        if (inheritableComponentHandler is UInheritableComponentHandler typedHandler)
        {
            try
            {
                foreach (var record in typedHandler.GetRecords())
                {
                    if (record.ComponentTemplate is { } componentTemplate)
                    {
                        CollectMeshExportsFromValue(componentTemplate, provider, visited, depth + 1, meshes);
                    }
                }
            }
            catch
            {
                // Some cooked Fortnite component handlers deserialize only through fallback properties.
            }
        }

        CollectMeshExportsFromValue(inheritableComponentHandler.GetOrDefault<FStructFallback[]>("Records", []), provider, visited, depth + 1, meshes);
    }

    private static void CollectStaticMeshComponent(UStaticMeshComponent component, List<ResolvedUnrealMesh> meshes)
    {
        CollectStaticMeshComponent(component, GetComponentTransform(component), meshes);
    }

    private static void CollectStaticMeshComponent(
        UStaticMeshComponent component,
        UnrealSceneTransform transform,
        List<ResolvedUnrealMesh> meshes,
        string source = "static-mesh-component")
    {
        try
        {
            var meshIndex = component.GetStaticMesh();
            if (!meshIndex.IsNull &&
                TryLoadPackageIndex(meshIndex, out var export) &&
                export is UStaticMesh staticMesh)
            {
                meshes.Add(new ResolvedUnrealMesh(
                    staticMesh,
                    component.GetOrDefault<FPackageIndex[]>("OverrideMaterials", []),
                    transform,
                    CollectionSource: source));
            }
        }
        catch
        {
            // Ignore incomplete component exports and continue resolving siblings.
        }
    }

    private static void CollectSkeletalMeshComponent(USkeletalMeshComponent component, List<ResolvedUnrealMesh> meshes)
    {
        CollectSkeletalMeshComponent(component, GetComponentTransform(component), meshes);
    }

    private static void CollectSkeletalMeshComponent(
        USkeletalMeshComponent component,
        UnrealSceneTransform transform,
        List<ResolvedUnrealMesh> meshes,
        string source = "skeletal-mesh-component")
    {
        try
        {
            var meshIndex = component.GetSkeletalMesh();
            if (!meshIndex.IsNull &&
                TryLoadPackageIndex(meshIndex, out var export) &&
                export is USkeletalMesh skeletalMesh)
            {
                meshes.Add(new ResolvedUnrealMesh(
                    skeletalMesh,
                    component.GetOrDefault<FPackageIndex[]>("OverrideMaterials", []),
                    transform,
                    CollectionSource: source));
            }
        }
        catch
        {
            // Ignore incomplete component exports and continue resolving siblings.
        }
    }

    private static void CollectMeshExportsFromValue(
        object? value,
        DefaultFileProvider provider,
        HashSet<string> visited,
        int depth,
        List<ResolvedUnrealMesh> meshes)
    {
        switch (value)
        {
            case null:
                return;
            case USkeletalMesh or UStaticMesh:
                meshes.Add(new ResolvedUnrealMesh((UObject)value, null, UnrealSceneTransform.Identity, CollectionSource: "property-walk-mesh"));
                return;
            case UObject export:
                CollectMeshExports(export, provider, visited, depth, meshes);
                return;
            case FPackageIndex packageIndex:
                if (TryLoadPackageIndex(packageIndex, out var packageExport) && packageExport is not null)
                {
                    CollectMeshExports(packageExport, provider, visited, depth, meshes);
                }

                return;
            case FSoftObjectPath softObjectPath:
                if (TryLoadSoftObjectPath(softObjectPath, provider, out var softExport) && softExport is not null)
                {
                    CollectMeshExports(softExport, provider, visited, depth, meshes);
                }

                return;
            case FStructFallback fallback:
                foreach (var property in fallback.Properties ?? [])
                {
                    CollectMeshExportsFromValue(property.Tag?.GenericValue, provider, visited, depth + 1, meshes);
                }

                return;
            case UScriptArray array:
                foreach (var property in array.Properties)
                {
                    CollectMeshExportsFromValue(property.GenericValue, provider, visited, depth + 1, meshes);
                }

                return;
            case Array array:
                foreach (var item in array)
                {
                    CollectMeshExportsFromValue(item, provider, visited, depth + 1, meshes);
                }

                return;
        }
    }

    private static ResolvedUnrealMesh? ResolveMeshExport(UObject export, DefaultFileProvider provider)
    {
        return ResolveMeshExport(export, provider, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);
    }

    private static ResolvedUnrealMesh? ResolveMeshExport(
        UObject export,
        DefaultFileProvider provider,
        HashSet<string> visited,
        int depth)
    {
        if (export is USkeletalMesh or UStaticMesh)
        {
            return new ResolvedUnrealMesh(export, null, UnrealSceneTransform.Identity);
        }

        if (export is UStaticMeshComponent staticMeshComponent && ResolveStaticMeshComponent(staticMeshComponent, provider, visited, depth + 1) is { } staticMesh)
        {
            return staticMesh;
        }

        if (export is USkeletalMeshComponent skeletalMeshComponent && ResolveSkeletalMeshComponent(skeletalMeshComponent, provider, visited, depth + 1) is { } skeletalMesh)
        {
            return skeletalMesh;
        }

        if (depth >= _maxResolveDepth || visited.Count >= _maxResolveVisitedCount || !visited.Add(GetExportVisitKey(export)))
        {
            return null;
        }

        if (export is ULevelSaveRecord levelSaveRecord &&
            ResolveLevelSaveRecord(levelSaveRecord, provider, visited, depth + 1) is { } levelSaveRecordMesh)
        {
            return levelSaveRecordMesh;
        }

        if (export is UBlueprintGeneratedClass blueprintGeneratedClass &&
            ResolveBlueprintGeneratedClass(blueprintGeneratedClass, provider, visited, depth + 1) is { } blueprintMesh)
        {
            return blueprintMesh;
        }

        foreach (var propertyName in _meshPropertyNames)
        {
            if (export.Properties.FirstOrDefault(property => property.Name.Text.Equals(propertyName, StringComparison.OrdinalIgnoreCase)) is { } property &&
                ResolveMeshFromPropertyValue(property.Tag?.GenericValue, provider, visited, depth + 1) is { } propertyMesh)
            {
                return propertyMesh;
            }
        }

        foreach (var property in export.Properties)
        {
            if (ResolveMeshFromPropertyValue(property.Tag?.GenericValue, provider, visited, depth + 1) is { } mesh)
            {
                return mesh;
            }
        }

        return null;
    }

    private static ResolvedUnrealMesh? ResolveLevelSaveRecord(
        ULevelSaveRecord levelSaveRecord,
        DefaultFileProvider provider,
        HashSet<string> visited,
        int depth)
    {
        if (levelSaveRecord.TemplateRecords is not null)
        {
            foreach (var templateRecord in levelSaveRecord.TemplateRecords.Values)
            {
                if (TryLoadSoftObjectPath(templateRecord.ActorClass, provider, out var actorClassExport) &&
                    actorClassExport is UBlueprintGeneratedClass blueprintGeneratedClass &&
                    ResolveMeshExport(blueprintGeneratedClass, provider, visited, depth) is { } mesh)
                {
                    return mesh;
                }
            }
        }

        foreach (var actorData in levelSaveRecord.ActorData ?? [])
        {
            if (ResolveMeshFromPropertyValue(actorData, provider, visited, depth + 1) is { } mesh)
            {
                return mesh;
            }
        }

        return null;
    }

    private static ResolvedUnrealMesh? ResolveBlueprintGeneratedClass(
        UBlueprintGeneratedClass blueprintGeneratedClass,
        DefaultFileProvider provider,
        HashSet<string> visited,
        int depth)
    {
        if (blueprintGeneratedClass.TryGetValue(out UObject constructionScript, "SimpleConstructionScript") &&
            ResolveConstructionScript(constructionScript, provider, visited, depth + 1) is { } constructionScriptMesh)
        {
            return constructionScriptMesh;
        }

        if (blueprintGeneratedClass.TryGetValue(out UObject inheritableComponentHandler, "InheritableComponentHandler") &&
            ResolveInheritableComponentHandler(inheritableComponentHandler, provider, visited, depth + 1) is { } inheritedComponentMesh)
        {
            return inheritedComponentMesh;
        }

        if (blueprintGeneratedClass.ClassDefaultObject is { IsNull: false } &&
            TryLoadPackageIndex(blueprintGeneratedClass.ClassDefaultObject, out var classDefaultObject) &&
            classDefaultObject is not null &&
            ResolveMeshExport(classDefaultObject, provider, visited, depth + 1) is { } defaultObjectMesh)
        {
            return defaultObjectMesh;
        }

        if (blueprintGeneratedClass.SuperStruct is { IsNull: false } &&
            TryLoadPackageIndex(blueprintGeneratedClass.SuperStruct, out var superStruct) &&
            superStruct is UBlueprintGeneratedClass superBlueprint &&
            ResolveMeshExport(superBlueprint, provider, visited, depth + 1) is { } superMesh)
        {
            return superMesh;
        }

        return null;
    }

    private static ResolvedUnrealMesh? ResolveConstructionScript(
        UObject constructionScript,
        DefaultFileProvider provider,
        HashSet<string> visited,
        int depth)
    {
        if (ResolveMeshFromPropertyValue(constructionScript.GetOrDefault<UObject[]>("AllNodes", []), provider, visited, depth + 1) is { } allNodesMesh)
        {
            return allNodesMesh;
        }

        if (constructionScript is USimpleConstructionScript simpleConstructionScript &&
            ResolveMeshFromPropertyValue(simpleConstructionScript.GetAllNodesRecursive(), provider, visited, depth + 1) is { } recursiveNodesMesh)
        {
            return recursiveNodesMesh;
        }

        return ResolveMeshExport(constructionScript, provider, visited, depth + 1);
    }

    private static ResolvedUnrealMesh? ResolveInheritableComponentHandler(
        UObject inheritableComponentHandler,
        DefaultFileProvider provider,
        HashSet<string> visited,
        int depth)
    {
        if (inheritableComponentHandler is UInheritableComponentHandler typedHandler)
        {
            try
            {
                foreach (var record in typedHandler.GetRecords())
                {
                    if (record.ComponentTemplate is { } componentTemplate &&
                        ResolveMeshFromPropertyValue(componentTemplate, provider, visited, depth + 1) is { } mesh)
                    {
                        return mesh;
                    }
                }
            }
            catch
            {
                // Some cooked Fortnite component handlers deserialize only through fallback properties.
            }
        }

        return ResolveMeshFromPropertyValue(inheritableComponentHandler.GetOrDefault<FStructFallback[]>("Records", []), provider, visited, depth + 1);
    }

    private static ResolvedUnrealMesh? ResolveStaticMeshComponent(
        UStaticMeshComponent component,
        DefaultFileProvider provider,
        HashSet<string> visited,
        int depth)
    {
        FPackageIndex meshIndex;
        try
        {
            meshIndex = component.GetStaticMesh();
        }
        catch
        {
            return null;
        }

        return ResolveMeshFromPropertyValue(meshIndex, provider, visited, depth + 1) is { } mesh
            ? mesh.WithOverrideMaterials(component.GetOrDefault<FPackageIndex[]>("OverrideMaterials", []))
            : null;
    }

    private static ResolvedUnrealMesh? ResolveSkeletalMeshComponent(
        USkeletalMeshComponent component,
        DefaultFileProvider provider,
        HashSet<string> visited,
        int depth)
    {
        FPackageIndex meshIndex;
        try
        {
            meshIndex = component.GetSkeletalMesh();
        }
        catch
        {
            return null;
        }

        return ResolveMeshFromPropertyValue(meshIndex, provider, visited, depth + 1) is { } mesh
            ? mesh.WithOverrideMaterials(component.GetOrDefault<FPackageIndex[]>("OverrideMaterials", []))
            : null;
    }

    private static ResolvedUnrealMesh? ResolveMeshFromPropertyValue(
        object? value,
        DefaultFileProvider provider,
        HashSet<string> visited,
        int depth)
    {
        switch (value)
        {
            case null:
                return null;
            case USkeletalMesh or UStaticMesh:
                return new ResolvedUnrealMesh((UObject)value, null, UnrealSceneTransform.Identity);
            case UObject export:
                return ResolveMeshExport(export, provider, visited, depth);
            case FPackageIndex packageIndex:
                return TryLoadPackageIndex(packageIndex, out var packageExport) && packageExport is not null
                    ? ResolveMeshExport(packageExport, provider, visited, depth)
                    : null;
            case FSoftObjectPath softObjectPath:
                return TryLoadSoftObjectPath(softObjectPath, provider, out var softExport) && softExport is not null
                    ? ResolveMeshExport(softExport, provider, visited, depth)
                    : null;
            case FStructFallback fallback:
                foreach (var property in fallback.Properties ?? [])
                {
                    if (ResolveMeshFromPropertyValue(property.Tag?.GenericValue, provider, visited, depth + 1) is { } mesh)
                    {
                        return mesh;
                    }
                }

                return null;
            case UScriptArray array:
                foreach (var property in array.Properties)
                {
                    if (ResolveMeshFromPropertyValue(property.GenericValue, provider, visited, depth + 1) is { } mesh)
                    {
                        return mesh;
                    }
                }

                return null;
            case Array array:
                foreach (var item in array)
                {
                    if (ResolveMeshFromPropertyValue(item, provider, visited, depth + 1) is { } mesh)
                    {
                        return mesh;
                    }
                }

                return null;
            default:
                return null;
        }
    }

    private static MaterialOverrideScope? ApplyMaterialOverrides(UObject export, FPackageIndex[]? overrideMaterials)
    {
        if (overrideMaterials is not { Length: > 0 })
        {
            return null;
        }

        return export switch
        {
            UStaticMesh staticMesh => MaterialOverrideScope.TryApply(staticMesh, overrideMaterials),
            USkeletalMesh skeletalMesh => MaterialOverrideScope.TryApply(skeletalMesh, overrideMaterials),
            _ => null
        };
    }

    private static UnrealSceneTransform GetComponentTransform(USceneComponent component)
    {
        try
        {
            return UnrealSceneTransform.From(component.GetAbsoluteTransform());
        }
        catch
        {
            return UnrealSceneTransform.From(component.GetRelativeTransform());
        }
    }

    private static bool TryLoadPackageIndex(FPackageIndex packageIndex, out UObject? export)
    {
        try
        {
            return packageIndex.TryLoad(out export);
        }
        catch
        {
            export = null!;
            return false;
        }
    }

    private static bool TryLoadSoftObjectPath(FSoftObjectPath softObjectPath, DefaultFileProvider provider, out UObject? export)
    {
        try
        {
            return softObjectPath.TryLoad(provider, out export);
        }
        catch
        {
            export = null!;
            return false;
        }
    }

    private static string CreateUnsupportedMeshMessage(UObject export, string archiveEntryPath)
    {
        var properties = export.Properties
            .Select(property => $"{property.Name.Text}:{property.PropertyType.Text}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToArray();
        var propertyDetails = properties.Length == 0
            ? "No readable properties were available."
            : $"Readable properties include: {string.Join(", ", properties)}.";
        return $"UE4/5 export does not expose a supported mesh: {archiveEntryPath}. " +
            $"Export type: {export.ExportType}. {propertyDetails}";
    }

    private static string GetExportVisitKey(UObject export)
    {
        try
        {
            return export.GetPathName();
        }
        catch
        {
            return $"{export.ExportType}:{export.Name}";
        }
    }

    private static string? WaitForExportedMesh(string exportRoot)
    {
        string? previousPath = null;
        long previousLength = -1;

        for (var attempt = 0; attempt < 50; attempt++)
        {
            var meshPath = Directory
                .EnumerateFiles(exportRoot, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetExtension(path) is ".psk" or ".pskx")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (meshPath is not null)
            {
                var length = new FileInfo(meshPath).Length;
                if (length > 0 &&
                    string.Equals(meshPath, previousPath, StringComparison.OrdinalIgnoreCase) &&
                    length == previousLength)
                {
                    WaitForExportTree(exportRoot);
                    return meshPath;
                }

                previousPath = meshPath;
                previousLength = length;
            }

            Thread.Sleep(100);
        }

        return null;
    }

    private static void WaitForExportTree(string exportRoot)
    {
        long previousTotalLength = -1;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var files = Directory.EnumerateFiles(exportRoot, "*", SearchOption.AllDirectories).ToArray();
            var totalLength = files.Sum(path => new FileInfo(path).Length);
            if (totalLength > 0 && totalLength == previousTotalLength)
            {
                return;
            }

            previousTotalLength = totalLength;
            Thread.Sleep(100);
        }
    }

    private static string GetExportRoot(ExplorerFileEntry fileEntry)
    {
        var exportKey = $"{Path.GetFullPath(fileEntry.ArchivePath!)}|{fileEntry.ArchiveEntryPath}";
        var exportHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(exportKey)))[..12];
        return Path.Combine(ExplorerFileSystem.GetExtractionRoot(fileEntry.ArchivePath!), "__ue4_exports", exportHash);
    }

    private static void ResetExportRoot(ExplorerFileEntry fileEntry, string exportRoot)
    {
        var exportsRoot = Path.GetFullPath(Path.Combine(ExplorerFileSystem.GetExtractionRoot(fileEntry.ArchivePath!), "__ue4_exports"));
        var fullExportRoot = Path.GetFullPath(exportRoot);
        if (!fullExportRoot.StartsWith(exportsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new GMConverterException($"Refusing to clear unexpected UE4/5 export cache path: {fullExportRoot}");
        }

        // Opt-in cache reuse: setting `GMCONVERTER_KEEP_EXPORT_CACHE=1` skips the wipe so
        // subsequent re-exports of the same asset reuse the previously-extracted PSK and source
        // textures. CUE4Parse pak mounting + texture decoding is the slow part of the pipeline;
        // sidecars, bakes, and the .glb writer overwrite their outputs in place. Cuts iteration
        // time from minutes to seconds when only the bake/exporter code has changed.
        var keepCache = Environment.GetEnvironmentVariable("GMCONVERTER_KEEP_EXPORT_CACHE");
        var shouldKeep = !string.IsNullOrEmpty(keepCache) &&
            !keepCache.Equals("0", StringComparison.Ordinal) &&
            !keepCache.Equals("false", StringComparison.OrdinalIgnoreCase);

        if (!shouldKeep && Directory.Exists(fullExportRoot))
        {
            Directory.Delete(fullExportRoot, recursive: true);
        }

        Directory.CreateDirectory(fullExportRoot);
    }

    private static string ProviderCacheKey(string root)
    {
        var fullPath = Path.GetFullPath(root);
        var newestArchiveWrite = Directory.Exists(fullPath)
            ? Directory.EnumerateFiles(fullPath, "*", SearchOption.TopDirectoryOnly)
                .Where(path => _archiveExtensions.Contains(Path.GetExtension(path)))
                .Select(path => new FileInfo(path).LastWriteTimeUtc.Ticks)
                .DefaultIfEmpty(0)
                .Max()
            : 0;
        return $"{fullPath}|{newestArchiveWrite}";
    }

    private static string SanitizeObjectPath(string objectName)
    {
        var sanitized = string.Concat(objectName.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_')).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "Object" : sanitized;
    }

    private sealed class AssetRegistryScanStats
    {
        public int FoundCount { get; set; }

        public int UnreadableCount { get; set; }

        public int ParseFailureCount { get; set; }

        public int ReadableCount { get; set; }

        public int SupportedAssetCount { get; set; }

        public string CreateFailureSuffix()
        {
            return FoundCount == 0
                ? "No AssetRegistry.bin files were present in the mounted readable containers. "
                : $"Asset registries found: {FoundCount}; readable: {ReadableCount}; unreadable: {UnreadableCount}; parse failures: {ParseFailureCount}; supported assets found: {SupportedAssetCount}. ";
        }
    }

    // `CollectionSource` tags which traversal added the mesh — used by the part-summary log to
    // diagnose mesh positioning issues (e.g. CDO property walk vs proper SCS walk).
    private sealed record ResolvedUnrealMesh(
        UObject Export,
        FPackageIndex[]? OverrideMaterials,
        UnrealSceneTransform Transform,
        Dictionary<int, UBuildingTextureData>? TextureData = null,
        string CollectionSource = "")
    {
        public ResolvedUnrealMesh WithOverrideMaterials(FPackageIndex[]? overrideMaterials)
        {
            return overrideMaterials is { Length: > 0 } ? this with { OverrideMaterials = overrideMaterials } : this;
        }

        public ResolvedUnrealMesh WithTextureData(Dictionary<int, UBuildingTextureData> textureData)
        {
            return textureData.Count > 0 ? this with { TextureData = textureData } : this;
        }

        public ResolvedUnrealMesh WithParentTransform(UnrealSceneTransform parentTransform)
        {
            return this with { Transform = UnrealSceneTransform.Compose(parentTransform, Transform) };
        }

        public ResolvedUnrealMesh WithSource(string source)
        {
            return string.IsNullOrEmpty(CollectionSource) ? this with { CollectionSource = source } : this;
        }
    }

    private sealed record UnrealSceneManifest(
        int Version,
        string Name,
        IReadOnlyList<UnrealSceneManifestEntry> Entries);

    private sealed record UnrealSceneManifestEntry(string Path, UnrealSceneTransform Transform);

    private sealed record UnrealMaterialTextureOverride(
        Dictionary<string, string> Textures,
        UnrealMaterialParameterOverride? Parameters = null);

    private sealed record UnrealMaterialParameterOverride(Dictionary<string, UnrealMaterialColorOverride> Colors);

    private sealed record UnrealMaterialColorOverride(float R, float G, float B, float A);

    private sealed record UnrealMaterialDebugDump(
        string MaterialName,
        string MaterialPath,
        IReadOnlyList<string> ParentChain,
        string BlendMode,
        string ShadingModel,
        Dictionary<string, string> TextureParameterNames,
        Dictionary<string, float> ScalarParameters,
        Dictionary<string, bool> SwitchParameters,
        Dictionary<string, string> ResolvedTextures);

    private sealed record UnrealMeshExportResult(string MeshPath, bool UsedMeshOnlyFallback);

    private sealed record LevelSaveTemplateRecord(int TemplateKey, int ActorDataIndex, FActorTemplateRecord Record)
    {
        public IEnumerable<ulong> GetActorInstanceLookupKeys()
        {
            if (TemplateKey >= 0)
            {
                yield return (ulong)TemplateKey;
            }

            yield return Record.ID;
        }
    }

    private sealed record UnrealScenePartSummary(
        string Name,
        string ExportType,
        int MaterialCount,
        int TextureDataCount,
        string TextureDataSlots,
        string TransformDetails,
        string CollectionSource,
        bool UsedMeshOnlyFallback);

    private sealed record UnrealSceneTransform(
        UnrealSceneVector3 Translation,
        UnrealSceneQuaternion Rotation,
        UnrealSceneVector3 Scale)
    {
        public static UnrealSceneTransform Identity { get; } = new(
            new UnrealSceneVector3(0, 0, 0),
            new UnrealSceneQuaternion(0, 0, 0, 1),
            new UnrealSceneVector3(1, 1, 1));

        public static UnrealSceneTransform From(FTransform transform)
        {
            return new UnrealSceneTransform(
                new UnrealSceneVector3(transform.Translation.X, transform.Translation.Y, transform.Translation.Z),
                new UnrealSceneQuaternion(transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W),
                new UnrealSceneVector3(transform.Scale3D.X, transform.Scale3D.Y, transform.Scale3D.Z));
        }

        public static UnrealSceneTransform Compose(UnrealSceneTransform parent, UnrealSceneTransform child)
        {
            return From(child.ToFTransform() * parent.ToFTransform());
        }

        private FTransform ToFTransform()
        {
            var rotation = new FQuat(Rotation.X, Rotation.Y, Rotation.Z, Rotation.W);

            if (!rotation.IsNormalized)
            {
                rotation.Normalize();
            }

            return new FTransform(
                rotation,
                new FVector(Translation.X, Translation.Y, Translation.Z),
                new FVector(Scale.X, Scale.Y, Scale.Z));
        }

        public string DedupKey()
        {
            return FormattableString.Invariant(
                $"{Translation.DedupKey()}|{Rotation.DedupKey()}|{Scale.DedupKey()}");
        }

        public bool IsIdentity()
        {
            return Translation.IsZero() && Rotation.IsIdentity() && Scale.IsOne();
        }

        public bool IsAtOrigin()
        {
            return Translation.IsZero();
        }

        public string ToDiagnosticString()
        {
            return FormattableString.Invariant(
                $"t=({Translation.X:0.###},{Translation.Y:0.###},{Translation.Z:0.###}) r=({Rotation.X:0.###},{Rotation.Y:0.###},{Rotation.Z:0.###},{Rotation.W:0.###}) s=({Scale.X:0.###},{Scale.Y:0.###},{Scale.Z:0.###})");
        }
    }

    private sealed record UnrealSceneVector3(float X, float Y, float Z)
    {
        public string DedupKey()
        {
            return string.Join(',', FormatDedupFloat(X), FormatDedupFloat(Y), FormatDedupFloat(Z));
        }

        public bool IsZero()
        {
            return MathF.Abs(X) < 0.0001f && MathF.Abs(Y) < 0.0001f && MathF.Abs(Z) < 0.0001f;
        }

        public bool IsOne()
        {
            return MathF.Abs(X - 1.0f) < 0.0001f &&
                MathF.Abs(Y - 1.0f) < 0.0001f &&
                MathF.Abs(Z - 1.0f) < 0.0001f;
        }
    }

    private sealed record UnrealSceneQuaternion(float X, float Y, float Z, float W)
    {
        public string DedupKey()
        {
            return string.Join(',', FormatDedupFloat(X), FormatDedupFloat(Y), FormatDedupFloat(Z), FormatDedupFloat(W));
        }

        public bool IsIdentity()
        {
            return MathF.Abs(X) < 0.0001f &&
                MathF.Abs(Y) < 0.0001f &&
                MathF.Abs(Z) < 0.0001f &&
                MathF.Abs(W - 1.0f) < 0.0001f;
        }

        public float Dot(UnrealSceneQuaternion other)
        {
            return X * other.X + Y * other.Y + Z * other.Z + W * other.W;
        }

        public float NormalizationError()
        {
            return MathF.Abs(1.0f - (X * X + Y * Y + Z * Z + W * W));
        }
    }

    private static string FormatDedupFloat(float value)
    {
        var normalized = MathF.Abs(value) < 0.0001f ? 0.0f : MathF.Round(value, 4);
        return normalized.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private sealed class MaterialOverrideScope : IDisposable
    {
        private readonly ResolvedObject?[] _materials;
        private readonly ResolvedObject?[] _originalMaterials;
        private readonly Action<int, ResolvedObject?>? _secondarySetter;
        private readonly ResolvedObject?[]? _originalSecondaryMaterials;

        private MaterialOverrideScope(
            ResolvedObject?[] materials,
            ResolvedObject?[] originalMaterials,
            Action<int, ResolvedObject?>? secondarySetter,
            ResolvedObject?[]? originalSecondaryMaterials)
        {
            _materials = materials;
            _originalMaterials = originalMaterials;
            _secondarySetter = secondarySetter;
            _originalSecondaryMaterials = originalSecondaryMaterials;
        }

        public static MaterialOverrideScope? TryApply(UStaticMesh staticMesh, FPackageIndex[] overrideMaterials)
        {
            return TryApply(
                staticMesh.Materials,
                staticMesh.StaticMaterials,
                overrideMaterials,
                staticMesh.StaticMaterials is null ? null : (index, material) => staticMesh.StaticMaterials[index].MaterialInterface = material,
                staticMesh.StaticMaterials?.Select(material => material.MaterialInterface).ToArray());
        }

        public static MaterialOverrideScope? TryApply(USkeletalMesh skeletalMesh, FPackageIndex[] overrideMaterials)
        {
            return TryApply(
                skeletalMesh.Materials,
                skeletalMesh.SkeletalMaterials,
                overrideMaterials,
                skeletalMesh.SkeletalMaterials is null ? null : (index, material) => skeletalMesh.SkeletalMaterials[index].Material = material,
                skeletalMesh.SkeletalMaterials?.Select(material => material.Material).ToArray());
        }

        private static MaterialOverrideScope? TryApply<TMaterial>(
            ResolvedObject?[]? materials,
            TMaterial[]? secondaryMaterials,
            FPackageIndex[] overrideMaterials,
            Action<int, ResolvedObject?>? secondarySetter,
            ResolvedObject?[]? originalSecondaryMaterials)
        {
            if (materials is null || materials.Length == 0)
            {
                return null;
            }

            var originalMaterials = materials.ToArray();
            Apply(materials, overrideMaterials);
            if (secondarySetter is not null && secondaryMaterials is not null)
            {
                Apply(secondaryMaterials.Length, overrideMaterials, secondarySetter);
            }

            return new MaterialOverrideScope(materials, originalMaterials, secondarySetter, originalSecondaryMaterials);
        }

        public void Dispose()
        {
            Restore(_materials, _originalMaterials);
            if (_secondarySetter is not null && _originalSecondaryMaterials is not null)
            {
                for (var i = 0; i < _originalSecondaryMaterials.Length; i++)
                {
                    _secondarySetter(i, _originalSecondaryMaterials[i]);
                }
            }
        }

        private static void Apply(ResolvedObject?[] materials, FPackageIndex[] overrideMaterials)
        {
            var count = Math.Min(materials.Length, overrideMaterials.Length);
            for (var i = 0; i < count; i++)
            {
                if (overrideMaterials[i] is { IsNull: false } overrideMaterial)
                {
                    materials[i] = overrideMaterial.ResolvedObject;
                }
            }
        }

        private static void Apply(int materialCount, FPackageIndex[] overrideMaterials, Action<int, ResolvedObject?> setter)
        {
            var count = Math.Min(materialCount, overrideMaterials.Length);
            for (var i = 0; i < count; i++)
            {
                if (overrideMaterials[i] is { IsNull: false } overrideMaterial)
                {
                    setter(i, overrideMaterial.ResolvedObject);
                }
            }
        }

        private static void Restore(ResolvedObject?[] target, ResolvedObject?[] source)
        {
            var count = Math.Min(target.Length, source.Length);
            for (var i = 0; i < count; i++)
            {
                target[i] = source[i];
            }
        }
    }
}
