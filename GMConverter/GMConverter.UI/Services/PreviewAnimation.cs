using GMConverter.SDK.Animation;

namespace GMConverter.UI.Services;

/// <summary>An animation of the previewed model and the role it was given before any override.</summary>
internal sealed record PreviewAnimation(string Name, AnimationRole? GuessedRole);
