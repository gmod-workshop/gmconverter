using System.Globalization;
using GMConverter.SDK.Common;
using GMConverter.Unity.Assets;

namespace GMConverter.Unity.Audio;

/// <summary>
/// A Unity <c>AudioClip</c>. Since Unity 5 a clip's audio is an FMOD sound bank (FSB5) holding one
/// sound, kept in a <c>.resource</c> file beside the serialized file whatever the compression.
/// </summary>
internal sealed class UnityAudioClip
{
    /// <summary>File extension of an FMOD sound bank.</summary>
    public const string BankExtension = ".fsb";

    private readonly UnityObject _clip;

    public UnityAudioClip(UnityObject clip)
    {
        _clip = clip;
    }

    public string Name => _clip.Name;

    /// <summary>A one-line description for the explorer, such as <c>1.38 s, 1 ch, 44100 Hz</c>.</summary>
    public string Describe()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{_clip["m_Length"].AsFloat:0.##} s, {_clip["m_Channels"].AsInt} ch, {_clip["m_Frequency"].AsInt} Hz");
    }

    /// <summary>The clip's FMOD sound bank.</summary>
    public byte[] ReadBank(UnityAssetLibrary library)
    {
        var resource = _clip["m_Resource"];
        if (resource.IsDummy)
        {
            // Unity 4 kept the encoded file itself in m_AudioData instead.
            throw new GMConverterException($"AudioClip {Name} comes from Unity 4 or earlier, whose audio is not supported.");
        }

        var size = resource["m_Size"].AsLong;
        if (size <= 0)
        {
            throw new GMConverterException($"AudioClip {Name} has no audio data.");
        }

        return library.ReadResource(resource["m_Source"].AsString, resource["m_Offset"].AsLong, size);
    }
}
