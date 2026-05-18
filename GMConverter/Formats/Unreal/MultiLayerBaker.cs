using System.Numerics;
using System.Text.Json;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Meshes;
using CUE4Parse_Conversion.Meshes.PSK;
using CUE4Parse_Conversion.Textures;
using GMConverter.Common;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GMConverter.Formats.Unreal;

/// <summary>
/// Bakes Fortnite multi-layer materials (Use 2/3/4 Layers) into single flat textures by
/// reproducing FortnitePorting's layer-mask formula at export time. FortnitePorting's Blender
/// shader looks up <c>UV1.x</c> per pixel and overlays Layer N where <c>UV1.x &gt; N-1</c>; we
/// rasterize triangles in UV0 space, interpolate UV1 per pixel, and write the appropriate layer's
/// source texel into a single baked Diffuse/Normals/SpecularMasks image. The result looks the
/// same as FP's Blender render in any flat output format (glTF/MDL/OBJ).
///
/// Each call bakes per-part: sibling mesh variants that share a material name (common in scene
/// PPIDs that use mirrored _A/_B mesh pairs) have completely different UV0/UV1 layouts, so we
/// emit a per-part baked PNG and rename the material with a per-part hash suffix so the downstream
/// glTF/MDL material table doesn't collapse them into one. The PSK importer reads the alias from
/// the sidecar and remaps the submesh material key to match.
///
/// The mesh's UV1 channel only exists in CUE4Parse's converted mesh data — the PSK exporter
/// strips it. We bypass PSK and call <see cref="MeshConverter"/> directly to keep that data.
/// </summary>
internal static class MultiLayerBaker
{
    private static readonly string[] _layer2Switches = ["Use 2 Layers", "Use_2_Layers", "Use 2 Materials"];
    private static readonly string[] _layer3Switches = ["Use 3 Layers", "Use_3_Layers", "Use 3 Materials"];
    private static readonly string[] _layer4Switches = ["Use 4 Layers", "Use_4_Layers", "Use 4 Materials"];

    /// <summary>
    /// Detects multi-layer materials on the export and replaces their canonical Diffuse/Normals/
    /// SpecularMasks textures with baked single-image composites. Mutates each material's sidecar
    /// JSON to point at the new files and to record a per-part material alias. Silently does
    /// nothing for single-layer materials or when the mesh has no UV1 channel.
    /// </summary>
    public static void BakeMultiLayerMaterials(
        UObject meshExport,
        UMaterialInterface?[] materialInterfaces,
        string outputDirectory,
        JsonSerializerOptions jsonOptions)
    {
        if (materialInterfaces.Length == 0)
        {
            return;
        }

        if (!TryGetMeshLod(meshExport, out var lod, out var verts) || verts is null || lod.Indices is null || lod.Sections is null)
        {
            return;
        }

        if (lod.NumTexCoords < 2 || lod.ExtraUV is null)
        {
            // Mesh has only UV0 — there's no per-vertex layer mask to evaluate.
            return;
        }

        var uv1Channel = lod.ExtraUV.Value[0];
        var indices = lod.Indices.Value;
        var sections = lod.Sections.Value;
        var partHash = ComputePartHash(outputDirectory);

        for (var slot = 0; slot < materialInterfaces.Length; slot++)
        {
            var material = materialInterfaces[slot];
            if (material is null)
            {
                continue;
            }

            var parameters = new CMaterialParams2();
            material.GetParams(parameters, EMaterialFormat.AllLayers);
            var layerCount = GetActiveLayerCount(parameters);
            if (layerCount < 2)
            {
                continue;
            }

            var triangles = CollectTrianglesForSlot(sections, indices, verts, uv1Channel, slot);
            if (triangles.Count == 0)
            {
                continue;
            }

            WriteUvDiagnostic(material, layerCount, triangles, outputDirectory);
            WriteBakeDiagnostic(material, parameters, layerCount, triangles, outputDirectory);
            BakeSingleMaterial(material, parameters, layerCount, triangles, outputDirectory, jsonOptions, partHash);
        }
    }

