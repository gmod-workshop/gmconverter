using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GMConverter.SDK.Common;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Textures;
using GMConverter.UnrealEngine.Formats.UE3.Packages;
using GMConverter.UnrealEngine.Formats.UE3.Textures;
using GMConverter.UnrealEngine.Materials;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GMConverter.UnrealEngine.Formats.UE3.Materials;

/// <summary>
/// A translated material's <see cref="MaterialGraph"/> as the explorer exports it, with its textures as
/// PNG files beside it (stored as the game stores them, not baked). Particle sidecars embed one per
/// emitter; mesh materials write one to a <see cref="Extension"/> file named in their <c>.mat</c>.
/// </summary>
internal sealed record UE3MaterialGraphFile(
    IReadOnlyList<MaterialGraphNode> Nodes,
    IReadOnlyList<UE3MaterialGraphFile.TextureFile> Textures,
    int? Emissive,
    int? Opacity)
{
    public const string Extension = ".graph.json";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter() },
        // Computed properties aren't stored.
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                info =>
                {
                    foreach (var property in info.Properties.Where(property => property.Set is null && !IsConstructorParameter(info, property)).ToArray())
                    {
                        info.Properties.Remove(property);
                    }
                },
            },
        },
    };

    /// <summary>The lit surface, for materials lit by a known shading model.</summary>
    public MaterialGraphSurface? Surface { get; init; }

    /// <summary>A texture's file (relative to the graph's file) and how it is read.</summary>
    public sealed record TextureFile(string File, bool Srgb, bool ClampU, bool ClampV);

    public static UE3MaterialGraphFile Read(string path) =>
        JsonSerializer.Deserialize<UE3MaterialGraphFile>(File.ReadAllText(path), _jsonOptions)
        ?? throw new InvalidDataException($"Empty UE3 material graph: {path}");

    public void Write(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, _jsonOptions));

    /// <summary>
    /// A translated material with each texture it samples written once into <paramref name="outputDirectory"/>
    /// (<paramref name="written"/> holds those already written there, by texture).
    /// </summary>
    public static UE3MaterialGraphFile Export(UE3GameContent content, UE3TranslatedMaterial material, string outputDirectory, Dictionary<string, TextureFile> written)
    {
        List<TextureFile> textures = [];
        foreach (var reference in material.Textures)
        {
            if (!written.TryGetValue(reference.Key, out var file))
            {
                file = WriteTexture(content, reference, outputDirectory, written.Values);
                written[reference.Key] = file;
            }

            textures.Add(file);
        }

        return new UE3MaterialGraphFile(material.Nodes, textures, material.Emissive, material.Opacity) { Surface = material.Surface };
    }

    /// <summary>The graph with its textures loaded by <paramref name="load"/> (given each file name); null when one is missing.</summary>
    public MaterialGraph? ToGraph(Func<string, Texture?> load)
    {
        List<MaterialGraphTexture> textures = [];
        foreach (var texture in Textures)
        {
            if (load(texture.File) is not { } loaded)
            {
                return null;
            }

            textures.Add(new MaterialGraphTexture(loaded, texture.Srgb, texture.ClampU, texture.ClampV));
        }

        var graph = new MaterialGraph(Nodes, textures, Emissive, Opacity) { Surface = Surface };
        graph.Validate();
        return graph;
    }

    private static TextureFile WriteTexture(UE3GameContent content, UE3ObjectReference reference, string outputDirectory, IEnumerable<TextureFile> taken)
    {
        var source = UE3Texture2D.Read(reference.Package, reference.Export);
        var image = UE3TextureDecoder.Decode(reference.Package, source, content.TextureCaches);
        if (!image.IsValid)
        {
            throw new GMConverterException($"UE3 texture {source.Name} could not be decoded.");
        }

        var stem = NameHelpers.SanitizeFileName(source.Name);
        var file = stem + ".png";
        for (var suffix = 2; taken.Any(texture => texture.File.Equals(file, StringComparison.OrdinalIgnoreCase)); suffix++)
        {
            file = FormattableString.Invariant($"{stem}_{suffix}.png");
        }

        if (image.Format == DecodedImagePixelFormat.Bgra8888)
        {
            using var bgra = Image.LoadPixelData<Bgra32>(image.Pixels, image.Width, image.Height);
            bgra.SaveAsPng(Path.Combine(outputDirectory, file));
        }
        else
        {
            using var rgba = Image.LoadPixelData<Rgba32>(image.Pixels, image.Width, image.Height);
            rgba.SaveAsPng(Path.Combine(outputDirectory, file));
        }

        return new TextureFile(file, source.Srgb, source.AddressX != UE3TextureAddress.Wrap, source.AddressY != UE3TextureAddress.Wrap);
    }

    private static bool IsConstructorParameter(JsonTypeInfo info, JsonPropertyInfo property) =>
        info.Type.GetConstructors().Any(constructor => constructor.GetParameters().Any(parameter =>
            string.Equals(parameter.Name, property.Name, StringComparison.OrdinalIgnoreCase)));
}
