namespace GMConverter.SDK.Audio;

/// <summary>What a <see cref="SoundEvent"/> is, so targets can route it to a matching channel or mixer group.</summary>
public enum SoundCategory
{
    Other,
    Weapon,
    Voice,
    Footstep,
    Movement,
    Impact,
    Ambient,
    Music,
    UI,
}
