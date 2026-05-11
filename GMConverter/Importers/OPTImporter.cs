using System.Globalization;
using System.Numerics;
using System.Text;
using ImageMagick;
using JeremyAnsel.Xwa.Opt;
using GMConverter.Common;
using GMConverter.Geometry;
using Mesh = GMConverter.Geometry.Mesh;
using OptTexture = JeremyAnsel.Xwa.Opt.Texture;
using Vector = JeremyAnsel.Xwa.Opt.Vector;

#pragma warning disable CS8602, CS8604 // JeremyAnsel.Xwa.Opt exposes populated collections without nullable annotations.

namespace GMConverter.Importers;

internal sealed class OPTImporter : IImporter
{
    public string InputFormat => "opt";

    public string InputName => "X-Wing Alliance";

    public object Summarize(string inputPath)
    {
        return OptSummary.From(inputPath, OptFile.FromFile(inputPath));
    }

    public Model Parse(string inputPath, ModelParseOptions options)
    {
        var opt = OptFile.FromFile(inputPath);
        var modelName = Path.GetFileNameWithoutExtension(inputPath);
        var lodDistance = OptHelpers.GetHighestLodDistance(opt);

        return new Model(
            modelName,
            BuildMeshes(opt, lodDistance, options.ScaleFactor, options.AxisMode),
            BuildMaterials(opt));
    }

    private static IReadOnlyList<Mesh> BuildMeshes(OptFile opt, float lodDistance, float scaleFactor, ModelAxisMode axisMode)
    {
        List<Mesh> meshes = [];

        foreach (var optMesh in opt.Meshes)
        {
            var lod = optMesh.Lods.FirstOrDefault(candidate => candidate.Distance <= lodDistance);

            if (lod is null)
            {
                continue;
            }

            List<Vertex> vertices = [];
            Dictionary<string, List<Triangle>> trianglesByMaterial = new(StringComparer.OrdinalIgnoreCase);

            foreach (var faceGroup in lod.FaceGroups)
            {
                var materialName = NameHelpers.SanitizeMaterialName(
                    NameHelpers.GetVersionedTextureName(faceGroup.Textures, 0) ?? "default");

                if (!trianglesByMaterial.TryGetValue(materialName, out var triangles))
                {
                    triangles = [];
                    trianglesByMaterial.Add(materialName, triangles);
                }

                foreach (var face in faceGroup.Faces)
                {
                    foreach (var sourceTriangle in FaceTriangulator.Triangulate(face))
                    {
                        var a = AddVertex(optMesh, sourceTriangle, 0, scaleFactor, axisMode, vertices);
                        var b = AddVertex(optMesh, sourceTriangle, 1, scaleFactor, axisMode, vertices);
                        var c = AddVertex(optMesh, sourceTriangle, 2, scaleFactor, axisMode, vertices);
                        triangles.Add(new Triangle(a, b, c));
                    }
                }
            }

            if (vertices.Count > 0)
            {
                meshes.Add(new Mesh(
                    vertices,
                    trianglesByMaterial
                        .Select(pair => new Submesh(pair.Key, pair.Value))
                        .ToArray()));
            }
        }

        return meshes;
    }

    private static int AddVertex(
        JeremyAnsel.Xwa.Opt.Mesh optMesh,
        TriangleIndices triangle,
        int corner,
        float scaleFactor,
        ModelAxisMode axisMode,
        List<Vertex> vertices)
    {
        var position = CoordinateTransforms.ToSource(optMesh.Vertices[triangle.Vertices[corner]], scaleFactor, axisMode);
        var normal = CoordinateTransforms.ToSourceNormal(optMesh.VertexNormals[triangle.Normals[corner]], axisMode);
        var textureCoordinate = optMesh.TextureCoordinates[triangle.TextureCoordinates[corner]];

        vertices.Add(new Vertex(position, normal, new Vector2(textureCoordinate.U, textureCoordinate.V)));
        return vertices.Count - 1;
    }

