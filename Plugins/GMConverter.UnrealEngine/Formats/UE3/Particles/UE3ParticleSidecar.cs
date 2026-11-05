using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Particles;

namespace GMConverter.UnrealEngine.Formats.UE3.Particles;

/// <summary>
/// A Cascade particle system the explorer exported: each emitter's settings, the texture it draws
/// (written beside this file) and how it blends. The UE3 particle importer reads it back; it never
/// leaves this plugin.
/// </summary>
internal sealed record UE3ParticleSidecar(string Name, IReadOnlyList<UE3ParticleSidecar.Emitter> Emitters)
{
    public const string Extension = ".ue3fx";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter() },
        // Materials and meshes travel as file and asset names, so they read back as placeholders;
        // computed properties aren't stored.
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                info =>
                {
                    foreach (var property in info.Properties)
                    {
                        if (property.PropertyType == typeof(Material))
                        {
                            property.CustomConverter = new PlaceholderConverter<Material>(() => new Material("particle"));
                        }
                        else if (property.PropertyType == typeof(Model))
                        {
                            property.CustomConverter = new PlaceholderConverter<Model>(() => null);
                        }
                    }

                    foreach (var property in info.Properties.Where(property => property.Set is null && !IsConstructorParameter(info, property)).ToArray())
                    {
                        info.Properties.Remove(property);
                    }
                },
            },
        },
    };

    public static UE3ParticleSidecar Read(string path)
    {
        return JsonSerializer.Deserialize<UE3ParticleSidecar>(File.ReadAllText(path), _jsonOptions)
            ?? throw new InvalidDataException($"Empty UE3 particle sidecar: {path}");
    }

    public void Write(string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(this, _jsonOptions));
    }

    private static bool IsConstructorParameter(JsonTypeInfo info, JsonPropertyInfo property) =>
        info.Type.GetConstructors().Any(constructor => constructor.GetParameters().Any(parameter =>
            string.Equals(parameter.Name, property.Name, StringComparison.OrdinalIgnoreCase)));

    private sealed class PlaceholderConverter<T>(Func<T?> placeholder) : JsonConverter<T>
        where T : class
    {
        public override bool HandleNull => true;

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Skip();
            return placeholder();
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteNullValue();
    }

    /// <summary>An emitter's settings, its texture file (relative to the sidecar) and blend, and its mesh's name for mesh emitters.</summary>
    public sealed record Emitter(ParticleEmitter Settings, string? Texture, MaterialBlendMode Blend, string? Mesh);
}
