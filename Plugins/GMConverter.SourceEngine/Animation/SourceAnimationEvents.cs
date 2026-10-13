using GMConverter.SDK.Animation;
using GMConverter.SourceEngine.Common;

namespace GMConverter.SourceEngine.Animation;

// The animation events Source games share. Footsteps become NPC foot events, which NPC code turns
// into a step sound for the surface underfoot; sounds become client sound events naming the sound,
// which sound exporters keep under its source-game name. Firing has no event every Source game
// handles the same way, so it is left out.
internal static class SourceAnimationEvents
{
    public static IReadOnlyList<SourceAnimationEvent> For(AnimationClip clip)
    {
        if (clip.Events is not { Count: > 0 } events || clip.Layering is not null)
        {
            return [];
        }

        List<SourceAnimationEvent> written = [];
        foreach (var animationEvent in events)
        {
            switch (animationEvent.Kind)
            {
                case AnimationEventKind.Footstep when FootEvent(animationEvent.Bone) is { } foot:
                    written.Add(new SourceAnimationEvent(foot, animationEvent.TimeSeconds));
                    break;
                case AnimationEventKind.Sound when !string.IsNullOrWhiteSpace(animationEvent.Sound):
                    written.Add(new SourceAnimationEvent("AE_CL_PLAYSOUND", animationEvent.TimeSeconds, animationEvent.Sound));
                    break;
            }
        }

        return written;
    }

    private static string? FootEvent(string? bone)
    {
        if (bone is null)
        {
            return null;
        }

        var words = NameWords.Split(bone);
        return NameWords.IsLeft(words) ? "AE_NPC_LEFTFOOT"
            : NameWords.IsRight(words) ? "AE_NPC_RIGHTFOOT"
            : null;
    }
}
