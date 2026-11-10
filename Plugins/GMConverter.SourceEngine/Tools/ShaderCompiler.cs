using GMConverter.SDK.Common;

namespace GMConverter.SourceEngine.Tools;

/// <summary>
/// Compiles HLSL for Garry's Mod's <c>screenspace_general</c> shaders with the standalone
/// <c>ShaderCompile.exe</c> and the Garry's Mod-adjusted Valve headers bundled by
/// meetric1/gmod_shader_guide (downloaded to <c>tools/</c> unless a kit folder is given). Sources
/// named <c>&lt;name&gt;_ps30.hlsl</c> / <c>_vs30.hlsl</c> build to <c>&lt;name&gt;_ps30.vcs</c> / <c>_vs30.vcs</c>.
/// </summary>
internal sealed class ShaderCompiler
{
    private const string _kitZipUrl = "https://github.com/meetric1/gmod_shader_guide/archive/refs/heads/main.zip";
    private const string _compilerName = "ShaderCompile.exe";

    private readonly string _kitDirectory;

    private ShaderCompiler(string kitDirectory)
    {
        _kitDirectory = kitDirectory;
    }

    /// <summary>The kit folder holding ShaderCompile.exe and the headers, if one is already on disk.</summary>
    public static string? TryFindLocalDefault() => FindKit(ToolDownloads.GetToolDirectory("gmod-shader-kit"));

    /// <summary>A compiler for the kit in <paramref name="kitDirectory"/>, or the downloaded one when null.</summary>
    public static ShaderCompiler Resolve(string? kitDirectory)
    {
        if (!string.IsNullOrWhiteSpace(kitDirectory))
        {
            return new ShaderCompiler(FindKit(kitDirectory)
                ?? throw new GMConverterException($"{kitDirectory} has no {_compilerName} with common_ps_fxc.h beside it."));
        }

        var toolDirectory = ToolDownloads.GetToolDirectory("gmod-shader-kit");
        if (FindKit(toolDirectory) is { } existing)
        {
            return new ShaderCompiler(existing);
        }

        ToolDownloads.DownloadAndExtractAsync(_kitZipUrl, toolDirectory).GetAwaiter().GetResult();
        return new ShaderCompiler(FindKit(toolDirectory)
            ?? throw new GMConverterException($"Downloaded the Garry's Mod shader kit, but {_compilerName} was not found in {toolDirectory}."));
    }

    /// <summary>Compiles each (file name, HLSL) into <paramref name="outputDirectory"/>, returning the .vcs paths.</summary>
    public IReadOnlyList<string> Compile(IReadOnlyList<(string FileName, string Source)> shaders, string outputDirectory)
    {
        var work = Path.Join(Path.GetTempPath(), "GMConverter.Shaders", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            foreach (var header in Directory.EnumerateFiles(_kitDirectory, "*.h"))
            {
                File.Copy(header, Path.Join(work, Path.GetFileName(header)));
            }

            List<string> written = [];
            Directory.CreateDirectory(outputDirectory);
            foreach (var (fileName, source) in shaders)
            {
                File.WriteAllText(Path.Join(work, fileName), source);
                var version = fileName.EndsWith("30.hlsl", StringComparison.OrdinalIgnoreCase) ? "30" : "20b";
                ProcessRunner.Run(Path.Join(_kitDirectory, _compilerName), ["/O", "3", "-ver", version, "-shaderpath", work, fileName], work);
                var vcsName = Path.GetFileNameWithoutExtension(fileName) + ".vcs";
                var built = Path.Join(work, "shaders", "fxc", vcsName);
                if (!File.Exists(built))
                {
                    throw new GMConverterException($"{_compilerName} could not compile {fileName}.");
                }

                var target = Path.Join(outputDirectory, vcsName);
                File.Copy(built, target, overwrite: true);
                written.Add(target);
            }

            return written;
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    // The folder under root holding ShaderCompile.exe next to the GMod-adjusted headers.
    private static string? FindKit(string root)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory.EnumerateFiles(root, _compilerName, SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .FirstOrDefault(directory => directory is not null && File.Exists(Path.Join(directory, "common_ps_fxc.h")));
    }
}
