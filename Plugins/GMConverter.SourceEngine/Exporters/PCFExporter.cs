using System.Text;
using GMConverter.SDK.Common;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Options;
using GMConverter.SDK.Particles;
using GMConverter.SourceEngine.Particles;
using GMConverter.SourceEngine.Profiles;

namespace GMConverter.SourceEngine.Exporters;

/// <summary>
/// Writes a particle effect for Source: its systems in <c>particles/&lt;name&gt;.pcf</c>, each
/// emitter's texture as a SpriteCard material (flipbooks as VTF sprite sheets), and whatever the
/// target game needs to load the file (see <see cref="ISourceParticleProfile"/>).
/// </summary>
internal sealed class PCFExporter : IParticleExporter
{
    public const string MaterialPathOption = "materialPath";
    public const string MaxTextureSizeOption = "maxTextureSize";
    public const string ViewModelOption = "viewModel";

    private static readonly UTF8Encoding _utf8NoBom = new(false);

    public string Format => "pcf";

    public string DisplayName => "Source particle effects";

    public OptionSchema OptionSchema { get; } = new(
    [
        new OptionGroup("particles", "Particles",
        [
            new OptionDescriptor(MaterialPathOption, OptionType.String, "Material path")
            {
                Description = "Folder for the effect's materials under the game's materials directory. Defaults to particle/gmconverter/<name>.",
            },
            new OptionDescriptor(MaxTextureSizeOption, OptionType.Int, "Max texture size")
            {
                DefaultValue = 512,
                Description = "Particle textures larger than this are scaled down.",
            },
            new OptionDescriptor(ViewModelOption, OptionType.Bool, "View model effect")
            {
                DefaultValue = false,
                Description = "Draw the effect with the view model, over it and at its field of view, for effects a first-person model's animations start.",
            },
        ]),
    ]);

