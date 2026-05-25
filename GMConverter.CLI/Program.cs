using System.CommandLine;
using System.Text;
using GMConverter.Exporters;
using GMConverter.Importers;
using GMConverter.Plugins;
using GMConverter.SDK.Common;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Importers;
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
        var inputFormatOption = RequiredOption<string>("--input-format", "Input model format. Supported: opt, mdl, psk, mow.");
        var outputFormatOption = RequiredOption<string>("--output-format", "Output format. Supported: info, obj, glb, gltf, source, mdl.");
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
        var animationPathOption = new Option<string>("--animation-path")
        {
            Description = "Optional PSA animation file to import alongside a PSK/PSKX mesh."
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
            animationPathOption,
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
            animationPath: parseResult.GetValue(animationPathOption),
            scaleFactor: parseResult.GetValue(scaleOption),
            noScale: parseResult.GetValue(noScaleOption),
            axisModeText: parseResult.GetValue(axisModeOption),
            buildMaterials: !parseResult.GetValue(noMaterialsOption),
            generatePhysics: parseResult.GetValue(physicsOption),
            physicsModeText: parseResult.GetValue(physicsModeOption),
            coacdThreshold: parseResult.GetValue(coacdThresholdOption),
            maxConvexPieces: parseResult.GetValue(maxConvexPiecesOption),
            maxHullVertices: parseResult.GetValue(maxHullVerticesOption),
            physicsMass: parseResult.GetValue(physicsMassOption)));

        return rootCommand;
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
        string? animationPath,
        float scaleFactor,
        bool noScale,
        string? axisModeText,
        bool buildMaterials,
        bool generatePhysics,
        string? physicsModeText,
        float coacdThreshold,
        int maxConvexPieces,
        int maxHullVertices,
        float physicsMass)
    {
        inputFormat = NormalizeFormat(inputFormat, "input-format");
        outputFormat = NormalizeFormat(outputFormat, "output-format");

        var fullInputPath = RequireInputFile(inputPath, inputFormat);
        baseName ??= Path.GetFileNameWithoutExtension(fullInputPath);
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole());
        var importer = GetImporter(inputFormat, loggerFactory);
        var parseOptions = new ModelParseOptions(
            GetScaleFactor(scaleFactor, noScale),
            NormalizeAxisMode(axisModeText),
            CreateMaterialResolveOptions(materialDirectory),
            CreateAnimationPath(animationPath));

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
                    outputFormat is "glb");
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
                    maxHullVertices);
                return 0;

            default:
                throw new ArgumentException("Option --output-format must be 'info', 'obj', 'glb', 'gltf', 'source', or 'mdl'.");
        }
    }

    private static void RunObj(Model model, string outputDirectory, string baseName)
    {
        Directory.CreateDirectory(outputDirectory);

        Console.WriteLine($"Writing OBJ output to {outputDirectory}");
        new OBJExporter().Export(model, outputDirectory, baseName, ExportOptions.Empty);
    }

    private static void RunGltf(Model model, string outputDirectory, string baseName, bool binary)
    {
        Directory.CreateDirectory(outputDirectory);

        Console.WriteLine($"Writing {(binary ? "GLB" : "glTF")} output to {outputDirectory}");
        var options = new ExportOptions(new Dictionary<string, object?>
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
        int maxHullVertices)
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
        exporter.Export(model, outputDirectory, baseName, new ExportOptions(bag));
    }

    private static IImporter GetImporter(string inputFormat, ILoggerFactory? loggerFactory = null)
    {
        return inputFormat switch
        {
            "opt" => new OPTImporter(),
            "mow" => new MOWImporter(loggerFactory),
            _ => PluginHost.Registry.GetImporter(inputFormat)
                ?? throw new ArgumentException($"Option --input-format '{inputFormat}' is not recognized. Built-ins: opt, mow. Plugins (GMConverter.UnrealEngine for psk; GMConverter.SourceEngine for mdl) may contribute additional formats.")
        };
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

    private static string RequireInputFile(string path, string inputFormat)
    {
        var fullPath = FullPath(path);

        if (!File.Exists(fullPath))
        {
            throw new ArgumentException($"File not found: {fullPath}");
        }

        var extension = Path.GetExtension(fullPath);
        var allowedExtensions = inputFormat switch
        {
            "psk" => [".psk", ".pskx", ".ue4scene"],
            "mow" => [".def", ".mdl"],
            _ => new[] { $".{inputFormat}" }
        };

        if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Expected a {string.Join(" or ", allowedExtensions)} file: {fullPath}");
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

    private static string? CreateAnimationPath(string? animationPath)
    {
        if (string.IsNullOrWhiteSpace(animationPath))
        {
            return null;
        }

        var fullPath = FullPath(animationPath);
        if (!File.Exists(fullPath))
        {
            throw new ArgumentException($"Animation file not found: {fullPath}");
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".psa", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Expected a .psa animation file: {fullPath}");
        }

        return fullPath;
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
