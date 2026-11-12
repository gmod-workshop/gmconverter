using System.Security.Cryptography;
using System.Text;
using GMConverter.SDK.Common;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Options;
using GMConverter.SDK.Particles;
using GMConverter.SourceEngine.Particles;
using GMConverter.SourceEngine.Particles.Shaders;
using GMConverter.SourceEngine.Tools;

namespace GMConverter.SourceEngine.Exporters;

/// <summary>
/// Writes a particle effect as a Garry's Mod add-on that draws it the way its source engine does:
/// each emitter's material graph compiled to a pixel shader for <c>screenspace_general_8tex</c>
/// (<see cref="MaterialGraphHlsl"/>), its textures as VTFs, mesh particles' models as vertex data, and
/// a Lua definition the shared client runtime (<c>lua/autorun/client/gmconverter_fx.lua</c>) simulates.
/// Materials without a graph draw their texture tinted by the particle. Needs Garry's Mod 2025.12 or
/// newer, and a shader kit (ShaderCompile and the Garry's Mod headers) to compile with.
/// </summary>
internal sealed class GarrysModEffectExporter : IParticleExporter
{
    public const string MaxTextureSizeOption = "maxTextureSize";
    public const string ShaderKitPathOption = "shaderKitPath";

    private const string _runtimeResource = "GMConverter.SourceEngine.Particles.Lua.gmconverter_fx.lua";
    private const string _spriteShaderResource = "GMConverter.SourceEngine.Particles.Shaders.gmcfx_sprite_vs30.hlsl";
    private const string _meshShaderResource = "GMConverter.SourceEngine.Particles.Shaders.gmcfx_mesh_vs30.hlsl";
    private static readonly UTF8Encoding _utf8NoBom = new(false);

    public string Format => "gmfx";

    public string DisplayName => "Garry's Mod effects (shaders and Lua)";

    public OptionSchema OptionSchema { get; } = new(
    [
        new OptionGroup("effects", "Effects",
        [
            new OptionDescriptor(MaxTextureSizeOption, OptionType.Int, "Max texture size")
            {
                DefaultValue = 1024,
                Description = "Effect textures larger than this are scaled down.",
            },
            new OptionDescriptor(ShaderKitPathOption, OptionType.Path, "Shader kit")
            {
                Description = "Folder with ShaderCompile.exe and the Garry's Mod shader headers (meetric1/gmod_shader_guide). Auto-downloads to tools/ when omitted.",
                DefaultValueFactory = ShaderCompiler.TryFindLocalDefault,
            },
        ]),
    ]);

    public ExportResult Export(ParticleEffect effect, ExportContext context)
    {
        var name = NameHelpers.SanitizeMaterialName(context.BaseName);
        var maxTextureSize = Math.Max(4, context.Options.GetInt(MaxTextureSizeOption, 1024));
        var textureFolder = $"gmconverter/fx/{name}";
        List<string> warnings = [];
        List<string> files = [];
        List<(string FileName, string Source)> shaders = [];

        var spriteShader = Versioned("gmcfx_sprite", Resource(_spriteShaderResource), "vs30", shaders);
        var meshShader = Versioned("gmcfx_mesh", Resource(_meshShaderResource), "vs30", shaders);

        Dictionary<ulong, string> texturePaths = [];
        HashSet<string> textureNames = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<ParticleEmitter, GarrysModEffectData.EmitterMaterial> materials = new(ReferenceEqualityComparer.Instance);
        Dictionary<ParticleEmitter, string> meshes = new(ReferenceEqualityComparer.Instance);
        foreach (var emitter in effect.Emitters)
        {
            var graph = emitter.Material.Graph ?? GarrysModEffectData.FallbackGraph(emitter.Material);
            string hlsl;
            try
            {
                hlsl = MaterialGraphHlsl.Write(graph, emitter.Material.BlendMode, $"material {emitter.Material.Name}");
            }
            catch (NotSupportedException ex)
            {
                warnings.Add($"{effect.Name} {emitter.Name}: drawn with its baked texture: {ex.Message}");
                graph = GarrysModEffectData.FallbackGraph(emitter.Material);
                hlsl = MaterialGraphHlsl.Write(graph, emitter.Material.BlendMode, $"material {emitter.Material.Name}");
            }

            var shader = Versioned("gmcfx", hlsl, "ps30", shaders);
            var textures = graph.Textures
                .Select(texture => (Path: WriteTexture(texture, context.OutputDirectory, textureFolder, maxTextureSize, texturePaths, textureNames, files), Linear: !texture.Srgb))
                .ToArray();
            materials[emitter] = new GarrysModEffectData.EmitterMaterial(shader, spriteShader, meshShader, textures, emitter.Material.BlendMode);

            if (emitter.Mesh is { } model)
            {
                var meshPath = $"data_static/gmconverter/fx/{name}/{NameHelpers.SanitizeMaterialName(model.Name)}.dat";
                if (!meshes.ContainsValue(meshPath))
                {
                    files.Add(WriteBytes(context.OutputDirectory, meshPath, GarrysModEffectData.MeshData(model)));
                }

                meshes[emitter] = meshPath;
            }
        }

        try
        {
            var compiler = ShaderCompiler.Resolve(context.Options.GetString(ShaderKitPathOption));
            files.AddRange(compiler.Compile(shaders, Path.Join(context.OutputDirectory, "shaders", "fxc")));
        }
        catch (GMConverterException ex)
        {
            // The HLSL is still written, so the shaders can be built by hand.
            warnings.Add($"{effect.Name}: shaders weren't compiled ({ex.Message}); their HLSL is in shaders/src.");
        }

        foreach (var (fileName, source) in shaders)
        {
            files.Add(WriteText(context.OutputDirectory, $"shaders/src/{fileName}", source));
        }

        var dataPath = $"lua/gmconverter_fx/effects/{name}.lua";
        files.Add(WriteText(context.OutputDirectory, dataPath, GarrysModEffectData.Write(effect, emitter => materials[emitter], emitter => meshes.GetValueOrDefault(emitter))));
        files.Add(WriteText(context.OutputDirectory, "lua/autorun/client/gmconverter_fx.lua", Resource(_runtimeResource)));
        files.Add(WriteText(context.OutputDirectory, $"lua/autorun/gmconverter_fx_{name}.lua", Loader(dataPath)));
        return new ExportResult(files) { Warnings = warnings };
    }

