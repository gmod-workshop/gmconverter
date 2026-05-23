using Frosty.Sdk.Managers;
using GMConverter.Common;

namespace GMConverter.Formats.Frostbite.Ebx;

// Lazy per-mount cache of MeshVariationDatabase bindings. On first lookup we scan every EBX
// entry whose type is "MeshVariationDatabase", walk it with MvdbWalker, and index every
// (meshGuid → list of material bindings) pair. Subsequent lookups by mesh GUID are O(1).
//
// Why eager-on-first-lookup instead of building during Mount: it's a couple of seconds of work
// against 858+ MVDB entries and most users never preview a mesh that needs it. Pay the cost
// once when the first vehicle/composite mesh is previewed.
internal static class MvdbCache
{
    private static readonly object _lock = new();
    private static bool _scanned;
    private static Dictionary<Guid, List<MvdbMaterialBinding>>? _byMesh;

    public static IReadOnlyList<MvdbMaterialBinding> Lookup(Guid meshGuid)
    {
        EnsureScanned();
        if (_byMesh!.TryGetValue(meshGuid, out var materials))
        {
            return materials;
        }
        return [];
    }

    public static void Reset()
    {
        lock (_lock)
        {
            _scanned = false;
            _byMesh = null;
        }
    }

    private static void EnsureScanned()
    {
        if (_scanned)
        {
            return;
        }
        lock (_lock)
        {
            if (_scanned)
            {
                return;
            }

            using var scope = PerfTimer.Measure("frostbite.mvdb", "Scan");
            var map = new Dictionary<Guid, (string Source, List<MvdbMaterialBinding> Materials)>();
            var mvdbCount = 0;
            var failures = 0;

            foreach (var entry in AssetManager.EnumerateEbxAssetEntries())
            {
                if (!string.Equals(entry.Type, "MeshVariationDatabase", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                mvdbCount++;

                try
                {
                    using var block = AssetManager.GetAsset(entry);
                    var bindings = MvdbWalker.Walk(block.ToArray());
                    foreach (var binding in bindings)
                    {
                        if (binding.MeshPartitionGuid == Guid.Empty)
                        {
                            continue;
                        }

                        // Multiple MVDB entries can reference the same mesh (one per material
                        // variation set — base skin vs Vanguard Squadron vs Luminous Being
                        // cosmetic, etc.). We want the *default* skin. Heuristic: prefer the
                        // MVDB whose path looks most generic (shortest path, or contains
                        // "MeshVariationDatabase" at root). Cosmetic variations live in deeper
                        // skin-pack folders. If no clear winner, keep the first seen.
                        var candidateMaterials = (List<MvdbMaterialBinding>)binding.Materials;
                        if (!map.TryGetValue(binding.MeshPartitionGuid, out var existing))
                        {
                            map[binding.MeshPartitionGuid] = (entry.Name, candidateMaterials);
                        }
                        else if (IsMorePreferredMvdb(entry.Name, existing.Source))
                        {
                            map[binding.MeshPartitionGuid] = (entry.Name, candidateMaterials);
                        }
                    }
                }
                catch (Exception ex) when (ex is GMConverterException or ArgumentException or IndexOutOfRangeException)
                {
                    failures++;
                    PerfTimer.Log("frostbite.mvdb", $"Skipped {entry.Name}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            _byMesh = map.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Materials);
            _scanned = true;
            PerfTimer.Log("frostbite.mvdb",
                $"Indexed {map.Count} mesh→materials mappings from {mvdbCount} MVDB entries ({failures} failures).");
        }
    }

    // Decides whether `candidate` MVDB EBX path is a more "default" / less cosmetic source than
    // `current`. Skin packs in Squadrons live under deeper paths like
    // `.../MeshVariationDatabases/LuminousBeing/...`, while the mesh's *own* MVDB sits next to
    // the mesh at e.g. `.../awing/Veh_Reb_Hunt_AWing_MeshVariationDatabase`. We prefer:
    //   1. MVDBs that don't reference known cosmetic-skin keywords in their path
    //   2. Among those, the shorter path (closer to the mesh)
    private static readonly string[] _cosmeticSkinHints =
    [
        "LuminousBeing", "VanguardSquadron", "TitanSquadron",
        "Skin", "Variant", "Variation", "Cosmetic", "Holographic"
    ];

    private static bool IsMorePreferredMvdb(string candidate, string current)
    {
        var candidateIsCosmetic = ContainsAnyHint(candidate);
        var currentIsCosmetic = ContainsAnyHint(current);
        if (candidateIsCosmetic != currentIsCosmetic)
        {
            return !candidateIsCosmetic;
        }
        return candidate.Length < current.Length;
    }

    private static bool ContainsAnyHint(string path)
    {
        foreach (var hint in _cosmeticSkinHints)
        {
            if (path.Contains(hint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
