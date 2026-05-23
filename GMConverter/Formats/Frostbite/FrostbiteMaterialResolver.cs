using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Entries;
using GMConverter.Common;
using GMConverter.Geometry;

namespace GMConverter.Formats.Frostbite;

// Resolves textures referenced by a Frostbite mesh asset and builds Material instances bound to
// the submesh material names. Strategy is heuristic — we do not have TypeLibrary so we cannot
// read the mesh EBX's material/texture links directly. Instead we use Squadrons' on-disk path
// convention:
//
//   Mesh:     <dir>/meshes/<stem>_mesh
//   Textures: <dir>/textures/<{t_|tx_|<empty>}><stem>_<suffix>
//
// And classify by suffix into diffuse / normal / emissive slots. The first-found texture in each
// category is shared across every Material in the mesh — fine for most Squadrons props where all
// materials sample the same texture set, less accurate for multi-material assets where the
// engine actually picks different textures per material. Without TypeLibrary that distinction
// is invisible to us, and a shared-texture preview is materially better than no textures at all.
internal static class FrostbiteMaterialResolver
{
    // Suffix preference is descending: plain single-channel diffuses look right when bound as
    // glTF diffuse, packed multi-channel variants (`_cs` color+spec, `_nmo` normal+metal+occlusion,
    // `_nma` normal+metal+ao) misrender because their non-color channels collide with glTF's
    // alpha/normal-Z semantics. We pick the first hit in this order; if only packed variants
    // exist for normal slots we drop the normal binding entirely (clean flat shading beats a
    // mis-decoded normal map).
    private static readonly string[] _diffuseSuffixes = ["_c", "_bc", "_d", "_cs"];
    // Frostbite stores normals in several conventions:
    //   _n / _ns / _norm / _bump — plain XY-or-XYZ tangent normal in RG (BC5) or RGB (BC7)
    //   _nmo / _nom / _nmr / _nma — packed: normal in RG, mask channels in BA
    // glTF's PBR normal texture samples RGB as tangent-space XYZ; both conventions render close
    // enough for preview when the underlying BCn data is correctly decoded. We still try plain
    // suffixes first since they're closer to what glTF expects, but the packed variants are now
    // bound instead of skipped — previously they were dropped because our BC mapping was wrong
    // and the result was rainbow noise. Authoritative SDK ordinal→format mapping (see
    // FrostbiteTextureFormat) lets us decode both correctly.
    private static readonly string[] _normalSuffixes = ["_n", "_ns", "_norm", "_bump", "_nmo", "_nom", "_nma", "_nmr", "_nm"];
    private static readonly string[] _emissiveSuffixes = ["_emissive", "_e"];

    internal sealed record SubmeshKey(string Name, int MaterialId);

