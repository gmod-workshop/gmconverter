using System.CommandLine;
using System.Globalization;
using System.Text;
using GMConverter.Core.Exporters;
using GMConverter.Core.Plugins;
using GMConverter.SDK.Common;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Options;
using Microsoft.Extensions.Logging;

namespace GMConverter.CLI;

internal static class Program
{
    private static readonly string[] _builtInOutputFormats = ["info", "obj", "glb", "gltf"];

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
        var inputFormatOption = RequiredOption<string>("--input-format", "Input model format. Supported: " + string.Join(", ", PluginHost.Registry.Importers.Select(importer => importer.InputFormat)) + ".");
        var outputFormatOption = RequiredOption<string>("--output-format", "Output format. Supported: " + string.Join(", ", _builtInOutputFormats.Concat(PluginHost.Registry.Exporters.Select(exporter => exporter.OutputFormat))) + ". Source is an alias for mdl.");
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
        foreach (var importer in PluginHost.Registry.Importers)
        {
            AddSchemaArguments(rootCommand, $"{importer.InputFormat}-import", importer.InputFormat, importer.OptionSchema, usedNames, importerArguments);
        }
        var exporterArguments = new List<SchemaArgument>();
        foreach (var exporter in new IExporter[] { new GLTFExporter() }.Concat(PluginHost.Registry.Exporters))
        {
            AddSchemaArguments(rootCommand, exporter.OutputFormat, exporter.OutputFormat, exporter.OptionSchema, usedNames, exporterArguments);
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
        foreach (var descriptor in schema.AllOptions.Where(descriptor => !IsHostControlled(format, descriptor)))
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

    // glTF's binary/text choice follows --output-format, so its own flag would be ignored.
    private static bool IsHostControlled(string format, OptionDescriptor descriptor)
    {
        return format == "glb" && descriptor.Key == "binary";
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

    private static string NormalizeOutputFormat(string outputFormat)
    {
        return outputFormat.Trim().ToLowerInvariant() switch
        {
            "source" => "mdl",
            "gltf" => "glb",
            var value => value
        };
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

    // Schema defaults first, then whatever the command line set explicitly.
    private static OptionValues WithDefaults(OptionSchema schema, OptionValues overrides)
    {
        var values = schema.AllOptions.ToDictionary(option => option.Key, option => option.ResolveDefault());
        foreach (var (key, value) in overrides.AsDictionary())
        {
            values[key] = value;
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

        var importer = GetImporter(inputFormat);
        var fullInputPath = RequireInputFile(inputPath, importer);
        baseName ??= Path.GetFileNameWithoutExtension(fullInputPath);
        var parseOptions = new ModelParseOptions(
            GetScaleFactor(scaleFactor, noScale),
            NormalizeAxisMode(axisModeText),
            CreateMaterialResolveOptions(materialDirectory))
        {
            Options = WithDefaults(importer.OptionSchema, importerOptions),
        };

        switch (outputFormat)
        {
            case "info":
                Console.WriteLine(importer.Summarize(fullInputPath));
                return 0;

            case "obj":
                RunObj(importer.Parse(fullInputPath, parseOptions), RequireOutputPath(outputPath, outputFormat), baseName);
                return 0;

            case "glb":
            case "gltf":
                RunGltf(
                    importer.Parse(fullInputPath, parseOptions),
                    RequireOutputPath(outputPath, outputFormat),
                    baseName,
                    outputFormat is "glb",
                    exporterOptions);
                return 0;

            default:
                var exporter = PluginHost.Registry.GetExporter(NormalizeOutputFormat(outputFormat))
                    ?? throw new GMConverterException($"Unsupported output format or plugin not loaded: {outputFormat}");
                var directory = RequireOutputPath(outputPath, outputFormat);
                Directory.CreateDirectory(directory);
                exporter.Export(importer.Parse(fullInputPath, parseOptions), directory, baseName, WithDefaults(exporter.OptionSchema, exporterOptions));
                Console.WriteLine($"Wrote {outputFormat} output to {directory}");
                return 0;
        }
    }

    private static void RunObj(Model model, string outputDirectory, string baseName)
    {
        Directory.CreateDirectory(outputDirectory);

        Console.WriteLine($"Writing OBJ output to {outputDirectory}");
        new OBJExporter().Export(model, outputDirectory, baseName, OptionValues.Empty);
    }

    private static void RunGltf(Model model, string outputDirectory, string baseName, bool binary, OptionValues overrides)
    {
        Directory.CreateDirectory(outputDirectory);

        Console.WriteLine($"Writing {(binary ? "GLB" : "glTF")} output to {outputDirectory}");
        var options = new OptionValues(new Dictionary<string, object?>(overrides.AsDictionary())
        {
            ["binary"] = binary,
        });
        new GLTFExporter().Export(model, outputDirectory, baseName, options);
    }


    private static IImporter GetImporter(string inputFormat)
    {
        return PluginHost.Registry.GetImporter(inputFormat)
            ?? throw new ArgumentException($"Option --input-format '{inputFormat}' is not recognized. Formats are contributed by plugins: {string.Join(", ", PluginHost.Registry.Importers.Select(importer => importer.InputFormat))}.");
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

    private static string RequireInputFile(string path, IImporter importer)
    {
        var fullPath = FullPath(path);

        if (!File.Exists(fullPath))
        {
            throw new ArgumentException($"File not found: {fullPath}");
        }

        if (!importer.FileExtensions.Contains(Path.GetExtension(fullPath), StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Expected a {string.Join(" or ", importer.FileExtensions)} file: {fullPath}");
        }

        return fullPath;
    }

    private static string RequireOutputPath(string? path, string outputFormat)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException($"Option --output-path is required when --output-format is '{outputFormat}'.");
        }

        return FullPath(path);
    }

    private static string FullPath(string path)
    {
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }


    private static MaterialResolveOptions? CreateMaterialResolveOptions(string? materialDirectory)
    {
        if (string.IsNullOrWhiteSpace(materialDirectory))
        {
            return null;
        }

        var fullPath = FullPath(materialDirectory);
        if (!Directory.Exists(fullPath))
        {
            throw new ArgumentException($"Material directory not found: {fullPath}");
        }

        return new MaterialResolveOptions(fullPath);
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
