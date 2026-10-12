namespace GMConverter.SDK.Animation;

/// <summary>The space a layered clip's difference from its base pose is measured and applied in.</summary>
public enum LayerSpace
{
    /// <summary>Each bone's difference is relative to its parent, so it follows the bones above it.</summary>
    Local,

    /// <summary>
    /// Each bone's rotation difference is relative to the whole model, so it holds its direction however
    /// the bones above it are posed. Typically used for aiming.
    /// </summary>
    Model,
}