    public static IReadOnlyList<Material> Resolve(
        string meshAssetName,
        Guid meshGuid,
        IReadOnlyList<SubmeshKey> submeshes)
    {
        if (submeshes.Count == 0)
        {
            return [];
        }
        var sanitizedMaterialNames = submeshes.Select(s => s.Name).ToArray();

        // Authoritative path #1: walk each section's SurfaceShaderPreset EBX directly. This is
        // what FrostyEditor does (`material.Shader.TextureParameters`, FrostyMeshSetEditor.cs L540).
        // The SSP holds the default texture bindings per shader; MVDB only matters when you want
        // a cosmetic skin variation, and grabbing variation textures by accident is the bug we
        // were fighting (Luminous Being blue-tint on a default A-wing).
        var sspMaterials = ResolveFromShaderPresets(submeshes);
        if (sspMaterials is not null)
        {
            PerfTimer.Log("frostbite.material",
                $"SSP-resolved \"{meshAssetName}\": {sspMaterials.Count(m => m.DiffuseTexture is not null)} diffuse bind(s).");
            return sspMaterials;
        }

        // Authoritative path #2: MVDB. We fall back here when SSPs are empty or absent. Each
        // section's MaterialId is the index into MeshAsset.Materials, which has the same
        // ordering as MVDB.Materials — so submesh → MVDB[section.MaterialId].
        var mvdbBindings = Ebx.MvdbCache.Lookup(meshGuid);
        if (mvdbBindings.Count > 0)
        {
            PerfTimer.Log("frostbite.material",
                $"MVDB hit for \"{meshAssetName}\": {mvdbBindings.Count} material slot(s).");
            return ResolveFromMvdb(meshAssetName, mvdbBindings, submeshes);
        }

        var (textureDir, stem) = ExtractTextureSearchKey(meshAssetName);
        if (textureDir is null || stem is null)
        {
            return BuildBareMaterials(sanitizedMaterialNames);
        }

        // Collect candidates from every progressive-shortened stem so generic-skin textures (like
        // the A-wing's `t_veh_reb_hunt_awing_01_cs`) coexist with longer-stem-specific ones (like
        // `t_veh_reb_hunt_awing_cockpit_colorvariation_c`). The token-relevance filter below
        // discards the over-specific ones; without the multi-stem collection we'd never see the
        // generic option for a cockpit-mesh lookup.
        var stems = ShortenStem(stem);
        var collected = new Dictionary<string, EbxAssetEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidateStem in stems)
        {
            foreach (var hit in FindCandidateTextures(textureDir, candidateStem))
            {
                collected.TryAdd(hit.Name, hit);
            }
        }
        var candidates = collected.Values.ToList();
        if (candidates.Count == 0)
        {
            return BuildBareMaterials(sanitizedMaterialNames);
        }
        PerfTimer.Log("frostbite.material",
            $"Texture candidate pool for \"{meshAssetName}\": {candidates.Count} (stems tried: {string.Join(", ", stems)}).");

        // Token-relevance filter: when the stem shortened, candidates can include textures that
        // share the prefix but are scoped to an unrelated sub-asset (e.g. for an A-wing mainbody
        // mesh the candidate pool also contains every cockpit-specific texture). Reject any
        // texture whose distinguishing tokens (non-digits, after stripping `t_`/`tx_` prefix and
        // the suffix code) aren't all present in the mesh stem — that drops e.g. `_cockpit_` or
        // `_colorvariation_` textures when matching a mainbody mesh, leaving the generic
        // `t_awing_01_*` skin set as the picks.
        var meshTokens = new HashSet<string>(stem.Split('_'), StringComparer.OrdinalIgnoreCase);
        var relevant = candidates
            .Where(c => IsTokenRelevant(c.Name, textureDir, meshTokens))
            .ToList();
        if (relevant.Count > 0)
        {
            candidates = relevant;
        }

        var diffuse = DecodeFirstMatching(candidates, _diffuseSuffixes);
        var normal = DecodeFirstMatching(candidates, _normalSuffixes);
        var emissive = DecodeFirstMatching(candidates, _emissiveSuffixes);

        var materials = new List<Material>(sanitizedMaterialNames.Length);
        foreach (var name in sanitizedMaterialNames)
        {
            // Sub-materials with "Emissive" in the name are typically only the glowy bits — give
            // them the emissive texture as the diffuse so they aren't dark when no real diffuse
            // applies. Falls back to the shared diffuse otherwise.
            var isEmissiveMaterial = name.Contains("emissive", StringComparison.OrdinalIgnoreCase);
            var resolvedDiffuse = isEmissiveMaterial && emissive is not null ? emissive : diffuse;

            materials.Add(new Material(
                name,
                diffuseTexture: resolvedDiffuse,
                normalTexture: normal,
                emissiveTexture: emissive));
        }

