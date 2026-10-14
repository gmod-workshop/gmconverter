using GMConverter.Unity.Assets;

namespace GMConverter.Unity.Animation;

/// <summary>Which clips to import with a model: those its Animator controller plays, and any others named.</summary>
internal sealed record UnityAnimationRequest(bool IncludeController, IReadOnlyList<UnityObjectKey> Clips)
{
    public static UnityAnimationRequest None { get; } = new(false, []);
}
