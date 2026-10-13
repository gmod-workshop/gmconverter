using System.Globalization;
using System.Numerics;
using GMConverter.Core.Textures;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Options;
using GMConverter.SDK.Physics;
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

    [Fact]
    public void CollisionSmdSharesTheReferenceSkeleton()
    {
        // A root bone turned 180 degrees, like Men of War's "basis" bone.
        var model = CreateSkinnedTriangle(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI));

        var output = ExportWithStubCompiler(model, new Dictionary<string, object?> { ["physics:enabled"] = true });

        string[] Header(string name)
        {
            var lines = File.ReadAllLines(Directory.GetFiles(output, name, SearchOption.AllDirectories).Single());
            return lines[..Array.IndexOf(lines, "triangles")];
        }
        Assert.Equal(Header("triangle.smd"), Header("triangle_phys.smd"));
    }

    [Fact]
    public void CollisionPreviewFollowsThePhysicsOptions()
    {
        var exporter = new MDLExporter(new DefaultTextureFactory());
        var model = CreateSkinnedTriangle(Quaternion.Identity);

        Assert.Empty(exporter.CreateCollisionPreview(model, OptionValues.Empty));

        var bounds = Assert.Single(exporter.CreateCollisionPreview(model, new OptionValues(
            new Dictionary<string, object?> { ["physics:enabled"] = true })));
        Assert.Equal(12, bounds.Triangles.Count());
        // The flat triangle is padded to Source's one-inch minimum collision thickness.
        var zs = bounds.Vertices.Select(vertex => vertex.Position.Z).ToArray();
        Assert.Equal(0.0254f, zs.Max() - zs.Min(), 4);
    }

    [Fact]
    public void LayeredClipsBecomeDeltaSequencesAgainstTheirBasePose()
    {
        var basePose = new AnimationPose("idle_0", new Dictionary<int, Transform> { [0] = Transform.Identity });
        AnimationClip Clip(string name, AnimationLayering? layering) => new(
            name, 30f, 0f, [new BoneTransformTrack(0, [new TransformKeyframe(0f, Transform.Identity)])], layering);
        var model = CreateSkinnedTriangle(Quaternion.Identity) with
        {
            Animations =
            [
                Clip("walk", null),
                Clip("aim_up", new AnimationLayering(LayerSpace.Model, basePose)),
                Clip("aim_down", new AnimationLayering(LayerSpace.Model, basePose)),
                Clip("flinch", new AnimationLayering(LayerSpace.Local)),
            ],
        };

        var output = ExportWithStubCompiler(model);

        var qc = File.ReadAllText(Directory.GetFiles(output, "triangle.qc", SearchOption.AllDirectories).Single());
        var baseAnimation = Assert.Single(qc.Split('\n'), line => line.StartsWith("$animation", StringComparison.Ordinal));
        Assert.Contains("triangle_base0_idle_0", baseAnimation);
        Assert.Single(Directory.GetFiles(output, "triangle_base0_idle_0.smd", SearchOption.AllDirectories));
        Assert.Equal(2, qc.Split("subtract \"triangle_base0_idle_0\" 0").Length - 1);
        Assert.Contains("subtract \"idle\" 0", qc);
        Assert.Equal(3, qc.Split("    delta").Length - 1);
        Assert.Contains("$sequence \"walk\" \"triangle_0_walk.smd\" fps 30", qc);
    }

    [Fact]
    public void RootMotionIsExtractedAsSequenceMovement()
    {
        // One second at 8 fps. The walk loops; the turn doesn't, and its root turns 90 degrees.
        AnimationClip Clip(string name, Func<float, Transform> root, bool rootMotion, bool loops, AnimationLayering? layering = null) => new(
            name,
            8f,
            1f,
            [new BoneTransformTrack(0, [.. Enumerable.Range(0, 9).Select(frame => new TransformKeyframe(frame / 8f, root(frame / 8f)))])],
            layering,
            rootMotion,
            loops);
        Transform Walk(float time) => new(new Vector3(0.25f + time, 0, 0), Quaternion.Identity, Vector3.One);
        Transform Turn(float time) => new(new Vector3(0.25f, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, time * MathF.PI / 2), Vector3.One);
        var model = CreateSkinnedTriangle(Quaternion.Identity) with
        {
            Animations =
            [
                Clip("walk", Walk, rootMotion: true, loops: true),
                Clip("turn", Turn, rootMotion: true, loops: false),
                Clip("idle_loop", Walk, rootMotion: false, loops: true),
                Clip("lean", Walk, rootMotion: true, loops: false, new AnimationLayering(LayerSpace.Local)),
            ],
        };

        var qc = File.ReadAllText(Directory.GetFiles(ExportWithStubCompiler(model), "triangle.qc", SearchOption.AllDirectories).Single()).Replace("\r", "");

        Assert.Contains("$sequence \"walk\" {\n    \"triangle_0_walk.smd\"\n    fps 8\n    LX LY\n    loop\n}", qc);
        // Quarter-second pieces keep a start or stop's change of speed.
        Assert.Contains("$sequence \"turn\" {\n    \"triangle_1_turn.smd\"\n    fps 8\n    walkframe 2 LX LY LZR\n    walkframe 4 LX LY LZR\n    walkframe 6 LX LY LZR\n    walkframe 8 LX LY LZR\n}", qc);
        Assert.Contains("$sequence \"idle_loop\" {\n    \"triangle_2_idle_loop.smd\"\n    fps 8\n    loop\n}", qc);
        // Layers never move the character.
        Assert.Contains("    subtract \"idle\" 0\n    delta\n}", qc);
        Assert.DoesNotContain("walkframe", qc[qc.IndexOf("\"lean\"", StringComparison.Ordinal)..]);
    }

    [Fact]
    public void RootMotionClipsStartOnTheBindPose()
    {
        // Like an Unreal stop: the root starts 1 m away and turned 90 degrees, and ends on its bind
        // pose. Extraction only removes motion from the first frame on, so the first frame is
        // moved onto the bind pose and the rest follows it.
        var stop = new AnimationClip(
            "stop",
            8f,
            1f,
            [
                new BoneTransformTrack(0,
                [
                    new TransformKeyframe(0f, new Transform(new Vector3(1.25f, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2), Vector3.One)),
                    new TransformKeyframe(1f, new Transform(new Vector3(0.25f, 0, 0), Quaternion.Identity, Vector3.One)),
                ]),
            ],
            RootMotion: true);
        var model = CreateSkinnedTriangle(Quaternion.Identity) with { Animations = [stop] };

        var smd = File.ReadAllLines(Directory.GetFiles(ExportWithStubCompiler(model), "triangle_0_stop.smd", SearchOption.AllDirectories).Single());

        float[] Root(int frame) => [.. smd[Array.IndexOf(smd, FormattableString.Invariant($"time {frame}")) + 1]
            .Split(' ')[1..]
            .Select(value => float.Parse(value, CultureInfo.InvariantCulture))];
        void AssertRoot(float[] expected, int frame) => Assert.All(expected.Zip(Root(frame)), pair => Assert.Equal(pair.First, pair.Second, 3));
        AssertRoot([9.84252f, 0, 0, 0, 0, 0], 0);
        // It moved 1 m to its own left (world -X while it faced +Y) and turned 90 degrees right;
        // from the bind facing, that is 1 m along +Y.
        AssertRoot([9.84252f, 39.37008f, 0, 0, 0, -MathF.PI / 2], 8);
    }

    [Fact]
    public void AttachmentsAreWrittenRelativeToTheirSourceBone()
    {
        // The root is scaled 2x; Source bones carry no scale, so the attachment's offset from the
        // bone doubles. A model-space attachment lands on the root as seen from the origin.
        var model = CreateSkinnedTriangle(Quaternion.Identity) with
        {
            Attachments =
            [
                new Attachment("muzzle", 0, new Transform(new Vector3(0, 0, 1), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2), Vector3.One)),
                new Attachment("origin", null, Transform.Identity),
            ],
        };
        model = model with { Skeleton = new Skeleton([model.Skeleton!.Bones[0] with { LocalBindPose = model.Skeleton.Bones[0].LocalBindPose with { Scale = new Vector3(2) } }]) };

        var qc = File.ReadAllLines(Directory.GetFiles(ExportWithStubCompiler(model), "triangle.qc", SearchOption.AllDirectories).Single());

        Assert.Contains("$attachment \"muzzle\" \"root\" 0 0 78.7402 rotate 0 90 0", qc);
        Assert.Contains("$attachment \"origin\" \"root\" -9.8425 0 0 rotate 0 0 0", qc);
    }

    [Fact]
    public void PhysicsBodiesBecomeARagdollWithJointLimitsAndHitboxes()
    {
        var model = CreateRagdoll();

        var output = ExportWithStubCompiler(model, new Dictionary<string, object?> { ["physics:enabled"] = true });

        var qc = File.ReadAllLines(Directory.GetFiles(output, "triangle.qc", SearchOption.AllDirectories).Single());
        Assert.Contains("$collisionjoints \"triangle_phys.smd\"", qc);
        Assert.DoesNotContain(qc, line => line.StartsWith("$collisionmodel", StringComparison.Ordinal));
        // The joint frame is turned 90 degrees about Z from the head bone: its X limit lands on
        // the bone's Y axis, and its Y limit on the bone's X axis, reversed because they point
        // opposite ways.
        Assert.Contains("    $jointconstrain \"head\" x limit -20 5 0", qc);
        Assert.Contains("    $jointconstrain \"head\" y limit -10 10 0", qc);
        Assert.Contains("    $jointconstrain \"head\" z limit 0 0 0", qc);
        // Hitboxes are in each bone's own space: a 0.1 m box and a 0.1 m sphere.
        Assert.Contains("$hbox 0 \"root\" -3.937 -3.937 -3.937 3.937 3.937 3.937", qc);
        Assert.Contains("$hbox 1 \"head\" -3.937 -3.937 -3.937 3.937 3.937 3.937", qc);

        // Each hull is bound to its body's bone.
        var physicsSmd = File.ReadAllLines(Directory.GetFiles(output, "triangle_phys.smd", SearchOption.AllDirectories).Single());
        var vertexBones = physicsSmd[(Array.IndexOf(physicsSmd, "triangles") + 1)..^1]
            .Where(line => line != "physics")
            .Select(line => line.Split(' ')[0])
            .ToHashSet();
        Assert.Equal(["0", "1"], vertexBones.Order());
    }

    [Fact]
    public void BoundsPhysicsModeIgnoresTheModelsBodies()
    {
        var output = ExportWithStubCompiler(CreateRagdoll(), new Dictionary<string, object?>
        {
            ["physics:enabled"] = true,
            ["physics:mode"] = "bounds",
        });

        var qc = File.ReadAllLines(Directory.GetFiles(output, "triangle.qc", SearchOption.AllDirectories).Single());
        Assert.Contains("$collisionmodel \"triangle_phys.smd\"", qc);
        Assert.DoesNotContain(qc, line => line.StartsWith("$collisionjoints", StringComparison.Ordinal));
    }

    [Fact]
    public void CollisionPreviewShowsEachRagdollShape()
    {
        var preview = new MDLExporter(new DefaultTextureFactory()).CreateCollisionPreview(
            CreateRagdoll(),
            new OptionValues(new Dictionary<string, object?> { ["physics:enabled"] = true }));

        Assert.Equal(2, preview.Count);
        Assert.Equal(12, preview[0].Triangles.Count());
    }

    [Fact]
    public void LegacyCliFlagsAndDefaultsAreDeclaredOnTheSchema()
    {
        var schema = new MDLExporter(new DefaultTextureFactory()).OptionSchema;

        Assert.Contains("no-materials", schema.Find("buildMaterials")!.Aliases);
        Assert.Contains("physics", schema.Find("physics:enabled")!.Aliases);
        Assert.Contains("coacd-max-hull-vertices", schema.Find("physics:maxHullVertices")!.Aliases);
        Assert.Equal(16, schema.Find("physics:maxConvexPieces")!.DefaultValue);
        Assert.Equal(-1m, schema.Find("physics:maxConvexPieces")!.Minimum);
    }

    [Fact]
    public void SequencesGetTheTargetGamesActivities()
    {
        AnimationClip Clip(string name, AnimationRole? role, AnimationLayering? layering = null) => new(
            name,
            30f,
            1f,
            [new BoneTransformTrack(0, [new TransformKeyframe(0f, Transform.Identity), new TransformKeyframe(1f, Transform.Identity)])],
            layering,
            Role: role);
        var model = CreateSkinnedTriangle(Quaternion.Identity) with
        {
            Animations =
            [
                Clip("run", new AnimationRole(AnimationAction.Run, WeaponStance.Rifle, MoveDirection.Forward)),
                Clip("strafe", new AnimationRole(AnimationAction.Run, WeaponStance.Rifle, MoveDirection.Left)),
                Clip("die", new AnimationRole(AnimationAction.Death)),
                Clip("flinch", new AnimationRole(AnimationAction.Flinch), new AnimationLayering(LayerSpace.Local)),
                Clip("wave", null),
            ],
        };
        string Qc(string? target, string? use = null) => File.ReadAllText(Directory.GetFiles(
            ExportWithStubCompiler(model, targetGameId: target, targetOptions: use is null ? null : new Dictionary<string, object?> { ["characterUse"] = use }),
            "*.qc", SearchOption.AllDirectories).Single()).ReplaceLineEndings("\n");

        var npc = Qc("garrysmod");
        Assert.Contains("$sequence \"run\" {\n    \"triangle_0_run.smd\"\n    fps 30\n    activity ACT_RUN 1\n}", npc);
        Assert.Contains("activity ACT_DIESIMPLE 1", npc);
        // Strafes, layers and clips without a role are written without an activity.
        Assert.Equal(2, npc.Split("activity ").Length - 1);
        Assert.DoesNotContain("_player", npc);

        var both = Qc("garrysmod", "both");
        Assert.Contains("$sequence \"run_player\" {\n    \"triangle_0_run.smd\"\n    fps 30\n    activity ACT_HL2MP_RUN_AR2 1\n}", both);
        Assert.Contains("activity ACT_RUN 1", both);

        var player = Qc("garrysmod", "player");
        Assert.Contains("$sequence \"run\" {\n    \"triangle_0_run.smd\"\n    fps 30\n    activity ACT_HL2MP_RUN_AR2 1\n}", player);
        Assert.DoesNotContain("ACT_RUN ", player);

        Assert.Equal(2, Qc("source").Split("activity ").Length - 1);
        Assert.DoesNotContain("activity", Qc(null));
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

    // The skinned triangle with a "head" bone 1 m above the root, a box body on the root, a sphere
    // body on the head, and a joint between them.
    private static Model CreateRagdoll()
    {
        var model = CreateSkinnedTriangle(Quaternion.Identity);
        var head = new Bone(1, "head", 0, new Transform(Vector3.UnitZ, Quaternion.Identity, Vector3.One));
        return model with
        {
            Skeleton = new Skeleton([model.Skeleton!.Bones[0], head]),
            Physics = new ModelPhysics(
                [
                    new PhysicsBody(0, [new BoxShape(Transform.Identity, new Vector3(0.1f))]),
                    new PhysicsBody(1, [new SphereShape(Transform.Identity, 0.1f)]),
                ],
                [
                    new PhysicsJoint(
                        1,
                        0,
                        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2),
                        new AngularLimit(-10, 10),
                        new AngularLimit(-5, 20),
                        new AngularLimit(0, 0)),
                ]),
        };
    }

    // buildMaterials=false makes the exporter write the SMD/QC workspace itself before studiomdl
    // runs; the empty stub compiler then fails, which is irrelevant to the files under test.
    private static string ExportWithStubCompiler(
        Model model,
        IReadOnlyDictionary<string, object?>? extraOptions = null,
        string? targetGameId = null,
        IReadOnlyDictionary<string, object?>? targetOptions = null)
    {
        var directory = Path.Join(Path.GetTempPath(), "GMConverter.SourceEngine.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var output = Path.Join(directory, "mdl");
        var stubStudioMdl = Path.Join(directory, "cestudiomdl.exe");
        File.WriteAllText(stubStudioMdl, string.Empty);
        _ = Record.Exception(() => new MDLExporter(new DefaultTextureFactory()).Export(model, new ExportContext(output, model.Name, new OptionValues(
            new Dictionary<string, object?>(extraOptions ?? new Dictionary<string, object?>())
            {
                ["studioMdlPath"] = stubStudioMdl,
                ["buildMaterials"] = false,
            }))
        {
            TargetGameId = targetGameId,
            TargetOptions = new OptionValues(targetOptions ?? new Dictionary<string, object?>()),
        }));
        return output;
    }
}
