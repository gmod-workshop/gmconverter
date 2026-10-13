using GMConverter.SDK.Animation;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Materials;
using GMConverter.UnrealEngine.Formats.Unreal;

namespace GMConverter.UnrealEngine.Tests;

public sealed class AnimationRoleGuesserTests
{
    // Names from the Star Wars Zero Company B1 battle droid.
    [Theory]
    [InlineData("A_B1_2HRifle_Idle", AnimationAction.Idle, null)]
    [InlineData("A_B1_2HRifle_Aim_Idle", AnimationAction.CombatIdle, null)]
    [InlineData("A_B1_2HRifle_Walk_F", AnimationAction.Walk, MoveDirection.Forward)]
    [InlineData("A_B1_2HRifle_Jog_F", AnimationAction.Jog, MoveDirection.Forward)]
    [InlineData("A_B1_2HRifle_Run_F", AnimationAction.Run, MoveDirection.Forward)]
    [InlineData("A_B1_2HRifle_Crouch_Idle", AnimationAction.CrouchIdle, null)]
    [InlineData("A_B1_2HRifle_Turn_L_90", AnimationAction.Turn, MoveDirection.Left)]
    [InlineData("A_B1_2HRifle_Aim_Turn_R_90", AnimationAction.Turn, MoveDirection.Right)]
    [InlineData("_A_B1_2HRifle_Aim_Fire_StandardShot_E5_01", AnimationAction.Attack, null)]
    [InlineData("A_B1_2HRifle_Fire_F_Shoot", AnimationAction.Attack, MoveDirection.Forward)]
    [InlineData("A_B1_2HRifle_HitReact_L_Med", AnimationAction.Flinch, MoveDirection.Left)]
    [InlineData("A_B1_2HRifle_Flinch_F", AnimationAction.Flinch, MoveDirection.Forward)]
    [InlineData("A_B1_2HRifle_HitReact_Hit_Stagger_Back_01", AnimationAction.Stagger, MoveDirection.Backward)]
    [InlineData("A_B1_2HRifle_Death_F", AnimationAction.Death, MoveDirection.Forward)]
    [InlineData("A_2HRifle_Death_01", AnimationAction.Death, null)]
    public void SteadyActionsGetARole(string name, AnimationAction action, MoveDirection? direction)
    {
        var role = AnimationRoleGuesser.Guess(name);

        Assert.Equal(new AnimationRole(action, WeaponStance.Rifle, direction), role);
    }

    [Theory]
    [InlineData("A_B1_2HRifle_Run_Start_F_0")]
    [InlineData("A_B1_2HRifle_RunStop_90L")]
    [InlineData("A_B1_2HRifle_Walk_Stop_F")]
    [InlineData("A_B1_2HRifle_Aim_Enter_90L")]
    [InlineData("A_B1_2HRifle_Crouch_Exit")]
    [InlineData("A_B1_2HRifle_Turn_L_180")]
    [InlineData("A_B1_2HRifle_Crouch_Turn_L_90")]
    [InlineData("A_B1_2HRifle_Aim_LU")]
    [InlineData("A_B1_2HRifle_Aim_CC_ref")]
    [InlineData("A_B1_2HRifle_HitReact_Hit_Light_Front_Left_Add")]
    [InlineData("A_B1_2HRifle_Flinch_Idle_FBL")]
    [InlineData("A_B1_2HRifle_Idle_Break_01")]
    [InlineData("A_B1_2HRifle_Death_Pose_Midair_Back")]
    [InlineData("A_B1_2HRifle_Climb_Up_Ladder300")]
    [InlineData("A_B1_Dead_01")]
    public void TransitionsPosesAndLayersGetNoRole(string name)
    {
        Assert.Null(AnimationRoleGuesser.Guess(name));
    }

    [Theory]
    [InlineData("Pistol_Idle", WeaponStance.Pistol)]
    [InlineData("Shotgun_Reload", WeaponStance.Shotgun)]
    [InlineData("Unarmed_Walk_F", WeaponStance.Unarmed)]
    [InlineData("Idle", null)]
    public void StanceComesFromTheName(string name, WeaponStance? stance)
    {
        Assert.Equal(stance, AnimationRoleGuesser.Guess(name)?.Stance);
    }

    [Fact]
    public void ApplyKeepsRolesAlreadySet()
    {
        var set = new AnimationRole(AnimationAction.Jump);
        var model = new Model("m", [], [new Material("m")], Animations:
        [
            new AnimationClip("A_Run_F", 30f, 1f, []),
            new AnimationClip("A_Run_F_Custom", 30f, 1f, [], Role: set),
            new AnimationClip("A_Run_Start", 30f, 1f, []),
        ]);

        var prepared = new ZeroCompanyUnrealGameProfile().Prepare(model);

        Assert.Equal(AnimationAction.Run, prepared.Animations![0].Role?.Action);
        Assert.Same(set, prepared.Animations[1].Role);
        Assert.Null(prepared.Animations[2].Role);
    }

    [Fact]
    public void ModelsWithoutAnimationsAreUnchanged()
    {
        var model = new Model("m", [], [new Material("m")]);

        Assert.Same(model, new GenericUnrealGameProfile().Prepare(model));
    }
}
