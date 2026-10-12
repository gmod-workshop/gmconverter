namespace GMConverter.SDK.Animation;

/// <summary>
/// Marks a clip as a layer meant to be added on top of other animation (an additive or delta
/// animation) rather than played on its own. The clip's keyframes stay absolute: they show the layer
/// applied to <see cref="Base"/>, so exporters without layering can play the clip as-is, and exporters
/// with layering recover the layer by taking the difference from <see cref="Base"/> in <see cref="Space"/>.
/// </summary>
/// <param name="Space">The space the difference is measured and applied in.</param>
/// <param name="Base">The pose the layer was authored against, or null for the skeleton's bind pose.</param>
public sealed record AnimationLayering(LayerSpace Space, AnimationPose? Base = null);