    private static bool TryGetMeshLod(UObject meshExport, out CBaseMeshLod lod, out CMeshVertex[]? verts)
    {
        lod = null!;
        verts = null;
        try
        {
            switch (meshExport)
            {
                case UStaticMesh staticMesh when staticMesh.TryConvert(out var convertedStatic, out _) && convertedStatic.LODs.Count > 0:
                    lod = convertedStatic.LODs[0];
                    verts = convertedStatic.LODs[0].Verts;
                    return true;
                case USkeletalMesh skeletalMesh when skeletalMesh.TryConvert(out var convertedSkeletal) && convertedSkeletal.LODs.Count > 0:
                    lod = convertedSkeletal.LODs[0];
                    verts = convertedSkeletal.LODs[0].Verts is { } skelVerts
                        ? [.. skelVerts.Cast<CMeshVertex>()]
                        : null;
                    return true;
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static int GetActiveLayerCount(CMaterialParams2 parameters)
    {
        if (TryGetTrueSwitch(parameters, _layer4Switches))
        {
            return 4;
        }

        if (TryGetTrueSwitch(parameters, _layer3Switches))
        {
            return 3;
        }

        if (TryGetTrueSwitch(parameters, _layer2Switches))
        {
            return 2;
        }

        return 1;
    }

    private static bool TryGetTrueSwitch(CMaterialParams2 parameters, string[] names)
    {
        return parameters.TryGetSwitch(out var value, names) && value;
    }

    private static List<TriangleUv> CollectTrianglesForSlot(
        CMeshSection[] sections,
        uint[] indices,
        CMeshVertex[] verts,
        CUE4Parse.UE4.Objects.Meshes.FMeshUVFloat[] uv1Channel,
        int slot)
    {
        var triangles = new List<TriangleUv>();
        foreach (var section in sections)
        {
            if (section.MaterialIndex != slot)
            {
                continue;
            }

            for (var face = 0; face < section.NumFaces; face++)
            {
                var baseIndex = section.FirstIndex + face * 3;
                if (baseIndex + 2 >= indices.Length)
                {
                    break;
                }

                var i0 = (int)indices[baseIndex];
                var i1 = (int)indices[baseIndex + 1];
                var i2 = (int)indices[baseIndex + 2];
                if (i0 >= verts.Length || i1 >= verts.Length || i2 >= verts.Length ||
                    i0 >= uv1Channel.Length || i1 >= uv1Channel.Length || i2 >= uv1Channel.Length)
                {
                    continue;
                }

                triangles.Add(new TriangleUv(
                    Uv0A: new Vector2(verts[i0].UV.U, verts[i0].UV.V),
                    Uv0B: new Vector2(verts[i1].UV.U, verts[i1].UV.V),
                    Uv0C: new Vector2(verts[i2].UV.U, verts[i2].UV.V),
                    Uv1A: new Vector2(uv1Channel[i0].U, uv1Channel[i0].V),
                    Uv1B: new Vector2(uv1Channel[i1].U, uv1Channel[i1].V),
                    Uv1C: new Vector2(uv1Channel[i2].U, uv1Channel[i2].V)));
            }
        }

        return triangles;
    }

    private static void BakeSingleMaterial(
        UMaterialInterface material,
        CMaterialParams2 parameters,
        int layerCount,
        List<TriangleUv> triangles,
        string outputDirectory,
        JsonSerializerOptions jsonOptions,
        string partHash)
    {
        var diffuseLayers = LoadChannelLayers(parameters, layerCount, CMaterialParams2.Diffuse);
        var normalLayers = LoadChannelLayers(parameters, layerCount, CMaterialParams2.Normals);
        var specularLayers = LoadChannelLayers(parameters, layerCount, CMaterialParams2.SpecularMasks);

        if (diffuseLayers.Count == 0)
        {
            DisposeAll(diffuseLayers); DisposeAll(normalLayers); DisposeAll(specularLayers);
            return;
        }

        // Output tile count comes from THIS part's UV0 extent — sibling mesh variants that share a
        // material name may have completely different UV0 ranges (some narrow [0,1], others span
        // multiple tiles), and we bake one PNG per part so each gets its own correct dimensions.
        var (tileX, tileY) = ComputeTileExtent(triangles);

        var baseWidth = diffuseLayers[0].Width;
        var baseHeight = diffuseLayers[0].Height;
        var width = baseWidth * tileX;
        var height = baseHeight * tileY;

        var sanitizedName = NameHelpers.SanitizeMaterialName(material.Name);
        var aliasName = $"{sanitizedName}__p{partHash}";
        var bakedDiffuseName = $"{aliasName}_baked_diffuse";
        var bakedNormalsName = $"{aliasName}_baked_normals";
        var bakedSpecularName = $"{aliasName}_baked_specular";

        BakeChannel(diffuseLayers, triangles, width, height, baseWidth, baseHeight, layerCount, Path.Combine(outputDirectory, bakedDiffuseName + ".png"));
        if (normalLayers.Count > 0)
        {
            BakeChannel(normalLayers, triangles, width, height, baseWidth, baseHeight, layerCount, Path.Combine(outputDirectory, bakedNormalsName + ".png"));
        }
        if (specularLayers.Count > 0)
        {
            BakeChannel(specularLayers, triangles, width, height, baseWidth, baseHeight, layerCount, Path.Combine(outputDirectory, bakedSpecularName + ".png"));
        }

        UpdateSidecarReferences(
            material.Name,
            outputDirectory,
            jsonOptions,
            diffuseRef: bakedDiffuseName,
            normalRef: normalLayers.Count > 0 ? bakedNormalsName : null,
            specularRef: specularLayers.Count > 0 ? bakedSpecularName : null,
            uvScaleX: tileX > 1 ? 1f / tileX : (float?)null,
            uvScaleY: tileY > 1 ? 1f / tileY : (float?)null,
            materialAlias: aliasName);

        DisposeAll(diffuseLayers);
        DisposeAll(normalLayers);
        DisposeAll(specularLayers);
    }

    private static (int tileX, int tileY) ComputeTileExtent(List<TriangleUv> triangles)
    {
        var maxU = 1f;
        var maxV = 1f;
        foreach (var t in triangles)
        {
            maxU = Math.Max(maxU, Math.Max(t.Uv0A.X, Math.Max(t.Uv0B.X, t.Uv0C.X)));
            maxV = Math.Max(maxV, Math.Max(t.Uv0A.Y, Math.Max(t.Uv0B.Y, t.Uv0C.Y)));
        }

        // Round up to whole tiles, clamp to reasonable bounds (a single material with > 8 tiles
        // is unusual and probably indicates degenerate UVs we shouldn't blow up the output for).
        var tileX = Math.Clamp((int)Math.Ceiling(maxU), 1, 8);
        var tileY = Math.Clamp((int)Math.Ceiling(maxV), 1, 8);
        return (tileX, tileY);
    }

    private static List<Image<Rgba32>> LoadChannelLayers(CMaterialParams2 parameters, int layerCount, string[][] channelNames)
    {
        var images = new List<Image<Rgba32>>(layerCount);
        for (var i = 0; i < layerCount && i < channelNames.Length; i++)
        {
            if (!parameters.TryGetTexture2d(out var texture, channelNames[i]) || texture is not UTexture2D tex2d)
            {
                continue;
            }

            try
            {
                if (tex2d.Decode(ETexturePlatform.DesktopMobile) is not { } bitmap)
                {
                    continue;
                }

                var pngBytes = bitmap.Encode(ETextureFormat.Png, true, out _);
                var image = Image.Load<Rgba32>(pngBytes);
                images.Add(image);
            }
            catch
            {
                // Skip unreadable layers — the baker will fall back to the previous layer.
            }
        }

        return images;
    }

    private static void BakeChannel(
        List<Image<Rgba32>> sourceLayers,
        List<TriangleUv> triangles,
        int width,
        int height,
        int baseWidth,
        int baseHeight,
        int layerCount,
        string outputPath)
    {
        var effectiveLayers = Math.Min(layerCount, sourceLayers.Count);

        // Pre-extract source pixel buffers for fast indexing during rasterization. ImageSharp's
        // ProcessPixelRows is too slow for per-pixel triangle work, and we need random-access
        // sampling into multiple source layers.
        var sourceBuffers = new Rgba32[effectiveLayers][];
        var sourceWidths = new int[effectiveLayers];
        var sourceHeights = new int[effectiveLayers];
        for (var i = 0; i < effectiveLayers; i++)
        {
            var src = sourceLayers[i];
            sourceWidths[i] = src.Width;
            sourceHeights[i] = src.Height;
            sourceBuffers[i] = new Rgba32[src.Width * src.Height];
            src.CopyPixelDataTo(sourceBuffers[i]);
        }

        var outputBuffer = new Rgba32[width * height];

        // Background fill: per-column dominant layer voted by triangles. UV gutters (pixels not
        // covered by any triangle) sample the same layer as nearby triangles do, so bilinear
        // filtering on the export consumer side doesn't blend in the wrong layer's texture (e.g.,
        // MI_NobleCrest_LAATRear_A's Layer-1 slot points at the FrontLaser fallback texture —
        // tile-column-based fill bled that fallback into the visible rear-hull region).
        var dominantLayerPerColumn = ComputeDominantLayerPerColumn(triangles, width, baseWidth, effectiveLayers);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var layer = dominantLayerPerColumn[x / baseWidth];
                var srcW = sourceWidths[layer];
                var srcH = sourceHeights[layer];
                var sx = (x % baseWidth) * srcW / baseWidth;
                var sy = (y % baseHeight) * srcH / baseHeight;
                outputBuffer[y * width + x] = sourceBuffers[layer][sy * srcW + sx];
            }
        }

        foreach (var triangle in triangles)
        {
            RasterizeTriangle(
                outputBuffer, width, height, baseWidth, baseHeight,
                triangle,
                sourceBuffers, sourceWidths, sourceHeights,
                effectiveLayers);
        }

        var output = Image.LoadPixelData<Rgba32>(outputBuffer, width, height);
        try
        {
            output.Save(outputPath);
        }
        finally
        {
            output.Dispose();
        }
    }

    private static int[] ComputeDominantLayerPerColumn(
        List<TriangleUv> triangles,
        int width,
        int baseWidth,
        int effectiveLayers)
    {
        // Background fill for pixels no triangle covers. With UV1-based layer selection the
        // column index doesn't tell us the layer (a pixel in UV0 column 2 might still pick
        // Layer 1 if the triangle's UV1.x is < 1 there), so we vote per column from triangle
        // centroids: bucket each triangle by (column = floor(UV0.x), layer = floor(UV1.x))
        // and fill each background column with whichever layer dominated. This stops the
        // Layer-1 fallback texture (e.g. MI_NobleCrest_LAATRear_A's FrontLaser slot) from
        // bleeding into UV-gutter pixels across a triangle edge.
        var tileX = Math.Max(1, width / baseWidth);
        var histogram = new int[tileX, effectiveLayers];

        foreach (var t in triangles)
        {
            var uv0x = (t.Uv0A.X + t.Uv0B.X + t.Uv0C.X) / 3f;
            var uv1x = (t.Uv1A.X + t.Uv1B.X + t.Uv1C.X) / 3f;
            var column = Math.Clamp((int)MathF.Floor(uv0x), 0, tileX - 1);
            var layer = Math.Clamp((int)MathF.Floor(uv1x), 0, effectiveLayers - 1);
            histogram[column, layer]++;
        }

        var dominant = new int[tileX];
        for (var c = 0; c < tileX; c++)
        {
            var best = 0;
            var bestCount = histogram[c, 0];
            for (var l = 1; l < effectiveLayers; l++)
            {
                if (histogram[c, l] > bestCount)
                {
                    bestCount = histogram[c, l];
                    best = l;
                }
            }

            // No triangles covered this column? Fall back to column-index = layer-index (the
            // natural layout for aligned UV0/UV1 multi-tile parts).
            dominant[c] = bestCount > 0 ? best : Math.Min(c, effectiveLayers - 1);
        }

        return dominant;
    }

    private static void RasterizeTriangle(
        Rgba32[] outputBuffer, int width, int height, int baseWidth, int baseHeight,
        TriangleUv t,
        Rgba32[][] sourceBuffers, int[] sourceWidths, int[] sourceHeights,
        int effectiveLayers)
    {
        // Map UV0 (which spans [0, tileX]×[0, tileY] for this part's range) to output pixel
        // coordinates. Output is `baseWidth * tileX` wide, so each unit of UV0.x === `baseWidth`
        // pixels. Multiplying by `width` would scale by an extra factor of tileX and smear the
        // triangle across multiple tiles — exactly the bug that broke wide-UV0 multi-layer parts
        // (Body_A, Rear_A part 11) when this rasterizer replaced the prior tile-stamping pass.
        var p0 = new Vector2(t.Uv0A.X * baseWidth, t.Uv0A.Y * baseHeight);
        var p1 = new Vector2(t.Uv0B.X * baseWidth, t.Uv0B.Y * baseHeight);
        var p2 = new Vector2(t.Uv0C.X * baseWidth, t.Uv0C.Y * baseHeight);

        var minX = Math.Max(0, (int)Math.Floor(Math.Min(p0.X, Math.Min(p1.X, p2.X))));
        var maxX = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(p0.X, Math.Max(p1.X, p2.X))));
        var minY = Math.Max(0, (int)Math.Floor(Math.Min(p0.Y, Math.Min(p1.Y, p2.Y))));
        var maxY = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(p0.Y, Math.Max(p1.Y, p2.Y))));

        var edge01x = p1.X - p0.X;
        var edge01y = p1.Y - p0.Y;
        var edge02x = p2.X - p0.X;
        var edge02y = p2.Y - p0.Y;
        var det = edge01x * edge02y - edge01y * edge02x;
        if (Math.Abs(det) < 1e-6f)
        {
            return;
        }

        var invDet = 1f / det;

        for (var py = minY; py <= maxY; py++)
        {
            for (var px = minX; px <= maxX; px++)
            {
                var dx = px + 0.5f - p0.X;
                var dy = py + 0.5f - p0.Y;
                var v = (dx * edge02y - dy * edge02x) * invDet;
                var w = (-dx * edge01y + dy * edge01x) * invDet;
                var u = 1f - v - w;
                if (u < 0f || v < 0f || w < 0f)
                {
                    continue;
                }

                // FortnitePorting FPv4 Layer formula: `(UV.x > Layer-1) * UseLayer` where the UV
                // Map node inside the FPv4 Layer node group has `uv_map='UV1'` explicitly set
                // (confirmed by introspecting FP's Blender output — earlier shader-text dumps hid
                // the property because the default-printer skipped it). Stacking multiple FPv4
                // Layer instances collapses to "highest layer N such that UV1.x > N-1" →
                // floor(UV1.x) (zero-indexed). Sampling textures uses the active render UV,
                // which FP leaves as UV0 → `verts[i].UV` in our CUE4Parse data.
                var uv0x = u * t.Uv0A.X + v * t.Uv0B.X + w * t.Uv0C.X;
                var uv0y = u * t.Uv0A.Y + v * t.Uv0B.Y + w * t.Uv0C.Y;
                var uv1x = u * t.Uv1A.X + v * t.Uv1B.X + w * t.Uv1C.X;
                var layerIndex = Math.Clamp((int)MathF.Floor(uv1x), 0, effectiveLayers - 1);

                // Source sample at (uv0 mod 1) so multi-tile UV0 wraps per-tile back into the
                // source texture's [0,1] range.
                var srcW = sourceWidths[layerIndex];
                var srcH = sourceHeights[layerIndex];
                var su = uv0x - MathF.Floor(uv0x);
                var sv = uv0y - MathF.Floor(uv0y);
                var sx = Math.Clamp((int)(su * srcW), 0, srcW - 1);
                var sy = Math.Clamp((int)(sv * srcH), 0, srcH - 1);
                outputBuffer[py * width + px] = sourceBuffers[layerIndex][sy * srcW + sx];
            }
        }
    }

    private static string ComputePartHash(string outputDirectory)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(outputDirectory);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    private static void UpdateSidecarReferences(
        string materialName,
        string outputDirectory,
        JsonSerializerOptions jsonOptions,
        string diffuseRef,
        string? normalRef,
        string? specularRef,
        float? uvScaleX,
        float? uvScaleY,
        string materialAlias)
    {
        var sidecarPath = Path.Combine(outputDirectory, NameHelpers.SanitizeMaterialName(materialName) + ".json");
        if (!File.Exists(sidecarPath))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(sidecarPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("Textures", out var texturesEl) || texturesEl.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var textures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in texturesEl.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    textures[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }
            }

            textures["Diffuse"] = diffuseRef;
            if (normalRef is not null)
            {
                textures["Normals"] = normalRef;
            }

            if (specularRef is not null)
            {
                textures["SpecularMasks"] = specularRef;
            }

            object? parametersBlock = null;
            if (root.TryGetProperty("Parameters", out var parametersEl) && parametersEl.ValueKind != JsonValueKind.Null)
            {
                parametersBlock = JsonSerializer.Deserialize<object>(parametersEl.GetRawText());
            }

            // BakedUv0Scale tells the downstream consumer (GLTFExporter) to wrap the texture
            // sampling UVs by this factor so the mesh's tiled UV0 lands in the right tile of the
            // baked output texture. Without this the bake is a strict regression — multi-tile UVs
            // sample the wrong region. See KHR_texture_transform in glTF.
            object? bakedScale = null;
            if (uvScaleX.HasValue || uvScaleY.HasValue)
            {
                bakedScale = new
                {
                    X = uvScaleX ?? 1f,
                    Y = uvScaleY ?? 1f,
                };
            }

            // MaterialAlias is the per-part renamed material. The PSK importer reads this and
            // rewrites the part's submesh material-name keys so sibling parts that share an
            // upstream material name don't collide in the glTF/MDL material table (which would
            // pick the first-resolved part's textures and shade every sibling with them).
            var updated = new
            {
                Textures = textures,
                Parameters = parametersBlock,
                BakedUv0Scale = bakedScale,
                MaterialAlias = materialAlias,
            };
            File.WriteAllText(sidecarPath, JsonSerializer.Serialize(updated, jsonOptions));
        }
        catch
        {
            // Sidecar updates are best-effort; keep going on parse/IO failures.
        }
    }

    private static void WriteUvDiagnostic(
        UMaterialInterface material,
        int layerCount,
        List<TriangleUv> triangles,
        string outputDirectory)
    {
        try
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            float uv0MinX = float.MaxValue, uv0MaxX = float.MinValue;
            float uv0MinY = float.MaxValue, uv0MaxY = float.MinValue;
            float uv1MinX = float.MaxValue, uv1MaxX = float.MinValue;
            float uv1MinY = float.MaxValue, uv1MaxY = float.MinValue;
            // Histogram UV1.x to see where values cluster — tells us whether the layer-mask values
            // sit in [0,1], [0,2], or some other range.
            var uv1Bins = new int[10];
            int totalVerts = 0;
            foreach (var t in triangles)
            {
                foreach (var (u0, u1) in new[]
                {
                    (t.Uv0A, t.Uv1A),
                    (t.Uv0B, t.Uv1B),
                    (t.Uv0C, t.Uv1C),
                })
                {
                    uv0MinX = Math.Min(uv0MinX, u0.X); uv0MaxX = Math.Max(uv0MaxX, u0.X);
                    uv0MinY = Math.Min(uv0MinY, u0.Y); uv0MaxY = Math.Max(uv0MaxY, u0.Y);
                    uv1MinX = Math.Min(uv1MinX, u1.X); uv1MaxX = Math.Max(uv1MaxX, u1.X);
                    uv1MinY = Math.Min(uv1MinY, u1.Y); uv1MaxY = Math.Max(uv1MaxY, u1.Y);
                    var binIndex = (int)Math.Clamp(Math.Floor(u1.X * 2f), 0, uv1Bins.Length - 1);
                    uv1Bins[binIndex]++;
                    totalVerts++;
                }
            }

            var sb = new System.Text.StringBuilder();
            sb.Append("materialName=").AppendLine(material.Name);
            sb.Append("layerCount=").AppendLine(layerCount.ToString(inv));
            sb.Append("triangleCount=").AppendLine(triangles.Count.ToString(inv));
            sb.Append("vertexCount=").AppendLine(totalVerts.ToString(inv));
            sb.Append("uv0.x range=[").Append(uv0MinX.ToString("F3", inv)).Append(", ")
                .Append(uv0MaxX.ToString("F3", inv)).AppendLine("]");
            sb.Append("uv0.y range=[").Append(uv0MinY.ToString("F3", inv)).Append(", ")
                .Append(uv0MaxY.ToString("F3", inv)).AppendLine("]");
            sb.Append("uv1.x range=[").Append(uv1MinX.ToString("F3", inv)).Append(", ")
                .Append(uv1MaxX.ToString("F3", inv)).AppendLine("]");
            sb.Append("uv1.y range=[").Append(uv1MinY.ToString("F3", inv)).Append(", ")
                .Append(uv1MaxY.ToString("F3", inv)).AppendLine("]");
            sb.AppendLine("uv1.x histogram (bin width 0.5):");
            for (var i = 0; i < uv1Bins.Length; i++)
            {
                var low = (i * 0.5f).ToString("F1", inv);
                var high = ((i + 1) * 0.5f).ToString("F1", inv);
                sb.Append("  [").Append(low).Append(", ").Append(high).Append(") = ")
                    .AppendLine(uv1Bins[i].ToString(inv));
            }

            var logPath = Path.Combine(
                outputDirectory,
                NameHelpers.SanitizeMaterialName(material.Name) + ".uvdiag.log");
            File.WriteAllText(logPath, sb.ToString());
        }
        catch
        {
            // Diagnostics must never break export.
        }
    }

    private static void WriteBakeDiagnostic(
        UMaterialInterface material,
        CMaterialParams2 parameters,
        int layerCount,
        List<TriangleUv> triangles,
        string outputDirectory)
    {
        try
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.Append("materialName=").AppendLine(material.Name);
            sb.Append("layerCount=").AppendLine(layerCount.ToString(inv));

            sb.AppendLine("diffuseLayerSlots:");
            for (var i = 0; i < layerCount && i < CMaterialParams2.Diffuse.Length; i++)
            {
                var slotNames = string.Join("|", CMaterialParams2.Diffuse[i]);
                var hasTex = parameters.TryGetTexture2d(out var tex, CMaterialParams2.Diffuse[i]) && tex is UTexture2D;
                var texName = hasTex ? tex!.Name : "<none>";
                sb.Append("  layer[").Append(i.ToString(inv)).Append("] slots='").Append(slotNames)
                    .Append("' resolved='").Append(texName).AppendLine("'");
            }

            var layerHistogram = new int[layerCount + 1];
            foreach (var t in triangles)
            {
                var uv1x = (t.Uv1A.X + t.Uv1B.X + t.Uv1C.X) / 3f;
                var layer = Math.Clamp((int)MathF.Floor(uv1x), 0, layerCount);
                layerHistogram[layer]++;
            }

            sb.AppendLine("triangleLayerHistogram (by centroid floor(uv1.x)):");
            for (var i = 0; i < layerHistogram.Length; i++)
            {
                sb.Append("  layer[").Append(i.ToString(inv)).Append("] = ")
                    .AppendLine(layerHistogram[i].ToString(inv));
            }

            var logPath = Path.Combine(
                outputDirectory,
                NameHelpers.SanitizeMaterialName(material.Name) + ".bakediag.log");
            File.WriteAllText(logPath, sb.ToString());
        }
        catch
        {
            // Diagnostics must never break export.
        }
    }

    private static void DisposeAll(List<Image<Rgba32>> images)
    {
        foreach (var image in images)
        {
            image.Dispose();
        }
    }

    private readonly record struct TriangleUv(
        Vector2 Uv0A,
        Vector2 Uv0B,
        Vector2 Uv0C,
        Vector2 Uv1A,
        Vector2 Uv1B,
        Vector2 Uv1C);
}
