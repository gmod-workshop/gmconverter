using System.Numerics;
using GMConverter.SDK.Explorer;
using GMConverter.SDK.Games;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Options;
using GMConverter.SourceEngine.Games;

namespace GMConverter.SourceEngine.Tests;

public sealed class GarrysModGameProfileTests
{
    private static readonly OptionValues _defaults = OptionValues.Empty;

    [Fact]
    public void HandAttachmentsCopyTheWeaponAndPropSocketsOnEachHand()
    {
        // The Zero Company B1's sockets: an aimed prop socket and a side-less weapon socket must
        // not win over the right-hand weapon socket.
        var model = CreateModel(
            new Attachment("prop_rSocket_Aim", 1, Transform.Identity),
            new Attachment("weaponSocket_0", 2, Transform.Identity),
            new Attachment("prop_rSocket", 1, Transform.Identity),
            new Attachment("weapon_r_socket", 2, new Transform(Vector3.UnitX, Quaternion.Identity, Vector3.One)),
            new Attachment("prop_L_Socket", 3, Transform.Identity));

        var prepared = new GarrysModGameProfile().Prepare(model, _defaults);

        var right = Assert.Single(prepared.Attachments!, attachment => attachment.Name == HandAttachments.RightName);
        Assert.Equal(2, right.BoneIndex);
        Assert.Equal(Vector3.UnitX, right.LocalTransform.Translation);
        var left = Assert.Single(prepared.Attachments!, attachment => attachment.Name == HandAttachments.LeftName);
        Assert.Equal(3, left.BoneIndex);
        Assert.Equal(7, prepared.Attachments!.Count);
    }

    [Fact]
    public void HandAttachmentsFallBackToTheHandBones()
    {
        var prepared = new GarrysModGameProfile().Prepare(CreateModel(), _defaults);

        Assert.Equal(4, prepared.Attachments!.Single(attachment => attachment.Name == HandAttachments.RightName).BoneIndex);
        Assert.Equal(5, prepared.Attachments!.Single(attachment => attachment.Name == HandAttachments.LeftName).BoneIndex);
    }

    [Fact]
    public void ExistingHandAttachmentsAreKept()
    {
        var existing = new Attachment("anim_attachment_RH", 1, Transform.Identity);

        var prepared = new GarrysModGameProfile().Prepare(CreateModel(existing), _defaults);

        Assert.Same(existing, prepared.Attachments!.Single(attachment => attachment.Name == HandAttachments.RightName));
        Assert.Single(prepared.Attachments!, attachment => attachment.Name == HandAttachments.LeftName);
    }

    [Fact]
    public void HandAttachmentsCanBeTurnedOff()
    {
        var model = CreateModel();
        var options = new OptionValues(new Dictionary<string, object?> { [GarrysModGameProfile.HandAttachmentsOption] = false });

        Assert.Same(model, new GarrysModGameProfile().Prepare(model, options));
    }

    [Fact]
    public void ModelsWithoutHandsAreUnchanged()
    {
        var model = new Model("prop", [], [new Material("m")]);

        Assert.Same(model, new GarrysModGameProfile().Prepare(model, _defaults));
    }

    [Fact]
    public void GarrysModIsDetectedFromItsGameFolder()
    {
        var install = Path.Join(Path.GetTempPath(), "GMConverter.GameTests", Guid.NewGuid().ToString("N"), "GarrysMod");
        var game = Path.Join(install, "garrysmod");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Join(game, "gameinfo.txt"), "\"GameInfo\" {}");

        Assert.Equal(GameMatch.Exact, new GarrysModGameProfile().Detect(new ExplorerTarget(install)));
        Assert.Equal(GameMatch.Exact, new GarrysModGameProfile().Detect(new ExplorerTarget(game)));
        Assert.Equal(GameMatch.Fallback, new SourceGameProfile().Detect(new ExplorerTarget(game)));
        Assert.Equal(GameMatch.None, new SourceGameProfile().Detect(new ExplorerTarget(install)));
    }

    [Fact]
    public void BothTargetsBelongToTheMdlExporter()
    {
        Assert.Equal("mdl", new GarrysModGameProfile().ExporterFormat);
        Assert.Equal("mdl", new SourceGameProfile().ExporterFormat);
    }

    private static Model CreateModel(params Attachment[] attachments)
    {
        var skeleton = new Skeleton(
        [
            new Bone(0, "root", -1, Transform.Identity),
            new Bone(1, "prop_r", 0, Transform.Identity),
            new Bone(2, "ik_hand_weapon", 0, Transform.Identity),
            new Bone(3, "prop_l", 0, Transform.Identity),
            new Bone(4, "hand_r", 0, Transform.Identity),
            new Bone(5, "hand_l", 0, Transform.Identity),
            new Bone(6, "ik_hand_r", 0, Transform.Identity),
        ]);
        return new Model("b1", [], [new Material("m")], skeleton, Attachments: attachments.Length == 0 ? null : attachments);
    }
}
