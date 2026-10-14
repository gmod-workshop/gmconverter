using System.Numerics;
using GMConverter.Unity.Assets;

namespace GMConverter.Unity.Materials;

/// <summary>The saved properties of a Unity <c>Material</c>: shader name, textures, floats and colours by name.</summary>
internal sealed class UnityMaterialProperties
{
    private readonly Dictionary<string, UnityObjectKey?> _textures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Vector2 Scale, Vector2 Offset)> _textureTransforms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _floats = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector4> _colors = new(StringComparer.Ordinal);

    private UnityMaterialProperties(string name, string shaderName)
    {
        Name = name;
        ShaderName = shaderName;
    }

    public string Name { get; }

    public string ShaderName { get; }

    public static UnityMaterialProperties Read(UnityAssetLibrary library, UnityObject material)
    {
        var shader = library.Resolve(material.File, material["m_Shader"]);
        var shaderName = shader?["m_ParsedForm"]["m_Name"] is { IsDummy: false } parsedName ? parsedName.AsString : string.Empty;
        var properties = new UnityMaterialProperties(material.Name, shaderName);
        var saved = material["m_SavedProperties"];

        foreach (var entry in UnityFields.Items(saved["m_TexEnvs"]))
        {
            var name = entry["first"].AsString;
            var value = entry["second"];
            var texture = library.Resolve(material.File, value["m_Texture"]);
            properties._textures[name] = texture?.ClassId == AssetsTools.NET.Extra.AssetClassID.Texture2D ? texture.Key : null;
            properties._textureTransforms[name] = (
                new Vector2(value["m_Scale"]["x"].AsFloat, value["m_Scale"]["y"].AsFloat),
                new Vector2(value["m_Offset"]["x"].AsFloat, value["m_Offset"]["y"].AsFloat));
        }

        foreach (var entry in UnityFields.Items(saved["m_Floats"]))
        {
            properties._floats[entry["first"].AsString] = entry["second"].AsFloat;
        }

        foreach (var entry in UnityFields.Items(saved["m_Colors"]))
        {
            var color = entry["second"];
            properties._colors[entry["first"].AsString] = new Vector4(color["r"].AsFloat, color["g"].AsFloat, color["b"].AsFloat, color["a"].AsFloat);
        }

        return properties;
    }

    /// <summary>The first of <paramref name="names"/> that holds a texture, with its tiling and offset.</summary>
    public (UnityObjectKey Texture, string Property)? FindTexture(params ReadOnlySpan<string> names)
    {
        foreach (var name in names)
        {
            if (_textures.TryGetValue(name, out var texture) && texture is { } key)
            {
                return (key, name);
            }
        }

        return null;
    }

    public (Vector2 Scale, Vector2 Offset) GetTextureTransform(string name)
    {
        return _textureTransforms.TryGetValue(name, out var transform) ? transform : (Vector2.One, Vector2.Zero);
    }

    public float GetFloat(string name, float defaultValue)
    {
        return _floats.TryGetValue(name, out var value) ? value : defaultValue;
    }

    /// <summary>The first of <paramref name="names"/> that is set, or <paramref name="defaultValue"/>.</summary>
    public Vector4 GetColor(Vector4 defaultValue, params ReadOnlySpan<string> names)
    {
        foreach (var name in names)
        {
            if (_colors.TryGetValue(name, out var value))
            {
                return value;
            }
        }

        return defaultValue;
    }
}