        return materials;
    }

    // Builds Material instances from MVDB bindings. The MVDB gives us per-material-slot texture
    // bindings keyed by ParameterName (e.g. "BaseColor", "Normal", "Emissive"). We map those
    // names to GMConverter's Texture slots and decode each referenced texture.
    // Walks each section's SurfaceShaderPreset (SSP) EBX directly. The SSP is the authoritative
    // *default* texture binding for a mesh material — FrostyEditor reads it first and only falls
    // back to MeshVariationDatabase when the SSP has no TextureParameters (which is rare and
    // signals an asset that *only* exists as cosmetic skin variants). Returns null when any
    // section is unresolved so the caller can decide whether to fall back to MVDB.
    private static List<Material>? ResolveFromShaderPresets(
        IReadOnlyList<SubmeshKey> submeshes)
    {
        var textureCache = new Dictionary<Guid, Texture?>();
        Texture? DecodeOrCached(Guid guid)
        {
            if (textureCache.TryGetValue(guid, out var cached))
            {
                return cached;
            }
            var entry = AssetManager.GetEbxAssetEntry(guid);
            if (entry is null)
            {
                textureCache[guid] = null;
                return null;
            }
            try
            {
                var tex = DecodeTexture(entry);
                textureCache[guid] = tex;
                return tex;
            }
            catch (Exception ex) when (ex is GMConverterException or InvalidOperationException or ArgumentException)
            {
                PerfTimer.Log("frostbite.material", $"SSP-bound texture failed to decode: {entry.Name}: {ex.GetType().Name}: {ex.Message}");
                textureCache[guid] = null;
                return null;
            }
        }

        var materials = new List<Material>(submeshes.Count);
        var anyBound = false;
        foreach (var sub in submeshes)
        {
            if (string.IsNullOrWhiteSpace(sub.Name) || sub.Name.StartsWith("section_", StringComparison.OrdinalIgnoreCase))
            {
                materials.Add(new Material(sub.Name));
                continue;
            }

            var sspEntry = FindSspEntry(sub.Name);
            if (sspEntry is null)
            {
                materials.Add(new Material(sub.Name));
                continue;
            }

            IReadOnlyList<Ebx.MvdbTextureBinding> texParams;
            try
            {
                using var block = AssetManager.GetAsset(sspEntry);
                texParams = Ebx.SspWalker.ReadTextureParameters(block.ToArray());
            }
            catch (Exception ex) when (ex is GMConverterException or ArgumentException or IndexOutOfRangeException)
            {
                PerfTimer.Log("frostbite.material", $"SSP walk failed for \"{sspEntry.Name}\": {ex.GetType().Name}: {ex.Message}");
                materials.Add(new Material(sub.Name));
                continue;
            }

            if (texParams.Count == 0)
            {
                materials.Add(new Material(sub.Name));
                continue;
            }

            anyBound = true;
            Texture? diffuse = null;
            Texture? normal = null;
            Texture? emissive = null;
            foreach (var p in texParams)
            {
                if (p.ParameterName.Equals("BaseColor", StringComparison.OrdinalIgnoreCase))
                {
                    diffuse = DecodeOrCached(p.TexturePartitionGuid);
                }
                else if (p.ParameterName.Equals("Normal", StringComparison.OrdinalIgnoreCase) ||
                         p.ParameterName.Equals("NormalMap", StringComparison.OrdinalIgnoreCase))
                {
                    normal = DecodeNormalOrCached(p.TexturePartitionGuid);
                }
                else if (p.ParameterName.Contains("Emissive", StringComparison.OrdinalIgnoreCase) ||
                         p.ParameterName.Contains("Glow", StringComparison.OrdinalIgnoreCase))
                {
                    emissive = DecodeOrCached(p.TexturePartitionGuid);
                }
            }
            materials.Add(new Material(sub.Name, diffuseTexture: diffuse, normalTexture: normal, emissiveTexture: emissive));
        }
        return anyBound ? materials : null;

        // Local helper: decode a normal-slot texture, reconstructing Z from R/G if the source is
        // a Frostbite packed-NMA texture (R=Normal.X, G=Normal.Y, B=Metallic, A=AO). Without
        // this, glTF samples B as Normal.Z and gets the metallic channel — visible as
        // metallic-tinted shading drift on top of the diffuse.
        Texture? DecodeNormalOrCached(Guid guid)
        {
            var raw = DecodeOrCached(guid);
            if (raw is null)
            {
                return null;
            }
            if (!IsFrostbitePackedNormalName(raw.Name))
            {
                return raw;
            }
            return raw.WithFrostbiteNmaToNormal();
        }
    }

    // Frostbite packed-normal suffix detection. Plain `_n`/`_norm`/`_nm` are honest tangent
    // normals where RGB already encodes XYZ; the packed variants (`_nma`/`_nmo`/`_nmr`) overlay
    // Metallic/Occlusion/Roughness onto B and A. We only reconstruct Z for the packed variants.
    private static bool IsFrostbitePackedNormalName(string textureName)
    {
        return textureName.EndsWith("_nma", StringComparison.OrdinalIgnoreCase) ||
               textureName.EndsWith("_nmo", StringComparison.OrdinalIgnoreCase) ||
               textureName.EndsWith("_nmr", StringComparison.OrdinalIgnoreCase);
    }

    // Finds the SurfaceShaderPreset EBX entry whose name matches `materialName`. The MeshSet RES
    // section's MaterialName is the SSP's leaf name (e.g. "SSP_AWing_body"); EBX entry names are
    // full paths like "game/.../awing/textures/ssp_awing_body". We match by case-insensitive
    // suffix on `/<name>` to find the right entry. Caches results per mount.
    private static readonly Dictionary<string, Frosty.Sdk.Managers.Entries.EbxAssetEntry?> _sspCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static Frosty.Sdk.Managers.Entries.EbxAssetEntry? FindSspEntry(string materialName)
    {
        if (_sspCache.TryGetValue(materialName, out var cached))
        {
            return cached;
        }

        var suffix = "/" + materialName;
        Frosty.Sdk.Managers.Entries.EbxAssetEntry? found = null;
        foreach (var entry in AssetManager.EnumerateEbxAssetEntries())
        {
            if (!string.Equals(entry.Type, "SurfaceShaderPreset", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (entry.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                found = entry;
                break;
            }
        }
        _sspCache[materialName] = found;
        return found;
    }

    private static List<Material> ResolveFromMvdb(
        string meshAssetName,
        IReadOnlyList<Ebx.MvdbMaterialBinding> mvdbBindings,
        IReadOnlyList<SubmeshKey> submeshes)
    {
        // Decode each unique texture GUID once across the whole mesh; submeshes may reuse the
        // same texture asset for multiple parameter slots.
        var textureCache = new Dictionary<Guid, Texture?>();
        Texture? DecodeOrCached(Guid guid)
        {
            if (textureCache.TryGetValue(guid, out var cached))
            {
                return cached;
            }
            var entry = AssetManager.GetEbxAssetEntry(guid);
            if (entry is null)
            {
                textureCache[guid] = null;
                return null;
            }
            try
            {
                var tex = DecodeTexture(entry);
                textureCache[guid] = tex;
                return tex;
            }
            catch (Exception ex) when (ex is GMConverterException or InvalidOperationException or ArgumentException)
            {
                PerfTimer.Log("frostbite.material", $"MVDB-bound texture failed to decode: {entry.Name}: {ex.GetType().Name}: {ex.Message}");
                textureCache[guid] = null;
                return null;
            }
        }

        Material BuildFromBinding(Ebx.MvdbMaterialBinding binding, string materialName)
        {
            Texture? diffuse = null;
            Texture? normal = null;
            Texture? emissive = null;
            foreach (var t in binding.Textures)
            {
                if (t.ParameterName.Equals("BaseColor", StringComparison.OrdinalIgnoreCase))
                {
                    diffuse = DecodeOrCached(t.TexturePartitionGuid);
                }
                else if (t.ParameterName.Equals("Normal", StringComparison.OrdinalIgnoreCase) ||
                         t.ParameterName.Equals("NormalMap", StringComparison.OrdinalIgnoreCase))
                {
                    var rawNormal = DecodeOrCached(t.TexturePartitionGuid);
                    normal = rawNormal is not null && IsFrostbitePackedNormalName(rawNormal.Name)
                        ? rawNormal.WithFrostbiteNmaToNormal()
                        : rawNormal;
                }
                else if (t.ParameterName.Contains("Emissive", StringComparison.OrdinalIgnoreCase) ||
                         t.ParameterName.Contains("Glow", StringComparison.OrdinalIgnoreCase))
                {
                    emissive = DecodeOrCached(t.TexturePartitionGuid);
                }
            }
            return new Material(materialName, diffuseTexture: diffuse, normalTexture: normal, emissiveTexture: emissive);
        }

        // Each section's MaterialId indexes into MVDB.Materials directly (which mirrors the
        // MeshAsset's Materials list ordering). Some slots are empty proxies (glass / damage
        // decals) — those submeshes legitimately have no textures and render plain.
        PerfTimer.Log("frostbite.material",
            $"MVDB slot table for \"{meshAssetName}\": " +
            string.Join("; ", mvdbBindings.Select((b, i) =>
                $"[{i}] {b.Textures.Count}tex (" +
                string.Join(",", b.Textures.Select(t => t.ParameterName)) + ")")) +
            $"  →  submeshes=[{string.Join(",", submeshes.Select(s => $"{s.Name}(matId={s.MaterialId})"))}]");

        var materials = new List<Material>(submeshes.Count);
        foreach (var sub in submeshes)
        {
            if (sub.MaterialId >= 0 && sub.MaterialId < mvdbBindings.Count)
            {
                materials.Add(BuildFromBinding(mvdbBindings[sub.MaterialId], sub.Name));
            }
            else
            {
                materials.Add(new Material(sub.Name));
            }
        }

        _ = meshAssetName;
        return materials;
    }

    private static bool IsTokenRelevant(string textureAssetName, string textureDir, HashSet<string> meshTokens)
    {
        if (!textureAssetName.StartsWith(textureDir, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var file = textureAssetName[textureDir.Length..];
        // Strip the `t_` / `tx_` engine prefix.
        if (file.StartsWith("tx_", StringComparison.OrdinalIgnoreCase))
        {
            file = file[3..];
        }
        else if (file.StartsWith("t_", StringComparison.OrdinalIgnoreCase))
        {
            file = file[2..];
        }

        var tokens = file.Split('_');
        // Drop the last token: it's always the suffix code (`c`, `cs`, `n`, `nmo`, `e`, ...).
        if (tokens.Length <= 1)
        {
            return true;
        }
        var bodyTokens = tokens.AsSpan(0, tokens.Length - 1);

        foreach (var token in bodyTokens)
        {
            // Pure-digit tokens like "01" / "02" frequently appear in textures but not always in
            // the mesh; allow them through. Tokens that are purely alphabetic must appear in the
            // mesh's token set.
            if (token.Length == 0 || token.All(char.IsDigit))
            {
                continue;
            }
            if (!meshTokens.Contains(token))
            {
                return false;
            }
        }

        return true;
    }

    private static List<string> ShortenStem(string stem)
    {
        var stems = new List<string> { stem };
        var current = stem;
        while (true)
        {
            var lastUnderscore = current.LastIndexOf('_');
            if (lastUnderscore <= 0)
            {
                break;
            }
            current = current[..lastUnderscore];
            // Stop when the prefix becomes too short to be discriminating (≤ 1 token after split).
            if (!current.Contains('_'))
            {
                break;
            }
            stems.Add(current);
        }
        return stems;
    }

    private static (string? TextureDir, string? Stem) ExtractTextureSearchKey(string meshAssetName)
    {
        var meshesIdx = meshAssetName.LastIndexOf("/meshes/", StringComparison.OrdinalIgnoreCase);
        if (meshesIdx < 0)
        {
            return (null, null);
        }

        var prefix = meshAssetName[..meshesIdx];
        var stem = meshAssetName[(meshesIdx + "/meshes/".Length)..];
        if (stem.EndsWith("_mesh", StringComparison.OrdinalIgnoreCase))
        {
            stem = stem[..^"_mesh".Length];
        }

        return ($"{prefix}/textures/", stem);
    }

    private static List<EbxAssetEntry> FindCandidateTextures(string textureDir, string stem)
    {
        // Squadrons mixes naming styles: characters often use the raw stem, environment props use
        // a t_/tx_ prefix. Match all three to cover both cases.
        var prefixes = new[] { stem, $"t_{stem}", $"tx_{stem}" };
        var result = new List<EbxAssetEntry>();

        foreach (var entry in AssetManager.EnumerateEbxAssetEntries())
        {
            if (!string.Equals(entry.Type, "TextureAsset", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!entry.Name.StartsWith(textureDir, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var file = entry.Name[textureDir.Length..];
            if (prefixes.Any(p => file.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    private static Texture? DecodeFirstMatching(IReadOnlyList<EbxAssetEntry> candidates, string[] suffixes)
    {
        foreach (var suffix in suffixes)
        {
            var hit = candidates.FirstOrDefault(c => MatchesSuffix(c.Name, suffix));
            if (hit is null)
            {
                continue;
            }

            try
            {
                return DecodeTexture(hit);
            }
            catch (Exception ex) when (ex is GMConverterException or InvalidOperationException or ArgumentException)
            {
                // Swallow decode errors — log via perf timer so a single ordinal-mismatch doesn't
                // strip every texture from the mesh. The user still sees the geometry, just
                // without that particular slot's binding.
                PerfTimer.Log("frostbite.material", $"Skipped texture {hit.Name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return null;
    }

    private static bool MatchesSuffix(string assetName, string suffix)
    {
        // Match `<asset>_<suffix>` exactly — the texture's filename portion must END with the
        // suffix, with the suffix preceded by a non-suffix-letter (so `_c` matches "foo_c" but
        // not "foo_cs"). Suffix lookup table order handles `_cs` before `_c`.
        return assetName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static Texture DecodeTexture(EbxAssetEntry entry)
    {
        var res = AssetManager.GetResAssetEntry(entry.Name)
            ?? throw new GMConverterException($"No RES sidecar for texture asset {entry.Name}.");
        using var resBlock = AssetManager.GetAsset(res);
        var textureAsset = TextureAssetReader.Parse(resBlock.ToArray());

        var chunkEntry = AssetManager.GetChunkAssetEntry(textureAsset.ChunkId)
            ?? throw new GMConverterException($"Texture asset {entry.Name} references missing chunk {textureAsset.ChunkId}.");
        using var chunkBlock = AssetManager.GetAsset(chunkEntry);

        // Log every texture decode attempt; Detex AV crashes the process so the perf log's
        // last "PRE-DECODE" entry identifies the culprit when this is the failure mode.
        var format = FrostbiteTextureFormatResolver.Resolve(textureAsset.PixelFormatOrdinal);
        PerfTimer.Log("frostbite.texture",
            $"PRE-DECODE {entry.Name} ord={textureAsset.PixelFormatOrdinal} ({format}) " +
            $"{textureAsset.Width}x{textureAsset.Height} type={textureAsset.Type} " +
            $"depth={textureAsset.Depth} slices={textureAsset.SliceCount} " +
            $"firstMip={textureAsset.FirstMip} mip0Size={textureAsset.MipSizes[0]}");

        var image = TextureChunkDecoder.DecodeMip0(textureAsset, chunkBlock.ToArray());

        // Frostbite color textures with `_cs` packing put a spec mask in the alpha channel; glTF
        // treats diffuse alpha as transparency, so the prop ends up partially see-through. Force
        // alpha = 255 across the image to drop that interpretation. Plain diffuses (`_c`) are
        // already opaque so this is a no-op for them.
        ForceOpaque(image);

        var textureName = NameHelpers.SanitizeFileName(Path.GetFileName(entry.Name));

        // Temp diagnostic: write the decoded mip0 PNG to disk so we can visually verify whether
        // the source texture is a tile-pattern (UVs > 1 are expected to tile) or a unique layout
        // (UVs > 1 = decoder bug). Off by default; set GMCONVERTER_FROSTBITE_DUMP_TEX=1 to enable.
        if (Environment.GetEnvironmentVariable("GMCONVERTER_FROSTBITE_DUMP_TEX") is { Length: > 0 })
        {
            try
            {
                var dumpDir = Path.Combine(Path.GetTempPath(), "GMConverter.FrostbiteTex");
                Directory.CreateDirectory(dumpDir);
                var dumpPath = Path.Combine(dumpDir, textureName + ".png");
                SixLabors.ImageSharp.ImageExtensions.SaveAsPng(image, dumpPath);
                PerfTimer.Log("frostbite.texture", $"Dumped {dumpPath}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                PerfTimer.Log("frostbite.texture", $"Failed to dump texture: {ex.Message}");
            }
        }

        return new Texture(textureName, image);
    }

    private static void ForceOpaque(SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> image)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    pixel.A = 255;
                    row[x] = pixel;
                }
            }
        });
    }

    private static IReadOnlyList<Material> BuildBareMaterials(IReadOnlyCollection<string> names)
    {
        return [.. names.Select(name => new Material(name))];
    }
}
