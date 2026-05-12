using System.Globalization;
using GMConverter.Common;

namespace GMConverter.UI.Services;

internal sealed record UiConfig(
    string? InputFormat,
    string? OutputFormat,
    string? InputPath,
    string? OutputPath,
    string? BaseName,
    string? ModelPath,
    string? GameDirectory,
    string? EngineDirectory,
    string? MaterialDirectory,
    string? AnimationPath,
    float? Scale,
    bool? NoScale,
    string? AxisMode,
    bool? NoMaterials,
    bool? Physics,
    string? PhysicsMode,
    float? PhysicsMass,
    float? CoacdThreshold,
    int? MaxConvexPieces,
    int? MaxHullVertices)
{
    public const string DefaultFileName = "gmconverter.ini";

    public static UiConfig Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new GMConverterException("Config path cannot be empty.");
        }

        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        if (!File.Exists(fullPath))
        {
            throw new GMConverterException($"Config file not found: {fullPath}");
        }

        var builder = new Builder();
        var lineNumber = 0;

        foreach (var rawLine in File.ReadLines(fullPath))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';') ||
                line.StartsWith('[') && line.EndsWith(']'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex < 1)
            {
                throw new GMConverterException($"Invalid config line {lineNumber}: expected key = value.");
            }

            var key = NormalizeKey(line[..separatorIndex]);
            var value = Unquote(line[(separatorIndex + 1)..].Trim());
            builder.Set(fullPath, lineNumber, key, value);
        }

        return builder.Build();
    }

    public static string? FindDefaultPath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.CurrentDirectory, DefaultFileName),
            Path.Combine(AppContext.BaseDirectory, DefaultFileName),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GMConverter",
                DefaultFileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string NormalizeKey(string key)
    {
        return string.Concat(key.Trim().Where(c => c is not '-' and not '_' and not '.' && !char.IsWhiteSpace(c)))
            .ToLowerInvariant();
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    private static bool ParseBool(string path, int lineNumber, string key, string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "yes" or "on" or "1" => true,
            "false" or "no" or "off" or "0" => false,
            _ => throw new GMConverterException(
                $"Invalid boolean value for {key} in {path} line {lineNumber}: {value}")
        };
    }

    private static float ParseFloat(string path, int lineNumber, string key, string value)
    {
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        throw new GMConverterException($"Invalid number for {key} in {path} line {lineNumber}: {value}");
    }

    private static int ParseInt(string path, int lineNumber, string key, string value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        throw new GMConverterException($"Invalid integer for {key} in {path} line {lineNumber}: {value}");
    }

    private sealed class Builder
    {
        public string? InputFormat { get; private set; }
        public string? OutputFormat { get; private set; }
        public string? InputPath { get; private set; }
        public string? OutputPath { get; private set; }
        public string? BaseName { get; private set; }
        public string? ModelPath { get; private set; }
        public string? GameDirectory { get; private set; }
        public string? EngineDirectory { get; private set; }
        public string? MaterialDirectory { get; private set; }
        public string? AnimationPath { get; private set; }
        public float? Scale { get; private set; }
        public bool? NoScale { get; private set; }
        public string? AxisMode { get; private set; }
        public bool? NoMaterials { get; private set; }
        public bool? Physics { get; private set; }
        public string? PhysicsMode { get; private set; }
        public float? PhysicsMass { get; private set; }
        public float? CoacdThreshold { get; private set; }
        public int? MaxConvexPieces { get; private set; }
        public int? MaxHullVertices { get; private set; }

        public void Set(string path, int lineNumber, string key, string value)
        {
            switch (key)
            {
                case "inputformat":
                    InputFormat = EmptyToNull(value);
                    break;
                case "outputformat":
                    OutputFormat = EmptyToNull(value);
                    break;
                case "inputpath":
                    InputPath = EmptyToNull(value);
                    break;
                case "outputpath":
                    OutputPath = EmptyToNull(value);
                    break;
                case "name":
                case "basename":
                    BaseName = EmptyToNull(value);
                    break;
                case "modelpath":
                    ModelPath = EmptyToNull(value);
                    break;
                case "gamedir":
                case "gamedirectory":
                    GameDirectory = EmptyToNull(value);
                    break;
                case "enginedir":
                case "enginedirectory":
                    EngineDirectory = EmptyToNull(value);
                    break;
                case "materialdir":
                case "materialdirectory":
                    MaterialDirectory = EmptyToNull(value);
                    break;
                case "animationpath":
                case "animationfile":
                    AnimationPath = EmptyToNull(value);
                    break;
                case "scale":
                    Scale = ParseFloat(path, lineNumber, key, value);
                    break;
                case "noscale":
                    NoScale = ParseBool(path, lineNumber, key, value);
                    break;
                case "axismode":
                    AxisMode = EmptyToNull(value);
                    break;
                case "nomaterials":
                    NoMaterials = ParseBool(path, lineNumber, key, value);
                    break;
                case "physics":
                    Physics = ParseBool(path, lineNumber, key, value);
                    break;
                case "physicsmode":
                    PhysicsMode = EmptyToNull(value);
                    break;
                case "physicsmass":
                    PhysicsMass = ParseFloat(path, lineNumber, key, value);
                    break;
                case "coacdthreshold":
                    CoacdThreshold = ParseFloat(path, lineNumber, key, value);
                    break;
                case "maxconvexpieces":
                    MaxConvexPieces = ParseInt(path, lineNumber, key, value);
                    break;
                case "coacdmaxhullvertices":
                case "maxhullvertices":
                    MaxHullVertices = ParseInt(path, lineNumber, key, value);
                    break;
                default:
                    throw new GMConverterException($"Unknown config key in {path} line {lineNumber}: {key}");
            }
        }

        public UiConfig Build()
        {
            return new UiConfig(
                InputFormat,
                OutputFormat,
                InputPath,
                OutputPath,
                BaseName,
                ModelPath,
                GameDirectory,
                EngineDirectory,
                MaterialDirectory,
                AnimationPath,
                Scale,
                NoScale,
                AxisMode,
                NoMaterials,
                Physics,
                PhysicsMode,
                PhysicsMass,
                CoacdThreshold,
                MaxConvexPieces,
                MaxHullVertices);
        }

        private static string? EmptyToNull(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
