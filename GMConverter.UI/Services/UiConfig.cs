using System.Globalization;
using GMConverter.SDK.Common;

namespace GMConverter.UI.Services;

internal sealed record UiConfig(
    string? InputFormat,
    string? OutputFormat,
    string? InputPath,
    string? OutputPath,
    string? BaseName,
    string? MaterialDirectory,
    float? Scale,
    bool? NoScale,
    string? AxisMode,
    IReadOnlyDictionary<string, string> OptionValues)
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

    /// <summary>
    /// Folds a config key or option name to lowercase with separators removed, so
    /// <c>animation-path</c>, <c>Animation_Path</c> and <c>animationPath</c> all match.
    /// </summary>
    internal static string NormalizeKey(string key)
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
        public string? MaterialDirectory { get; private set; }
        public float? Scale { get; private set; }
        public bool? NoScale { get; private set; }
        public string? AxisMode { get; private set; }
        public Dictionary<string, string> OptionValues { get; } = new(StringComparer.Ordinal);

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
                case "gamedir":
                case "gamedirectory":
                case "enginedir":
                case "enginedirectory":
                    break;
                case "materialdir":
                case "materialdirectory":
                    MaterialDirectory = EmptyToNull(value);
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
                default:
                    // Not a host setting: keep it for the selected importer or exporter, which
                    // match it against their option keys and aliases.
                    OptionValues[key] = value;
                    break;
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
                MaterialDirectory,
                Scale,
                NoScale,
                AxisMode,
                OptionValues);
        }

        private static string? EmptyToNull(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
