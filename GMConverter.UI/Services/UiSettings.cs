using System.Text.Json;
using GMConverter.Common;

namespace GMConverter.UI.Services;

internal sealed record UiSettings(
    string? InputFormat,
    string? OutputFormat,
    string? AxisMode,
    string? ExplorerProfile,
    string? PhysicsMode,
    string? ConfigPath,
    string? InputPath,
    string? AnimationPath,
    string? OutputPath,
    string? BaseName,
    string? ModelPath,
    string? GameDirectory,
    string? EngineDirectory,
    string? MaterialDirectory,
    string? ExplorerRootDirectory,
    string? ExplorerFilter,
    double ScaleFactor,
    bool BuildMaterials,
    bool GeneratePhysics,
    bool PreviewOrthographic,
    bool PreviewWireframe,
    bool PreviewPhysicsOverlay,
    double PhysicsMass,
    double CoacdThreshold,
    int MaxConvexPieces,
    int MaxHullVertices)
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GMConverter",
            "ui-settings.json");

    public static UiSettings? Load()
    {
        var path = SettingsPath;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<UiSettings>(json, _jsonOptions);
            if (settings is null)
            {
                return null;
            }

            using var document = JsonDocument.Parse(json);
            return settings with
            {
                ExplorerProfile = settings.ExplorerProfile ?? ReadLegacyString(document.RootElement, "GameProfile"),
                ExplorerRootDirectory = settings.ExplorerRootDirectory ?? ReadLegacyString(document.RootElement, "ExplorerGameDirectory")
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new GMConverterException($"Failed to load UI settings from {path}: {ex.Message}");
        }
    }

    private static string? ReadLegacyString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) &&
            property.ValueKind is JsonValueKind.String
                ? property.GetString()
                : null;
    }

    public void Save()
    {
        var path = SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, this, _jsonOptions);
    }
}
