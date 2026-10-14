using System.Numerics;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Materials;

namespace GMConverter.SourceEngine.Materials;

/// <summary>
/// Moves the solid parts of alpha-blended materials onto an opaque copy. A <c>$translucent</c>
/// material writes no depth and its triangles aren't sorted, so in a bottle whose glass, cork and
/// label share one texture, the inside and far side of the glass draw over the cork. Triangles
/// whose texels are all opaque go to <c>&lt;name&gt;_opaque</c>, which Source draws in the opaque
/// pass; the rest keep the original material.
/// </summary>
internal static class SourceTranslucencySplit
{
    // Texels at or above this alpha count as solid; compression leaves solid areas at 254-255.
    private const byte _solidAlpha = 250;

    // Samples per triangle edge are capped so huge triangles stay cheap.
    private const int _maxSamplesPerEdge = 64;

    public static Model Split(Model model)
    {
        var skinned = model.Skins?.SelectMany(skin => skin.Replacements.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = model.Materials
            .Where(material => IsSplittable(material) && skinned?.Contains(material.Name) != true)
            .ToDictionary(material => material.Name, StringComparer.OrdinalIgnoreCase);
        if (candidates.Count == 0)
        {
            return model;
        }

        Dictionary<string, AlphaMap> alphaMaps = new(StringComparer.OrdinalIgnoreCase);
        var solid = new bool[model.Meshes.Count][][];
        Dictionary<string, (int Solid, int Blended)> counts = new(StringComparer.OrdinalIgnoreCase);
        for (var meshIndex = 0; meshIndex < model.Meshes.Count; meshIndex++)
        {
            var mesh = model.Meshes[meshIndex];
            solid[meshIndex] = new bool[mesh.Submeshes.Count][];
            for (var submeshIndex = 0; submeshIndex < mesh.Submeshes.Count; submeshIndex++)
            {
                var submesh = mesh.Submeshes[submeshIndex];
                if (submesh.MaterialName is not { } name || !candidates.TryGetValue(name, out var material))
                {
                    continue;
                }

                if (!alphaMaps.TryGetValue(name, out var alpha))
                {
                    alpha = AlphaMap.From(material);
                    alphaMaps[name] = alpha;
                }

                var flags = submesh.Triangles.Select(triangle => alpha.IsSolid(mesh, triangle)).ToArray();
                solid[meshIndex][submeshIndex] = flags;
                var solidCount = flags.Count(flag => flag);
                var (totalSolid, totalBlended) = counts.GetValueOrDefault(name);
                counts[name] = (totalSolid + solidCount, totalBlended + flags.Length - solidCount);
            }
        }

        HashSet<string> used = new(model.Materials.Select(material => material.Name), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> opaqueNames = new(StringComparer.OrdinalIgnoreCase);
        List<Material> materials = [];
        foreach (var material in model.Materials)
        {
            if (!counts.TryGetValue(material.Name, out var count) || count.Solid == 0)
            {
                materials.Add(material);
                continue;
            }

            // A material with no blended triangles left becomes opaque under its own name.
            if (count.Blended == 0)
            {
                opaqueNames[material.Name] = material.Name;
                materials.Add(material.WithBlendMode(material.Name, MaterialBlendMode.Opaque));
                continue;
            }

            var opaqueName = $"{material.Name}_opaque";
            for (var suffix = 2; !used.Add(opaqueName); suffix++)
            {
                opaqueName = $"{material.Name}_opaque_{suffix}";
            }

            opaqueNames[material.Name] = opaqueName;
            materials.Add(material);
            materials.Add(material.WithBlendMode(opaqueName, MaterialBlendMode.Opaque));
        }

        if (opaqueNames.Count == 0)
        {
            return model;
        }

        var meshes = model.Meshes.Select((mesh, meshIndex) =>
        {
            List<Submesh> submeshes = [];
            for (var submeshIndex = 0; submeshIndex < mesh.Submeshes.Count; submeshIndex++)
            {
                var submesh = mesh.Submeshes[submeshIndex];
                if (submesh.MaterialName is not { } name || !opaqueNames.TryGetValue(name, out var opaqueName) || solid[meshIndex][submeshIndex] is not { } flags)
                {
                    submeshes.Add(submesh);
                    continue;
                }

                var blended = submesh.Triangles.Where((_, index) => !flags[index]).ToArray();
                var opaque = submesh.Triangles.Where((_, index) => flags[index]).ToArray();
                if (blended.Length > 0)
                {
                    submeshes.Add(submesh with { Triangles = blended });
                }

                if (opaque.Length > 0)
                {
                    submeshes.Add(new Submesh(opaqueName, opaque));
                }
            }

            return mesh with { Submeshes = submeshes };
        }).ToArray();

        return model with { Materials = materials, Meshes = meshes };
    }

    private static bool IsSplittable(Material material)
    {
        var blended = material.BlendMode is MaterialBlendMode.AlphaBlend ||
                      (material.BlendMode is MaterialBlendMode.Unspecified && material.HasAlpha);

        // Moving coordinates would carry blended texels onto the opaque triangles.
        return blended &&
               material.DiffuseTexture is { HasAlpha: true } &&
               material.UvScrollRate is null &&
               material.UvTransform is null;
    }

    /// <summary>The diffuse alpha of a material, sampled at the mesh's texture coordinates.</summary>
    private sealed class AlphaMap(byte[] alpha, int width, int height, Vector2 uvScale)
    {
        public static AlphaMap From(Material material)
        {
            var texture = material.DiffuseTexture!;
            var pixels = texture.GetRgbaPixels();
            var alpha = new byte[texture.Width * texture.Height];
            for (var index = 0; index < alpha.Length; index++)
            {
                alpha[index] = pixels[(index * 4) + 3];
            }

            return new AlphaMap(alpha, texture.Width, texture.Height, material.BakedUv0Scale ?? Vector2.One);
        }

        public bool IsSolid(Mesh mesh, Triangle triangle)
        {
            var a = ToTexels(mesh.Vertices[triangle.A].TextureCoordinate);
            var b = ToTexels(mesh.Vertices[triangle.B].TextureCoordinate);
            var c = ToTexels(mesh.Vertices[triangle.C].TextureCoordinate);
            var longestEdge = MathF.Max(Vector2.Distance(a, b), MathF.Max(Vector2.Distance(b, c), Vector2.Distance(c, a)));
            var steps = Math.Clamp((int)MathF.Ceiling(longestEdge), 1, _maxSamplesPerEdge);
            for (var i = 0; i <= steps; i++)
            {
                for (var j = 0; j <= steps - i; j++)
                {
                    var point = ((a * i) + (b * j) + (c * (steps - i - j))) / steps;
                    if (Sample(point) < _solidAlpha)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // Texture coordinates have V up from the bottom of an image stored top row first.
        private Vector2 ToTexels(Vector2 uv)
        {
            uv *= uvScale;
            return new Vector2(uv.X * width, (1f - uv.Y) * height);
        }

        private byte Sample(Vector2 texel)
        {
            var x = Wrap((int)MathF.Floor(texel.X), width);
            var y = Wrap((int)MathF.Floor(texel.Y), height);
            return alpha[(y * width) + x];
        }

        private static int Wrap(int value, int size)
        {
            var wrapped = value % size;
            return wrapped < 0 ? wrapped + size : wrapped;
        }
    }
}
