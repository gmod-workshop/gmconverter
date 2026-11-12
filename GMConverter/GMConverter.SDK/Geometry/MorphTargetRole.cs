namespace GMConverter.SDK.Geometry;

/// <summary>What a morph target is for, in engine-neutral terms.</summary>
public enum MorphTargetRole
{
    /// <summary>Nothing an exporter needs to know about.</summary>
    None,

    /// <summary>Opens the mouth, as a character talking does; games that move mouths to speech drive it.</summary>
    MouthOpen,
}
