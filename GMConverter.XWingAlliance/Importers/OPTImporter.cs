using System.Numerics;
using GMConverter.SDK.Common;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Textures;
using JeremyAnsel.Xwa.Opt;
using Mesh = GMConverter.SDK.Geometry.Mesh;
using OptTexture = JeremyAnsel.Xwa.Opt.Texture;
using Texture = GMConverter.SDK.Textures.Texture;

#pragma warning disable CS8602, CS8604 // JeremyAnsel.Xwa.Opt exposes populated collections without nullable annotations.

namespace GMConverter.XWingAlliance.Importers;

internal sealed class OPTImporter : IImporter
{
    private readonly ITextureFactory _textureFactory;

    public OPTImporter(ITextureFactory textureFactory)
    {
        _textureFactory = textureFactory;
    }

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

    private static List<Mesh> BuildMeshes(OptFile opt, float lodDistance, float scaleFactor, ModelAxisMode axisMode)
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

    private Material[] BuildMaterials(OptFile opt)
    {
        return
        [
            .. opt.Textures.Values
            .Select(texture =>
            {
                var name = NameHelpers.SanitizeMaterialName(texture.Name);
                return new Material(
                    name,
                    diffuseTexture: CreateTexture(name, texture),
                    emissiveTexture: texture.IsIlluminated
                        ? CreateIlluminationTexture($"{name}_illum", texture)
                        : null);
            })
        ];
    }

    private Texture CreateTexture(string name, OptTexture texture)
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

        return CreateTextureFromBgra(name, converted.Width, converted.Height, converted.ImageData, texture.HasAlpha);
    }

    private Texture CreateIlluminationTexture(string name, OptTexture texture)
    {
        var illum = texture.GetIllumMap(0, out var width, out var height) ?? throw new GMConverterException($"Texture has no illumination map: {texture.Name}");
        var bgra = new byte[width * height * 4];

        for (var i = 0; i < width * height; i++)
        {
            var value = illum[i];
            bgra[i * 4 + 0] = value;
            bgra[i * 4 + 1] = value;
            bgra[i * 4 + 2] = value;
            bgra[i * 4 + 3] = 255;
        }

        return CreateTextureFromBgra(name, width, height, bgra, hasAlpha: false);
    }

    private Texture CreateTextureFromBgra(string name, int width, int height, byte[] bgra, bool hasAlpha)
    {
        if (width <= 0 || height <= 0)
        {
            throw new GMConverterException("Invalid texture dimensions.");
        }

        if (bgra.Length < width * height * 4)
        {
            throw new GMConverterException("Not enough texture image data.");
        }

        // Source is BGRA; the texture factory takes RGBA, so swap B and R.
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = bgra[i + 2];
            rgba[i + 1] = bgra[i + 1];
            rgba[i + 2] = bgra[i];
            rgba[i + 3] = bgra[i + 3];
        }

        return _textureFactory.FromRgba(name, width, height, rgba, hasAlpha);
    }
}