    private static IReadOnlyList<Material> BuildMaterials(OptFile opt)
    {
        return opt.Textures.Values
            .Select(texture =>
            {
                var name = NameHelpers.SanitizeMaterialName(texture.Name);
                return new Material(
                    name,
                    diffuseTexture: new Geometry.Texture(name, CreateTextureImage(texture), texture.HasAlpha),
                    emissiveTexture: texture.IsIlluminated
                        ? new Geometry.Texture($"{name}_illum", CreateIlluminationImage(texture))
                        : null);
            })
            .ToArray();
    }

    private static MagickImage CreateTextureImage(OptTexture texture)
    {
        var converted = texture.Clone();

        if (converted.BitsPerPixel == 8)
        {
            converted.Convert8To32(generateMipmaps: false);
        }

        if (converted.BitsPerPixel != 32 || converted.ImageData is null)
        {
            throw new GMConverterException($"Unsupported texture format for {texture.Name}.");
        }

        return CreateImageFromBgra(converted.Width, converted.Height, converted.ImageData);
    }

    private static MagickImage CreateIlluminationImage(OptTexture texture)
    {
        var illum = texture.GetIllumMap(0, out var width, out var height);

        if (illum is null)
        {
            throw new GMConverterException($"Texture has no illumination map: {texture.Name}");
        }

        var bgra = new byte[width * height * 4];

        for (var i = 0; i < width * height; i++)
        {
            var value = illum[i];
            bgra[i * 4 + 0] = value;
            bgra[i * 4 + 1] = value;
            bgra[i * 4 + 2] = value;
            bgra[i * 4 + 3] = 255;
        }

        return CreateImageFromBgra(width, height, bgra);
    }

    private static MagickImage CreateImageFromBgra(int width, int height, byte[] bgra)
    {
        if (width <= 0 || height <= 0)
        {
            throw new GMConverterException("Invalid texture dimensions.");
        }

        if (bgra.Length < width * height * 4)
        {
            throw new GMConverterException("Not enough texture image data.");
        }

        var settings = new PixelReadSettings((uint)width, (uint)height, StorageType.Char, "BGRA");
        return new MagickImage(bgra.AsSpan(), settings);
    }
}

