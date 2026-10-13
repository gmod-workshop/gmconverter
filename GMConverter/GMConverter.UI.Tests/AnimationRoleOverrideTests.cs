using GMConverter.Core.Animation;
using GMConverter.Core.Pipeline;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Common;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Materials;
using GMConverter.UI.Services;

namespace GMConverter.UI.Tests;

public sealed class AnimationRoleOverrideTests
{
    [Theory]
    [InlineData("run", AnimationAction.Run, null, null)]
    [InlineData("Run,Rifle,Forward", AnimationAction.Run, WeaponStance.Rifle, MoveDirection.Forward)]
    [InlineData("combat-idle", AnimationAction.CombatIdle, null, null)]
    [InlineData("combatidle , pistol", AnimationAction.CombatIdle, WeaponStance.Pistol, null)]
    [InlineData("turn,left", AnimationAction.Turn, null, MoveDirection.Left)]
    public void RoleTextParses(string text, AnimationAction action, WeaponStance? stance, MoveDirection? direction)
    {
        Assert.Equal(new AnimationRole(action, stance, direction), AnimationRoleText.Parse(text));
    }

    [Fact]
    public void RoleTextRoundTrips()
    {
        var role = new AnimationRole(AnimationAction.CrouchWalk, WeaponStance.Shotgun, MoveDirection.Backward);

        Assert.Equal("crouch-walk,shotgun,backward", AnimationRoleText.Format(role));
        Assert.Equal(role, AnimationRoleText.Parse(AnimationRoleText.Format(role)));
        Assert.Equal("none", AnimationRoleText.Format(null));
        Assert.Null(AnimationRoleText.Parse("NONE"));
        Assert.Contains("melee-attack", AnimationRoleText.Actions);
    }

    [Theory]
    [InlineData("sprint")]
    [InlineData("run,rifle,rifle")]
    [InlineData("run,sideways")]
    [InlineData("")]
    public void BadRoleTextIsRejected(string text)
    {
        Assert.Throws<GMConverterException>(() => AnimationRoleText.Parse(text));
    }

    [Fact]
    public void OverridesSplitAtTheLastEquals()
    {
        var (clip, role) = AnimationRoleText.ParseOverride("Odd=Name=walk");

        Assert.Equal("Odd=Name", clip);
        Assert.Equal(AnimationAction.Walk, role?.Action);
        Assert.Throws<GMConverterException>(() => AnimationRoleText.ParseOverride("walk"));
        Assert.Throws<GMConverterException>(() => AnimationRoleText.ParseOverride("clip="));
    }

    [Fact]
    public void OverridesReplaceAndClearRolesByClipName()
    {
        var guessed = new AnimationRole(AnimationAction.Jog, WeaponStance.Rifle, MoveDirection.Forward);
        var model = new Model("m", [], [new Material("m")], Animations:
        [
            new AnimationClip("Jog_F", 30f, 1f, [], Role: guessed),
            new AnimationClip("Walk_Start", 30f, 1f, [], Role: new AnimationRole(AnimationAction.Walk)),
            new AnimationClip("Idle", 30f, 1f, [], Role: new AnimationRole(AnimationAction.Idle)),
        ]);

        var overridden = AnimationRoleOverrides.Apply(model, new Dictionary<string, AnimationRole?>
        {
            ["jog_f"] = guessed with { Action = AnimationAction.Run },
            ["Walk_Start"] = null,
        });

        Assert.Equal(guessed with { Action = AnimationAction.Run }, overridden.Animations![0].Role);
        Assert.Null(overridden.Animations[1].Role);
        Assert.Equal(AnimationAction.Idle, overridden.Animations[2].Role?.Action);
        Assert.Same(model, AnimationRoleOverrides.Apply(model, new Dictionary<string, AnimationRole?>()));
    }

    [Fact]
    public void OverridesForMissingClipsAreAnError()
    {
        var model = new Model("m", [], [new Material("m")], Animations: [new AnimationClip("Idle", 30f, 1f, [])]);

        var error = Assert.Throws<GMConverterException>(() => AnimationRoleOverrides.Apply(model, new Dictionary<string, AnimationRole?> { ["Idel"] = null }));

        Assert.Contains("Idel", error.Message);
        Assert.Contains("Animations: Idle", error.Message);
    }

    [Fact]
    public void PipelineAppliesOverridesAfterImport()
    {
        ExporterOptionsTests.InitializePlugins();
        var output = Path.Join(Path.GetTempPath(), "GMConverter.RoleTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var input = Path.Join(output, "input.sample");
        File.WriteAllText(input, "test");
        var request = new AssetRequest("sample", input)
        {
            OutputFormat = "sample",
            OutputDirectory = output,
            RoleOverrides = new Dictionary<string, AnimationRole?> { ["Missing"] = null },
        };

        // The sample model has no animations, so any override names a missing clip.
        var error = Assert.Throws<GMConverterException>(() => new AssetJobRunner().Run(new AssetJob(request)));
        Assert.Contains("Missing", error.Message);
    }

    [Fact]
    public void RolesTableTracksGuessesAndOverrides()
    {
        var vm = ExporterOptionsTests.CreateViewModel();
        var jog = new AnimationRole(AnimationAction.Jog, WeaponStance.Rifle, MoveDirection.Forward);
        vm.ApplyPreviewAnimations([new PreviewAnimation("Jog_F", jog), new PreviewAnimation("Walk_Start", null), new PreviewAnimation("Idle", null)]);

        Assert.True(vm.HasAnimationRoles);
        Assert.Equal("jog, rifle, forward", vm.AnimationRoles[0].GuessedText);
        Assert.Empty(vm.CaptureSettings().RoleOverrides);

        vm.AnimationRoles[0].SelectedChoice = "run";
        vm.AnimationRoles[2].SelectedChoice = "idle";
        var overrides = vm.CaptureSettings().RoleOverrides;
        // Choosing an action keeps the guessed stance and direction.
        Assert.Equal(jog with { Action = AnimationAction.Run }, overrides["Jog_F"]);
        Assert.Equal(new AnimationRole(AnimationAction.Idle), overrides["Idle"]);
        Assert.Equal(2, overrides.Count);

        // A new preview of the same input keeps choices for clips still present.
        vm.ApplyPreviewAnimations([new PreviewAnimation("Jog_F", jog)]);
        Assert.Equal("run", vm.AnimationRoles.Single().SelectedChoice);

        vm.InputPath = Path.Join(Path.GetTempPath(), "other.sample");
        Assert.False(vm.HasAnimationRoles);
        Assert.Empty(vm.CaptureSettings().RoleOverrides);
    }

    [Fact]
    public void CliRoleOptionsAreValidated()
    {
        ExporterOptionsTests.InitializePlugins();
        var output = Path.Join(Path.GetTempPath(), "GMConverter.RoleTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var input = Path.Join(output, "input.sample");
        File.WriteAllText(input, "test");
        string[] common = ["--input-format", "sample", "--output-format", "sample", "--input-path", input, "--output-path", output];

        Assert.Equal(2, CLI.Program.Main([.. common, "--role", "Clip=sprint"]));
        Assert.NotEqual(0, CLI.Program.Main([.. common, "--role", "Clip=run", "--role", "clip=walk"]));
        // The sample model has no animations to override.
        Assert.Equal(2, CLI.Program.Main([.. common, "--role", "Clip=run"]));
        Assert.Equal(0, CLI.Program.Main(common));
    }
}
