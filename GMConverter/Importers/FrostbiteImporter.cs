using System.Buffers.Binary;
using System.Text.Json;
using Frosty.Sdk.Managers;
using GMConverter.Common;
using GMConverter.Explorer;
using GMConverter.Formats.Frostbite;
using GMConverter.Geometry;
using Mesh = GMConverter.Geometry.Mesh;
using Vertex = GMConverter.Geometry.Vertex;

namespace GMConverter.Importers;

// Importer for Frostbite mesh assets. The "input file" is a .frostbiteref JSON manifest written
// by FrostbiteExplorer.ResolveEntry. The manifest points at an install root + EBX asset name;
// this importer mounts the install via FrostbiteContext (no-op if already mounted from a Scan),
// pulls the matching RES + chunk through FrostySdk, parses the MeshSet header with
// MeshSetReader, decodes LOD0's renderable sections through FrostbiteVertexDecoder, and emits a
// GMConverter Model with one Mesh + per-section Submeshes grouped by material.
//
// Materials are intentionally empty for MVP — texture decoding is best-effort without the type
// SDK (see FrostbiteTextureFormatResolver) and wiring partially-broken textures into the
// preview is worse UX than no textures. Phase-2 will add material+texture binding once the
// SDK generator path is in place.
internal sealed class FrostbiteImporter : IImporter
{
    private static readonly JsonSerializerOptions _manifestJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public string InputFormat => "frostbite";

    public string InputName => "Frostbite (Star Wars)";

    public object Summarize(string inputPath)
    {
        var manifest = ReadManifest(inputPath);
        FrostbiteContext.Mount(manifest.InstallRoot);

        var ebx = AssetManager.GetEbxAssetEntry(manifest.AssetName)
            ?? throw new GMConverterException($"Frostbite asset \"{manifest.AssetName}\" not found in mounted install.");
        var res = AssetManager.GetResAssetEntry(manifest.AssetName)
            ?? throw new GMConverterException($"No MeshSet RES sidecar for \"{manifest.AssetName}\".");

        using var resBlock = AssetManager.GetAsset(res);
        var meshSet = MeshSetReader.Parse(resBlock.ToArray());
        var lod = meshSet.Lods[0];
        var renderableSections = lod.Sections.Where(IsRenderable).ToArray();

        return new
        {
            Asset = manifest.AssetName,
            ebx.Type,
            Lods = meshSet.Lods.Count,
            Lod0Sections = lod.Sections.Count,
            Lod0Renderable = renderableSections.Length,
            Lod0VertexBufferBytes = lod.VertexBufferSize,
            Lod0IndexBufferBytes = lod.IndexBufferSize
        };
    }

    public Model Parse(string inputPath, ModelParseOptions options)
    {
        var manifest = ReadManifest(inputPath);
        FrostbiteContext.Mount(manifest.InstallRoot);

        var res = AssetManager.GetResAssetEntry(manifest.AssetName)
            ?? throw new GMConverterException($"No MeshSet RES sidecar for \"{manifest.AssetName}\".");

        using (PerfTimer.Measure("frostbite.import", "MeshSetReader.Parse", manifest.AssetName))
        {
            using var resBlock = AssetManager.GetAsset(res);
            var meshSet = MeshSetReader.Parse(resBlock.ToArray());

            var lod = meshSet.Lods[0];
            var chunk = AssetManager.GetChunkAssetEntry(lod.ChunkId)
                ?? throw new GMConverterException($"Mesh LOD references chunk {lod.ChunkId} which is not present in the asset manager.");

            byte[] chunkBytes;
            using (PerfTimer.Measure("frostbite.import", "GetChunk", lod.ChunkId.ToString()))
            {
                using var chunkBlock = AssetManager.GetAsset(chunk);
                chunkBytes = chunkBlock.ToArray();
            }

            return BuildModel(meshSet, lod, chunkBytes, manifest.AssetName, options);
        }
    }