internal sealed record OptSummary(
    string FilePath,
    int MeshCount,
    int LodCount,
    int TextureCount,
    int TextureVersionCount,
    int FaceCount,
    int VertexCount,
    Bounds SourceBounds,
    Bounds DisplayBounds,
    IReadOnlyList<float> LodDistances)
{
    public static OptSummary From(string inputPath, OptFile opt)
    {
        var lodCount = opt.Meshes.Sum(mesh => mesh.Lods.Count);
        var faceCount = opt.Meshes
            .SelectMany(mesh => mesh.Lods)
            .SelectMany(lod => lod.FaceGroups)
            .Sum(group => group.Faces.Count);
        var vertexCount = opt.Meshes.Sum(mesh => mesh.Vertices.Count);
        var distances = opt.Meshes
            .SelectMany(mesh => mesh.Lods)
            .Select(lod => lod.Distance)
            .Distinct()
            .OrderByDescending(distance => distance)
            .ToArray();

        return new OptSummary(
            inputPath,
            opt.Meshes.Count,
            lodCount,
            opt.Textures.Count,
            opt.MaxTextureVersion,
            faceCount,
            vertexCount,
            OptHelpers.GetBounds(opt, 1.0f),
            OptHelpers.GetBounds(opt, OptFile.ScaleFactor),
            distances);
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"File: {FilePath}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Meshes: {MeshCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"LODs: {LodCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Textures: {TextureCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Texture versions: {TextureVersionCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Faces: {FaceCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Vertices: {VertexCount}");
        AppendBounds(builder, "Source size at --scale 1", SourceBounds);
        AppendBounds(builder, "Library display size", DisplayBounds);

        if (LodDistances.Count > 0)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"LOD distances: {string.Join(", ", LodDistances)}");
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendBounds(StringBuilder builder, string label, Bounds bounds)
    {
        var sizeX = bounds.Max.X - bounds.Min.X;
        var sizeY = bounds.Max.Y - bounds.Min.Y;
        var sizeZ = bounds.Max.Z - bounds.Min.Z;
        var maxDimension = Math.Max(Math.Max(sizeX, sizeY), sizeZ);

        builder.AppendLine(CultureInfo.InvariantCulture,
            $"{label}: {sizeX:0.###} x {sizeY:0.###} x {sizeZ:0.###} Source units, max {maxDimension:0.###}");
    }
}

internal static class OptHelpers
{
    public static float GetHighestLodDistance(OptFile opt)
    {
        return opt.Meshes
            .SelectMany(mesh => mesh.Lods)
            .Select(lod => lod.Distance)
            .DefaultIfEmpty(0)
            .OrderByDescending(distance => distance)
            .First();
    }

    public static Bounds GetBounds(OptFile opt, float scaleFactor)
    {
        var hasVertex = false;
        float minX = 0;
        float minY = 0;
        float minZ = 0;
        float maxX = 0;
        float maxY = 0;
        float maxZ = 0;

        foreach (var mesh in opt.Meshes)
        {
            foreach (var vertex in mesh.Vertices)
            {
                var sourceVertex = CoordinateTransforms.ToSource(vertex, scaleFactor, ModelAxisMode.Auto);

                if (!hasVertex)
                {
                    minX = maxX = sourceVertex.X;
                    minY = maxY = sourceVertex.Y;
                    minZ = maxZ = sourceVertex.Z;
                    hasVertex = true;
                    continue;
                }

                minX = Math.Min(minX, sourceVertex.X);
                minY = Math.Min(minY, sourceVertex.Y);
                minZ = Math.Min(minZ, sourceVertex.Z);
                maxX = Math.Max(maxX, sourceVertex.X);
                maxY = Math.Max(maxY, sourceVertex.Y);
                maxZ = Math.Max(maxZ, sourceVertex.Z);
            }
        }

        if (!hasVertex)
        {
            throw new GMConverterException("Cannot convert an OPT with no vertices.");
        }

        return new Bounds(new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ));
    }
}

internal static class CoordinateTransforms
{
    public static Vector3 ToSource(Vector vector, float scaleFactor, ModelAxisMode axisMode)
    {
        var scaled = new Vector3(vector.X * scaleFactor, vector.Y * scaleFactor, vector.Z * scaleFactor);
        return ModelAxisTransforms.TransformPosition(scaled, axisMode, "opt");
    }

    public static Vector3 ToSourceNormal(Vector vector, ModelAxisMode axisMode)
    {
        return ModelAxisTransforms.TransformNormal(new Vector3(vector.X, vector.Y, vector.Z), axisMode, "opt");
    }
}

internal readonly record struct TriangleIndices(int[] Vertices, int[] TextureCoordinates, int[] Normals);

internal static class FaceTriangulator
{
    public static IEnumerable<TriangleIndices> Triangulate(Face face)
    {
        int[] vertices =
        [
            face.VerticesIndex.A,
            face.VerticesIndex.B,
            face.VerticesIndex.C,
            face.VerticesIndex.D
        ];
        int[] textureCoordinates =
        [
            face.TextureCoordinatesIndex.A,
            face.TextureCoordinatesIndex.B,
            face.TextureCoordinatesIndex.C,
            face.TextureCoordinatesIndex.D
        ];
        int[] normals =
        [
            face.VertexNormalsIndex.A,
            face.VertexNormalsIndex.B,
            face.VertexNormalsIndex.C,
            face.VertexNormalsIndex.D
        ];

        yield return new TriangleIndices(
            [vertices[0], vertices[1], vertices[2]],
            [textureCoordinates[0], textureCoordinates[1], textureCoordinates[2]],
            [normals[0], normals[1], normals[2]]);

        if (vertices[3] >= 0)
        {
            yield return new TriangleIndices(
                [vertices[0], vertices[2], vertices[3]],
                [textureCoordinates[0], textureCoordinates[2], textureCoordinates[3]],
                [normals[0], normals[2], normals[3]]);
        }
    }
}
