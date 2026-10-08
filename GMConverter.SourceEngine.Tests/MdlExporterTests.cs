using System.Globalization;
using System.Numerics;
using GMConverter.Plugins;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Materials;
using GMConverter.SourceEngine.Exporters;

namespace GMConverter.SourceEngine.Tests;

public sealed class MdlExporterTests
{
    [Fact]
    public void SourceCollisionThicknessPreservesSmallModelsAndPadsFlatAxes()
    {
        var bounds = new Bounds(new Vector3(0, 0, 2), new Vector3(0.4f, 0.8f, 2));
        var collision = bounds.WithMinimumThickness(0.0254f);
        Assert.Equal(bounds.Min.X, collision.Min.X);
        Assert.Equal(bounds.Max.X, collision.Max.X);
        Assert.Equal(bounds.Min.Y, collision.Min.Y);
        Assert.Equal(bounds.Max.Y, collision.Max.Y);
        Assert.Equal(0.0254f, collision.Max.Z - collision.Min.Z, 5);
        Assert.Equal(2f, (collision.Min.Z + collision.Max.Z) / 2f, 5);
    }

    [Fact]
    public void SmdEulerAnglesKeepRotationAtNinetyDegreePitch()
    {
        // A bone pitched straight up is gimbal locked: roll and yaw spin about the same axis, so
        // the SMD angles must fold the whole spin into one of them rather than emit atan2 noise.
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 6f) *
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        var model = CreateSkinnedTriangle(rotation);

        var smd = File.ReadAllLines(Directory.GetFiles(ExportWithStubCompiler(model), "triangle.smd", SearchOption.AllDirectories).Single());
        var bone = smd[Array.IndexOf(smd, "time 0") + 1].Split(' ').Select(value => float.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        // Source's AngleMatrix composes roll about X, then pitch about Y, then yaw about Z.
        var actual = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, bone[6]) *
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, bone[5]) *
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, bone[4]);
        Assert.Equal(1f, MathF.Abs(Quaternion.Dot(Quaternion.Normalize(rotation), actual)), 4);
    }

    // One triangle 1 m across, fully weighted to a single root bone with the given rotation.
    private static Model CreateSkinnedTriangle(Quaternion rootRotation)
    {
        VertexBoneWeight[] weights = [new(0, 1f)];
        Vertex[] vertices =
        [
            new(new Vector3(0.1f, 0.2f, 0.3f), Vector3.UnitZ, Vector2.Zero, weights),
            new(new Vector3(1.1f, 0.2f, 0.3f), Vector3.UnitZ, Vector2.UnitX, weights),
            new(new Vector3(0.1f, 1.2f, 0.3f), Vector3.UnitZ, Vector2.UnitY, weights),
        ];
        var mesh = new Mesh(vertices, [new Submesh("test", [new Triangle(0, 1, 2)])]);
        var skeleton = new Skeleton([new Bone(0, "root", -1, new Transform(new Vector3(0.25f, 0, 0), rootRotation, Vector3.One))]);
        return new Model("triangle", [mesh], [new Material("test")], skeleton);
    }

    // buildMaterials=false makes the exporter write the SMD/QC workspace itself before studiomdl
    // runs; the empty stub compiler then fails, which is irrelevant to the files under test.
    private static string ExportWithStubCompiler(Model model)
    {
        var directory = Path.Join(Path.GetTempPath(), "GMConverter.SourceEngine.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var output = Path.Join(directory, "mdl");
        var stubStudioMdl = Path.Join(directory, "cestudiomdl.exe");
        File.WriteAllText(stubStudioMdl, string.Empty);
        _ = Record.Exception(() => new MDLExporter(new DefaultTextureFactory()).Export(model, output, model.Name, new ExportOptions(
            new Dictionary<string, object?>
            {
                ["studioMdlPath"] = stubStudioMdl,
                ["buildMaterials"] = false,
            })));
        return output;
    }
}