    public ExportResult Export(ParticleEffect effect, ExportContext context)
    {
        var name = NameHelpers.SanitizeMaterialName(context.BaseName);
        var materialPath = (context.Options.GetString(MaterialPathOption) is { Length: > 0 } path ? path : $"particle/gmconverter/{name}").Trim('/').Replace('\\', '/');
        var maxTextureSize = Math.Max(4, context.Options.GetInt(MaxTextureSizeOption, 512));
        var materialDirectory = Path.Join(context.OutputDirectory, "materials", materialPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(materialDirectory);
        List<string> warnings = [];

        Dictionary<ParticleEmitter, (string Material, SpriteSheetPlan? Sheet)> materials = new(ReferenceEqualityComparer.Instance);
        HashSet<string> taken = new(StringComparer.OrdinalIgnoreCase);
        foreach (var emitter in effect.Emitters)
        {
            var file = Unique(NameHelpers.SanitizeMaterialName($"{effect.Name}_{emitter.Name}"), taken);
            var sheet = emitter.Sheet is { } flipbook ? SpriteSheetPlan.Create(flipbook) : null;
            // Mesh emitters aren't written (see PcfSystemBuilder), so they need no sprite.
            if (emitter.Mesh is null)
            {
                WriteTexture(Path.Join(materialDirectory, file + ".vtf"), emitter, sheet, maxTextureSize, warnings);
                File.WriteAllText(Path.Join(materialDirectory, file + ".vmt"), SpriteCard($"{materialPath}/{file}", emitter.Material), _utf8NoBom);
            }

            materials[emitter] = ($"{materialPath}/{file}.vmt", sheet);
        }

        var builder = new PcfSystemBuilder(context.Options.GetBool(ViewModelOption, defaultValue: false));
        var root = builder.Build(effect, emitter => materials[emitter].Material, emitter => materials[emitter].Sheet);
        warnings.AddRange(builder.Warnings);

        var particlesDirectory = Path.Join(context.OutputDirectory, "particles");
        Directory.CreateDirectory(particlesDirectory);
        var pcfPath = Path.Join(particlesDirectory, name + ".pcf");
        using (var stream = File.Create(pcfPath))
        {
            DmxBinaryWriter.Write(stream, root, "pcf", 1, effect.Name);
        }

        var systems = ((IReadOnlyList<DmxElement>)root.Get("particleSystemDefinitions")!).Select(system => system.Name).ToArray();
        List<string> files = [pcfPath];
        var profile = SourceParticleProfiles.Find(context.TargetGameId) ?? SourceParticleProfiles.Find("source")!;
        var (loaderPath, contents) = profile.WriteLoader($"particles/{name}.pcf", systems, name);
        if (loaderPath is null)
        {
            warnings.Add($"{effect.Name}: {contents}");
        }
        else
        {
            var fullPath = Path.Join(context.OutputDirectory, loaderPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, contents, _utf8NoBom);
            files.Add(fullPath);
        }

        return new ExportResult(files) { Warnings = warnings };
    }

    private static void WriteTexture(string path, ParticleEmitter emitter, SpriteSheetPlan? sheet, int maxTextureSize, List<string> warnings)
    {
        var texture = emitter.Material.DiffuseTexture ?? emitter.Material.EmissiveTexture;
        var brightness = PcfSystemBuilder.Brightness(emitter);
        if (texture is null)
        {
            warnings.Add(brightness > 1f
                ? $"{emitter.Name}: has no texture, and its colour is brighter than white; drawn white."
                : $"{emitter.Name}: has no texture; drawn plain white.");
            SpriteVtfWriter.Write(path, [.. Enumerable.Repeat((byte)255, 4 * 4 * 4)], 4, 4, 1, 1, null);
            return;
        }

        using var sized = texture.Width > maxTextureSize || texture.Height > maxTextureSize ? texture.Resized(maxTextureSize) : null;
        var source = sized ?? texture;
        if (!IsPowerOfTwo(source.Width) || !IsPowerOfTwo(source.Height))
        {
            warnings.Add($"{emitter.Name}: texture is {source.Width}x{source.Height}, not a power of two, which Source may draw blurred or not at all.");
        }

        var (columns, rows) = emitter.Sheet is { } flipbook ? (Math.Max(1, flipbook.Columns), Math.Max(1, flipbook.Rows)) : (1, 1);
        SpriteVtfWriter.Write(path, Brighten(source.GetRgbaPixels(), brightness), source.Width, source.Height, columns, rows, sheet?.Sequences);
    }

    // Scales colour in linear light, so the texture times the reduced particle colour matches the original.
    private static byte[] Brighten(byte[] rgba, float factor)
    {
        if (factor <= 1f)
        {
            return rgba;
        }

        var table = new byte[256];
        for (var value = 0; value < 256; value++)
        {
            var srgb = value / 255f;
            var linear = srgb <= 0.04045f ? srgb / 12.92f : MathF.Pow((srgb + 0.055f) / 1.055f, 2.4f);
            var scaled = Math.Min(1f, linear * factor);
            var encoded = scaled <= 0.0031308f ? scaled * 12.92f : (1.055f * MathF.Pow(scaled, 1f / 2.4f)) - 0.055f;
            table[value] = (byte)MathF.Round(encoded * 255f);
        }

        var result = (byte[])rgba.Clone();
        for (var index = 0; index < result.Length; index += 4)
        {
            result[index] = table[result[index]];
            result[index + 1] = table[result[index + 1]];
            result[index + 2] = table[result[index + 2]];
        }

        return result;
    }

    private static string SpriteCard(string texturePath, Material material)
    {
        var text = new StringBuilder("\"SpriteCard\"\n{\n");
        text.Append(System.Globalization.CultureInfo.InvariantCulture, $"\t\"$basetexture\" \"{texturePath}\"\n");
        text.Append("\t\"$vertexcolor\" \"1\"\n");
        text.Append("\t\"$vertexalpha\" \"1\"\n");
        if (material.BlendMode == MaterialBlendMode.Additive)
        {
            text.Append("\t\"$additive\" \"1\"\n");
        }

        text.Append("}\n");
        return text.ToString();
    }

    private static string Unique(string stem, HashSet<string> taken)
    {
        var name = stem;
        for (var suffix = 2; !taken.Add(name); suffix++)
        {
            name = FormattableString.Invariant($"{stem}_{suffix}");
        }

        return name;
    }

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
}