    private static Model BuildModel(
        FrostbiteMeshSet meshSet,
        FrostbiteMeshLod lod,
        byte[] chunkBytes,
        string assetName,
        ModelParseOptions options)
    {
        // Chunk layout per LOD: [vertex buffer (lod.VertexBufferSize)] [index buffer (lod.IndexBufferSize)].
        if (chunkBytes.Length < lod.VertexBufferSize + lod.IndexBufferSize)
        {
            throw new GMConverterException(
                $"Mesh chunk is {chunkBytes.Length} bytes but LOD needs {lod.VertexBufferSize + lod.IndexBufferSize}. Truncated chunk fetch.");
        }

        var vertexBuffer = chunkBytes.AsSpan(0, (int)lod.VertexBufferSize);
        var indexBuffer = chunkBytes.AsSpan((int)lod.VertexBufferSize, (int)lod.IndexBufferSize);

        // Frosty's authoritative renderable filter: a section is visible only if it's in the
        // Opaque / Transparent / TransparentDecal subset categories. ZOnly + Shadow proxies
        // share geometry with the renderable section but with a stripped vertex layout, and
        // rendering them produces overlapping white triangles where the body should be.
        // Falls back to the heuristic (declaration element count > 1) only when the MeshSet
        // didn't classify anything — handles malformed assets without breaking the common path.
        FrostbiteMeshSection[] renderableSections =
            lod.RenderableSectionIndices.Count > 0
                ? [.. lod.RenderableSectionIndices
                    .Where(idx => idx < lod.Sections.Count)
                    .Select(idx => lod.Sections[idx])
                    .Where(s => s.PrimitiveCount > 0)]
                : [.. lod.Sections.Where(IsRenderable)];
        if (renderableSections.Length == 0)
        {
            throw new GMConverterException(
                $"Mesh LOD has no renderable sections (only depth-only/proxy geometry).");
        }


        var indexWidth = InferIndexWidth(lod, renderableSections);

        var pooledVertices = new List<Vertex>(renderableSections.Sum(s => (int)s.VertexCount));
        var trianglesByMaterial = new Dictionary<string, List<Triangle>>(StringComparer.OrdinalIgnoreCase);
        var materialIdByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // section.VertexOffset is the *byte offset into the chunk's vertex buffer* where this
        // section's vertex data starts (per FrostyToolsuite's FBXExporter — `reader.Position =
        // section.VertexOffset` before reading VertexCount vertices). Sections can be laid out
        // non-sequentially or with different strides interleaved, so using the field directly
        // instead of accumulating from cumulative sizes is required for the general case.
        foreach (var section in renderableSections)
        {
            var sectionBytes = (int)(section.VertexCount * section.VertexStride);
            var sectionSlice = vertexBuffer.Slice((int)section.VertexOffset, sectionBytes);
            var decoded = FrostbiteVertexDecoder.Decode(sectionSlice, section);

            var baseVertexIndex = pooledVertices.Count;
            for (var i = 0; i < decoded.Positions.Count; i++)
            {
                var pos = ModelAxisTransforms.TransformPosition(decoded.Positions[i] * options.ScaleFactor, options.AxisMode, "frostbite");
                var nrm = ModelAxisTransforms.TransformNormal(decoded.Normals[i], options.AxisMode, "frostbite");
                pooledVertices.Add(new Vertex(pos, nrm, decoded.Uvs[i]));
            }

            var materialName = string.IsNullOrWhiteSpace(section.MaterialName)
                ? $"section_{Array.IndexOf(renderableSections, section)}"
                : NameHelpers.SanitizeMaterialName(section.MaterialName);

            if (!trianglesByMaterial.TryGetValue(materialName, out var triangles))
            {
                triangles = [];
                trianglesByMaterial.Add(materialName, triangles);
                materialIdByName[materialName] = section.MaterialId;
            }

            // Indices in the LOD index buffer are 0-based within each section's vertex slice
            // (DrawIndexed adds VertexOffset at submit time). Rebase into the merged vertex pool
            // by adding baseVertexIndex.
            var sectionIndexCount = (int)section.PrimitiveCount * 3;
            var sectionIndexBytes = indexBuffer.Slice((int)section.StartIndex * indexWidth, sectionIndexCount * indexWidth);
            for (var t = 0; t < section.PrimitiveCount; t++)
            {
                var ia = ReadIndex(sectionIndexBytes, t * 3, indexWidth);
                var ib = ReadIndex(sectionIndexBytes, t * 3 + 1, indexWidth);
                var ic = ReadIndex(sectionIndexBytes, t * 3 + 2, indexWidth);
                triangles.Add(new Triangle(
                    baseVertexIndex + ia,
                    baseVertexIndex + ib,
                    baseVertexIndex + ic));
            }
        }

        var mesh = new Mesh(
            pooledVertices,
            [.. trianglesByMaterial.Select(pair => new Submesh(pair.Key, pair.Value))],
            Name: NameHelpers.SanitizeFileName(meshSet.Name));

        // Material resolution is heuristic: walks the install's TextureAsset names looking for
        // siblings of the mesh path that match by stem + a suffix table. Failures are tolerated
        // so a single bad ordinal doesn't strip every binding (geometry still previews).
        // Pass the mesh's partition GUID so the resolver can consult MeshVariationDatabase for
        // authoritative per-material texture bindings. Falls back to path-heuristic if no MVDB
        // entry exists for this mesh.
        var meshEbx = AssetManager.GetEbxAssetEntry(assetName);
        var meshGuid = meshEbx?.Guid ?? Guid.Empty;
        var submeshKeys = trianglesByMaterial.Keys
            .Select(name => new FrostbiteMaterialResolver.SubmeshKey(name, materialIdByName[name]))
            .ToArray();
        var materials = FrostbiteMaterialResolver.Resolve(assetName, meshGuid, submeshKeys);

        _ = options;
        return new Model(
            Name: NameHelpers.SanitizeFileName(Path.GetFileName(assetName)),
            Meshes: [mesh],
            Materials: materials);
    }