    // Sends the definition to clients and registers it there; the runtime loads itself if autorun hasn't yet.
    private static string Loader(string dataPath)
    {
        var include = dataPath["lua/".Length..];
        return $"""
            -- Generated by GMConverter.

            if SERVER then
            	AddCSLuaFile("{include}")
            	AddCSLuaFile("autorun/client/gmconverter_fx.lua")
            	return
            end

            if not GMConverterFX then include("autorun/client/gmconverter_fx.lua") end
            GMConverterFX.Register(include("{include}"))

            """;
    }

    // Shader names carry a hash of their source: Garry's Mod caches shaders by name until restarted.
    private static string Versioned(string stem, string source, string suffix, List<(string FileName, string Source)> shaders)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..10];
        var name = $"{stem}_{hash}_{suffix}";
        if (!shaders.Any(shader => shader.FileName.StartsWith(name, StringComparison.Ordinal)))
        {
            shaders.Add(($"{name}.hlsl", source));
        }

        return name;
    }

    private static string WriteTexture(
        MaterialGraphTexture graphTexture, string outputDirectory, string folder, int maxTextureSize,
        Dictionary<ulong, string> written, HashSet<string> names, List<string> files)
    {
        var texture = graphTexture.Texture;
        var key = texture.ContentHash() ^ (graphTexture.ClampU ? 1UL : 0) ^ (graphTexture.ClampV ? 2UL : 0);
        if (written.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var stem = NameHelpers.SanitizeMaterialName(texture.Name);
        var file = stem;
        for (var suffix = 2; !names.Add(file); suffix++)
        {
            file = FormattableString.Invariant($"{stem}_{suffix}");
        }

        using var sized = texture.Width > maxTextureSize || texture.Height > maxTextureSize ? texture.Resized(maxTextureSize) : null;
        var source = sized ?? texture;
        var path = Path.Join(outputDirectory, "materials", folder.Replace('/', Path.DirectorySeparatorChar), file + ".vtf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SpriteVtfWriter.Write(path, source.GetRgbaPixels(), source.Width, source.Height, 1, 1, null, graphTexture.ClampU, graphTexture.ClampV);
        files.Add(path);
        written[key] = $"{folder}/{file}";
        return written[key];
    }

    private static string Resource(string name)
    {
        using var stream = typeof(GarrysModEffectExporter).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"{name} is missing from the plugin.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string WriteText(string directory, string relativePath, string contents)
    {
        var path = Path.Join(directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents, _utf8NoBom);
        return path;
    }

    private static string WriteBytes(string directory, string relativePath, byte[] contents)
    {
        var path = Path.Join(directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, contents);
        return path;
    }
}
