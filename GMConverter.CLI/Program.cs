using System.CommandLine;
using System.Text;
using GMConverter.Common;
using GMConverter.Exporters;
using GMConverter.Geometry;
using GMConverter.Importers;
using GMConverter.Source;

namespace GMConverter.CLI;

internal static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

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
        var inputFormatOption = RequiredOption<string>("--input-format", "Input model format. Supported: opt, mdl, psk.");
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
        var gameDirectoryOption = new Option<string>("--game-dir")
        {
            Description = "Source game directory containing gameinfo.txt."
        };
        var engineDirectoryOption = new Option<string>("--engine-dir")
        {
            Description = "Optional Source engine root override."
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
            DefaultValueFactory = _ => 0.01f
        };
        var maxConvexPiecesOption = new Option<int>("--max-convex-pieces")
        {
            Description = "Maximum CoACD convex hull count. Use -1 for no limit.",
            DefaultValueFactory = _ => 32
        };
        var maxHullVerticesOption = new Option<int>("--coacd-max-hull-vertices")
        {
            Description = "Maximum vertices per CoACD hull.",
            DefaultValueFactory = _ => 32
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
            gameDirectoryOption,
            engineDirectoryOption,
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
            gameDirectory: parseResult.GetValue(gameDirectoryOption),
            engineDirectory: parseResult.GetValue(engineDirectoryOption),
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
        string? gameDirectory,
        string? engineDirectory,
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

        var importer = GetImporter(inputFormat);
        var fullInputPath = RequireInputFile(inputPath, inputFormat);
        baseName ??= Path.GetFileNameWithoutExtension(fullInputPath);
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
                    engineDirectory,
                    gameDirectory,
                    buildMaterials,
                    CreatePhysicsOptions(generatePhysics, physicsModeText, physicsMass, coacdThreshold, maxConvexPieces, maxHullVertices));
                return 0;

            default:
                throw new ArgumentException("Option --output-format must be 'info', 'obj', 'glb', 'gltf', 'source', or 'mdl'.");
        }
    }

    private static void RunObj(Model model, string outputDirectory, string baseName)
    {
        Directory.CreateDirectory(outputDirectory);

        Console.WriteLine($"Writing OBJ output to {outputDirectory}");
        new OBJExporter().Export(model, outputDirectory, baseName, new OBJExportOptions());
    }

    private static void RunGltf(Model model, string outputDirectory, string baseName, bool binary)
    {
        Directory.CreateDirectory(outputDirectory);

        Console.WriteLine($"Writing {(binary ? "GLB" : "glTF")} output to {outputDirectory}");
        new GLTFExporter().Export(model, outputDirectory, baseName, new GLTFExportOptions(binary));
    }

    private static void RunMdl(
        Model model,
        string outputDirectory,
        string baseName,
        string modelPath,
        string? engineDirectory,
        string? gameDirectory,
        bool buildMaterials,
        PhysicsOptions? physicsOptions)
    {
        Directory.CreateDirectory(outputDirectory);

        Console.WriteLine($"Writing Source compile workspace to {outputDirectory}");
        new MDLExporter().Export(
            model,
            outputDirectory,
            baseName,
            new MDLExportOptions(modelPath, engineDirectory, gameDirectory, buildMaterials, physicsOptions));
    }

    private static IImporter GetImporter(string inputFormat)
    {
        return inputFormat switch
        {
            "opt" => new OPTImporter(),
            "mdl" => new MDLImporter(),
            "psk" => new PSKImporter(),
            _ => throw new ArgumentException("Option --input-format must be 'opt', 'mdl', or 'psk'.")
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
        var allowedExtensions = inputFormat is "psk" ? [".psk", ".pskx"] : new[] { $".{inputFormat}" };

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

    private static PhysicsOptions? CreatePhysicsOptions(
        bool generatePhysics,
        string? physicsModeText,
        float mass,
        float threshold,
        int maxConvexPieces,
        int maxHullVertices)
    {
        if (!generatePhysics && string.IsNullOrWhiteSpace(physicsModeText))
        {
            return null;
        }

        var mode = NormalizePhysicsMode(physicsModeText);

        if (mass <= 0)
        {
            throw new ArgumentException("Option --physics-mass must be greater than zero.");
        }

        if (mode is PhysicsMode.Bounds)
        {
            return new PhysicsOptions(mode, mass, null);
        }

        if (threshold is < 0.01f or > 1.0f)
        {
            throw new ArgumentException("Option --coacd-threshold must be between 0.01 and 1.");
        }

        if (maxConvexPieces is 0 or < -1)
        {
            throw new ArgumentException("Option --max-convex-pieces must be -1 or greater than zero.");
        }

        if (maxHullVertices < 4)
        {
            throw new ArgumentException("Option --coacd-max-hull-vertices must be at least 4.");
        }

        return new PhysicsOptions(mode, mass, new CoacdOptions(threshold, maxConvexPieces, maxHullVertices));
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

    private static PhysicsMode NormalizePhysicsMode(string? physicsModeText)
    {
        return physicsModeText?.Trim().ToLowerInvariant() switch
        {
            null or "" or "bounds" => PhysicsMode.Bounds,
            "coacd" => PhysicsMode.Coacd,
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
