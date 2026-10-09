using System.CommandLine;
using System.Globalization;
using System.Text;
using GMConverter.Exporters;
using GMConverter.Plugins;
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
        var modelPathOption = new Option<string>("--model-path")
        {
            Description = "Output MDL path under the game models directory."
        };
        var studioMdlPathOption = new Option<string>("--studiomdl-path")
        {
            Description = "Optional StudioMDL executable override. Downloads StudioMDL-CE into tools when omitted."
        };
        var vtfCmdPathOption = new Option<string>("--vtfcmd-path")
        {
            Description = "Optional VTFCmd executable override. Downloads VTFEdit Reloaded into tools when omitted and materials are built."
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
        var noMaterialsOption = new Option<bool>("--no-materials")
        {
            Description = "Skip VTF/VMT compilation."
        };
        var physicsOption = new Option<bool>("--physics")
        {
            Description = "Generate a simple bounds collision mesh."
        };
        var physicsModeOption = new Option<string>("--physics-mode")
        {
            Description = "Physics mode. Supported: bounds, coacd."
        };
        var coacdThresholdOption = new Option<float>("--coacd-threshold")
        {
            Description = "CoACD termination threshold from 0.01 to 1.",
            DefaultValueFactory = _ => 0.05f
        };
        var maxConvexPiecesOption = new Option<int>("--max-convex-pieces")
        {
            Description = "Maximum CoACD convex hull count. Use -1 for no limit.",
            DefaultValueFactory = _ => 16
        };
        var maxHullVerticesOption = new Option<int>("--coacd-max-hull-vertices")
        {
            Description = "Maximum vertices per CoACD hull.",
            DefaultValueFactory = _ => 16
        };
        var physicsMassOption = new Option<float>("--physics-mass")
        {
            Description = "Physics mass.",
            DefaultValueFactory = _ => 100.0f
        };

        var rootCommand = new RootCommand("Convert model assets into Source Engine compile inputs for Garry's Mod.")
        {
            inputFormatOption,
            outputFormatOption,
            inputPathOption,
            outputPathOption,
            nameOption,
            modelPathOption,
            studioMdlPathOption,
            vtfCmdPathOption,
            materialDirectoryOption,
            scaleOption,
            noScaleOption,
            axisModeOption,
            noMaterialsOption,
            physicsOption,
            physicsModeOption,
            coacdThresholdOption,
            maxConvexPiecesOption,
            maxHullVerticesOption,
            physicsMassOption
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
            modelPath: parseResult.GetValue(modelPathOption),
            studioMdlPath: parseResult.GetValue(studioMdlPathOption),
            vtfCmdPath: parseResult.GetValue(vtfCmdPathOption),
            materialDirectory: parseResult.GetValue(materialDirectoryOption),
            scaleFactor: parseResult.GetValue(scaleOption),
            noScale: parseResult.GetValue(noScaleOption),
            axisModeText: parseResult.GetValue(axisModeOption),
            buildMaterials: !parseResult.GetValue(noMaterialsOption),
            generatePhysics: parseResult.GetValue(physicsOption),
            physicsModeText: parseResult.GetValue(physicsModeOption),
            coacdThreshold: parseResult.GetValue(coacdThresholdOption),
            maxConvexPieces: parseResult.GetValue(maxConvexPiecesOption),
            maxHullVertices: parseResult.GetValue(maxHullVerticesOption),
            physicsMass: parseResult.GetValue(physicsMassOption),
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
            var argument = new Option<string>($"--{prefix}-{descriptor.Key.Replace(':', '-')}")
            {
                Description = descriptor.Description ?? descriptor.Label
            };
            usedNames.Add(argument.Name);
            // An alias another option already claimed stays with its first owner.
            foreach (var alias in descriptor.Aliases.Select(alias => $"--{alias}").Where(usedNames.Add))
            {
                argument.Aliases.Add(alias);
            }
            rootCommand.Options.Add(argument);
            arguments.Add(new SchemaArgument(format, descriptor, argument));
        }
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
        foreach (var (argumentFormat, descriptor, argument) in arguments)
        {
            var text = parseResult.GetValue(argument);
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
                OptionType.Bool when bool.TryParse(text, out var parsed) => parsed,
                OptionType.Int when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                OptionType.Float when float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && float.IsFinite(parsed) => parsed,
                OptionType.Enum when descriptor.Choices?.Contains(text) == true => text,
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
        string? modelPath,
        string? studioMdlPath,
        string? vtfCmdPath,
        string? materialDirectory,
        float scaleFactor,
        bool noScale,
        string? axisModeText,
        bool buildMaterials,
        bool generatePhysics,
        string? physicsModeText,
        float coacdThreshold,
        int maxConvexPieces,
        int maxHullVertices,
        float physicsMass,
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

            case "source":
            case "mdl":
                RunMdl(
                    importer.Parse(fullInputPath, parseOptions),
                    RequireOutputPath(outputPath, outputFormat),
                    baseName,
                    modelPath ?? $"gmconverter/{SanitizePathToken(baseName)}.mdl",
                    studioMdlPath,
                    vtfCmdPath,
                    buildMaterials,
                    generatePhysics,
                    physicsModeText,
                    physicsMass,
                    coacdThreshold,
                    maxConvexPieces,
                    maxHullVertices,
                    exporterOptions);
                return 0;

            default:
                var exporter = PluginHost.Registry.GetExporter(outputFormat)
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

    private static void RunMdl(
        Model model,
        string outputDirectory,
        string baseName,
        string modelPath,
        string? studioMdlPath,
        string? vtfCmdPath,
        bool buildMaterials,
        bool generatePhysics,
        string? physicsModeText,
        float physicsMass,
        float coacdThreshold,
        int maxConvexPieces,
        int maxHullVertices,
        OptionValues overrides)
    {
        Directory.CreateDirectory(outputDirectory);

        var exporter = PluginHost.Registry.GetExporter("mdl")
            ?? throw new GMConverterException("Source plugin not loaded: cannot produce MDL output. Install GMConverter.SourceEngine.");

        Console.WriteLine($"Writing Source compile workspace to {outputDirectory}");
        var bag = new Dictionary<string, object?>
        {
            ["modelPath"] = modelPath,
            ["studioMdlPath"] = studioMdlPath,
            ["vtfCmdPath"] = vtfCmdPath,
            ["buildMaterials"] = buildMaterials,
        };
        if (generatePhysics || !string.IsNullOrWhiteSpace(physicsModeText))
        {
            var mode = NormalizePhysicsMode(physicsModeText);
            bag["physics:enabled"] = true;
            bag["physics:mode"] = mode;
            bag["physics:mass"] = physicsMass;
            if (mode == "coacd")
            {
                bag["physics:coacdThreshold"] = coacdThreshold;
                bag["physics:maxConvexPieces"] = maxConvexPieces;
                bag["physics:maxHullVertices"] = maxHullVertices;
            }
        }
        foreach (var (key, value) in overrides.AsDictionary())
        {
            bag[key] = value;
        }
        exporter.Export(model, outputDirectory, baseName, new OptionValues(bag));
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

    private static string SanitizePathToken(string value)
    {
        return string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? char.ToLowerInvariant(c) : '_')).Trim('_');
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

    private static string NormalizePhysicsMode(string? physicsModeText)
    {
        return physicsModeText?.Trim().ToLowerInvariant() switch
        {
            null or "" or "bounds" => "bounds",
            "coacd" => "coacd",
            _ => throw new ArgumentException("Option --physics-mode must be 'bounds' or 'coacd'.")
        };
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