    private static bool IsRenderable(FrostbiteMeshSection section)
    {
        // Sections with only a single Pos element are depth-only / shadow proxies. Their stride
        // is typically 8 bytes (one Half4 position). We don't surface them in the preview.
        return section.Declarations.Count > 0 && section.Declarations[0].Elements.Count > 1;
    }

    private static int InferIndexWidth(FrostbiteMeshLod lod, IReadOnlyList<FrostbiteMeshSection> sections)
    {
        // The on-disk IndexBufferFormat value for SWS is a raw RenderFormat ordinal that needs
        // TypeLibrary to interpret. We work around it by deriving the width from arithmetic:
        // sum of primitive counts × 3 indices × bytesPerIndex = lod.IndexBufferSize.
        // Index counts include depth sections too, so we sum across the whole LOD, not just
        // renderable.
        var totalIndices = lod.Sections.Sum(s => (long)s.PrimitiveCount * 3);
        if (totalIndices == 0)
        {
            return 2;
        }

        var width = lod.IndexBufferSize / totalIndices;
        return width switch
        {
            2 => 2,
            4 => 4,
            _ => throw new GMConverterException(
                $"Cannot infer index width: LOD has {totalIndices} indices and a {lod.IndexBufferSize}-byte index buffer (= {width} B/index).")
        };
    }

    private static int ReadIndex(ReadOnlySpan<byte> indices, int position, int width)
    {
        var slice = indices.Slice(position * width, width);
        return width == 2
            ? BinaryPrimitives.ReadUInt16LittleEndian(slice)
            : (int)BinaryPrimitives.ReadUInt32LittleEndian(slice);
    }

    private static FrostbiteRefManifest ReadManifest(string path)
    {
        var json = File.ReadAllText(path);
        var manifest = JsonSerializer.Deserialize<FrostbiteRefManifest>(json, _manifestJsonOptions);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.InstallRoot) || string.IsNullOrWhiteSpace(manifest.AssetName))
        {
            throw new GMConverterException($"Invalid .frostbiteref manifest: {path}");
        }
        return manifest;
    }

    internal sealed record FrostbiteRefManifest(string InstallRoot, string AssetName);
}
