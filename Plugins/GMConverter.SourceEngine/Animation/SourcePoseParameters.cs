using GMConverter.SDK.Animation;

namespace GMConverter.SourceEngine.Animation;

// The pose parameters Source NPC code sets, shared by Source games. Source angles grow to the left
// and downwards, the opposite of the SDK's (right and up), so those axes are reversed; HL2's own
// aim layers write their ranges the same way (blend aim_yaw 45 -45).
internal static class SourcePoseParameters
{
    public static SourcePoseParameter? For(BlendAxis axis)
    {
        return axis switch
        {
            BlendAxis.AimYaw => new SourcePoseParameter("aim_yaw", Reversed: true),
            BlendAxis.AimPitch => new SourcePoseParameter("aim_pitch", Reversed: true),
            BlendAxis.MoveDirection => new SourcePoseParameter("move_yaw", Reversed: true),
            // Source picks walk or run sequences by activity rather than blending by speed.
            _ => null,
        };
    }
}
