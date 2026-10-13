using System.CommandLine;
using System.Globalization;
using System.Text;
using GMConverter.Core.Formats;
using GMConverter.Core.Pipeline;
using GMConverter.Core.Plugins;
using GMConverter.SDK.Common;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Options;
using Microsoft.Extensions.Logging;

namespace GMConverter.CLI;

internal static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // Plugin load is observability that belongs in the same stream as the rest of the CLI's
        // output. A console logger at Information level surfaces plugin discovery + load failures
        // alongside conversion progress, so a user running the CLI sees plugin issues immediately
        // instead of hunting for them.
        using var pluginLoggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddConsole();
        });
        PluginHost.Initialize(PluginHost.DefaultDirectory, pluginLoggerFactory);

        var rootCommand = CreateRootCommand();

        try
        {
            return rootCommand.Parse(args).Invoke(new InvocationConfiguration
            {
                EnableDefaultExceptionHandler = false
            });
        }
        catch (GMConverterException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static RootCommand CreateRootCommand()
    {
        var inputFormatOption = RequiredOption<string>("--input-format", "Input model format. Supported: " + string.Join(", ", FormatCatalog.Importers.Select(importer => importer.Format)) + ".");
        var outputFormatOption = RequiredOption<string>("--output-format", "Output format. Supported: " + string.Join(", ", FormatCatalog.Exports.Select(format => format.Format).Prepend("info")) + ". Source is an alias for mdl.");
        var inputPathOption = RequiredOption<string>("--input-path", "Path to the input model file.");
        var outputPathOption = new Option<string>("--output-path")
        {
            Description = "Directory for generated output files. Required except when --output-format is info."
        };
        var nameOption = new Option<string>("--name")
        {
            Description = "Override generated file names."
        };
        var materialDirectoryOption = new Option<string>("--material-dir")
        {
            Description = "Optional directory to search recursively for sidecar materials and textures."
        };
        var scaleOption = new Option<float>("--scale")
        {
            Description = "Scale exported geometry.",
            DefaultValueFactory = _ => 1.0f
        };
        var noScaleOption = new Option<bool>("--no-scale")
        {
            Description = "Equivalent to --scale 1; kept for command compatibility."
        };
        var axisModeOption = new Option<string>("--axis-mode")
        {
            Description = "Input model axis convention. Supported: auto, z-up, y-up.",
            DefaultValueFactory = _ => "auto"
        };

        var rootCommand = new RootCommand("Convert model assets into Source Engine compile inputs for Garry's Mod.")
        {
            inputFormatOption,
            outputFormatOption,
            inputPathOption,
            outputPathOption,
            nameOption,
            materialDirectoryOption,
            scaleOption,
            noScaleOption,
            axisModeOption
        };

        // Importer flags carry an extra "import" segment because a format can have both an importer
        // and an exporter (mdl does), and their option keys must not collide.
        var usedNames = rootCommand.Options.SelectMany(option => option.Aliases.Prepend(option.Name)).ToHashSet(StringComparer.Ordinal);
        var importerArguments = new List<SchemaArgument>();
        foreach (var importer in FormatCatalog.Importers)
        {
            AddSchemaArguments(rootCommand, $"{importer.Format}-import", importer.Format, importer.OptionSchema, usedNames, importerArguments);
        }
        var exporterArguments = new List<SchemaArgument>();
        // Formats that share an exporter (glb/gltf, mdl/source) share its flags, named after the
        // exporter; options a format sets itself, like glTF's binary/text choice, get no flag.
        foreach (var format in FormatCatalog.DistinctExporters)
        {
            AddSchemaArguments(rootCommand, format.Exporter.Format, format.Exporter.Format, format.OptionSchema, usedNames, exporterArguments);
        }

        rootCommand.SetAction(parseResult => Run(
            inputFormat: parseResult.GetRequiredValue(inputFormatOption),
            outputFormat: parseResult.GetRequiredValue(outputFormatOption),
            inputPath: parseResult.GetRequiredValue(inputPathOption),
            outputPath: parseResult.GetValue(outputPathOption),
            baseName: parseResult.GetValue(nameOption),
            materialDirectory: parseResult.GetValue(materialDirectoryOption),
            scaleFactor: parseResult.GetValue(scaleOption),
            noScale: parseResult.GetValue(noScaleOption),
            axisModeText: parseResult.GetValue(axisModeOption),
            importerOptions: ReadSchemaOptions(NormalizeInputFormat(parseResult.GetRequiredValue(inputFormatOption)), "input", importerArguments, parseResult),
            exporterOptions: ReadSchemaOptions(NormalizeOutputFormat(parseResult.GetRequiredValue(outputFormatOption)), "output", exporterArguments, parseResult)));

        return rootCommand;
    }

    private static void AddSchemaArguments(
        RootCommand rootCommand,
        string prefix,
        string format,
        OptionSchema schema,
        HashSet<string> usedNames,
        List<SchemaArgument> arguments)
    {
        foreach (var descriptor in schema.AllOptions)
        {
            var isBool = descriptor.Type == OptionType.Bool;
            var argument = CreateSchemaOption($"--{prefix}-{descriptor.Key.Replace(':', '-')}", descriptor.Description ?? descriptor.Label, isBool);
            usedNames.Add(argument.Name);
            // An alias another option already claimed stays with its first owner.
            foreach (var alias in descriptor.Aliases.Select(alias => $"--{alias}").Where(usedNames.Add))
            {
                if (isBool && alias.StartsWith("--no-", StringComparison.Ordinal))
                {
                    // A no- alias is its own flag because it means the opposite value.
                    var negated = CreateSchemaOption(alias, $"Turns off {argument.Name}.", isBool);
                    rootCommand.Options.Add(negated);
                    arguments.Add(new SchemaArgument(format, descriptor, negated, Negated: true));
                }
                else
                {
                    argument.Aliases.Add(alias);
                }
            }
            rootCommand.Options.Add(argument);
            arguments.Add(new SchemaArgument(format, descriptor, argument));
        }
    }

    // Boolean flags also work bare (--physics means --physics true).
    private static Option<string> CreateSchemaOption(string name, string description, bool isBool)
    {
        return new Option<string>(name)
        {
            Description = description,
            Arity = isBool ? ArgumentArity.ZeroOrOne : ArgumentArity.ExactlyOne,
        };
    }

    private static string NormalizeInputFormat(string inputFormat)
    {
        return inputFormat.Trim().ToLowerInvariant();
    }

    // Exporter flags are named after the exporter, so map a format name to its exporter's.
    private static string NormalizeOutputFormat(string outputFormat)
    {
        return FormatCatalog.FindExport(outputFormat)?.Exporter.Format ?? outputFormat.Trim().ToLowerInvariant();
    }

    private static OptionValues ReadSchemaOptions(
        string format,
        string direction,
        IEnumerable<SchemaArgument> arguments,
        ParseResult parseResult)
    {
        var values = new Dictionary<string, object?>();
        foreach (var (argumentFormat, descriptor, argument, negated) in arguments)
        {
            if (parseResult.GetResult(argument) is not { } result)
            {
                continue;
            }
            var text = result.Tokens.Count == 0 && descriptor.Type == OptionType.Bool
                ? "true"
                : parseResult.GetValue(argument);
            if (text is null)
            {
                continue;
            }
            if (!string.Equals(argumentFormat, format, StringComparison.OrdinalIgnoreCase))
            {
                throw new GMConverterException($"Option {argument.Name} does not apply to {format} {direction}.");
            }
            object value = descriptor.Type switch
            {
                OptionType.String or OptionType.Path => text,
                OptionType.Bool when bool.TryParse(text, out var parsed) => parsed != negated,
                OptionType.Int when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                OptionType.Float when float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && float.IsFinite(parsed) => parsed,
                OptionType.Enum when descriptor.Choices?.FirstOrDefault(choice => string.Equals(choice, text, StringComparison.OrdinalIgnoreCase)) is { } choice => choice,
                _ => throw new GMConverterException($"Invalid value for {argument.Name}: {text}")
            };
            if (value is int or float)
            {
                var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (descriptor.Minimum is { } minimum && number < (double)minimum ||
                    descriptor.Maximum is { } maximum && number > (double)maximum)
                {
                    throw new GMConverterException($"Value for {argument.Name} is outside its allowed range: {text}");
                }
            }
            values[descriptor.Key] = value;
        }
        return new OptionValues(values);
    }

    private static int Run(
        string inputFormat,
        string outputFormat,
        string inputPath,
        string? outputPath,
        string? baseName,
        string? materialDirectory,
        float scaleFactor,
        bool noScale,
        string? axisModeText,
        OptionValues importerOptions,
        OptionValues exporterOptions)
    {
        inputFormat = NormalizeFormat(inputFormat, "input-format");
        outputFormat = NormalizeFormat(outputFormat, "output-format");

        var importer = FormatCatalog.FindImporter(inputFormat)
            ?? throw new ArgumentException($"Option --input-format '{inputFormat}' is not recognized. Formats are contributed by plugins: {string.Join(", ", FormatCatalog.Importers.Select(importer => importer.Format))}.");

        if (outputFormat is "info")
        {
            Console.WriteLine(importer.Summarize(FormatCatalog.RequireInputFile(inputPath, importer)));
            return 0;
        }

        var request = new AssetRequest(inputFormat, inputPath)
        {
            OutputFormat = outputFormat,
            OutputDirectory = RequireOutputPath(outputPath, outputFormat),
            BaseName = baseName,
            ImporterOptions = importerOptions,
            ExporterOptions = exporterOptions,
            ScaleFactor = GetScaleFactor(scaleFactor, noScale),
            AxisMode = NormalizeAxisMode(axisModeText),
            MaterialDirectory = materialDirectory,
        };
        var result = new AssetJobRunner().Run(new AssetJob(request)).Results.Single();
        Console.WriteLine($"Wrote {outputFormat} output to {result.Destination.Directory}");
        return 0;
    }

    private static Option<T> RequiredOption<T>(string name, string description)
    {
        return new Option<T>(name)
        {
            Description = description,
            Required = true
        };
    }

    private static string NormalizeFormat(string value, string optionName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Option --{optionName} cannot be empty.");
        }

        return value.Trim().ToLowerInvariant();
    }

    private static string RequireOutputPath(string? path, string outputFormat)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException($"Option --output-path is required when --output-format is '{outputFormat}'.");
        }

        return path;
    }

    private static float GetScaleFactor(float scaleFactor, bool noScale)
    {
        if (noScale && Math.Abs(scaleFactor - 1.0f) > 0.000001f)
        {
            throw new ArgumentException("Options --scale and --no-scale cannot be used together.");
        }

        if (scaleFactor <= 0)
        {
            throw new ArgumentException("Option --scale must be greater than zero.");
        }

        return scaleFactor;
    }

    private static ModelAxisMode NormalizeAxisMode(string? axisModeText)
    {
        return axisModeText?.Trim().ToLowerInvariant() switch
        {
            null or "" or "auto" => ModelAxisMode.Auto,
            "z" or "z-up" or "zup" => ModelAxisMode.ZUp,
            "y" or "y-up" or "yup" => ModelAxisMode.YUp,
            _ => throw new ArgumentException("Option --axis-mode must be 'auto', 'z-up', or 'y-up'.")
        };
    }
}
