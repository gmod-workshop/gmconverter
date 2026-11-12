using System.Diagnostics;
using System.Runtime.InteropServices;
using GMConverter.SDK.Common;
using GMConverter.SDK.Options;

namespace GMConverter.SDK.Audio;

/// <summary>
/// Decodes game audio GMConverter cannot read itself, such as Wwise <c>.wem</c> media or FMOD
/// sound banks, with
/// vgmstream (https://github.com/vgmstream/vgmstream). vgmstream-cli is downloaded into
/// <c>tools/vgmstream</c> the first time it is needed, unless a path to one is given.
/// </summary>
public sealed class VgmstreamDecoder
{
    /// <summary>Name of the importer option holding a vgmstream-cli path; see <see cref="PathOptionDescriptor"/>.</summary>
    public const string PathOption = "vgmstreamPath";

    private const string _repository = "vgmstream/vgmstream";
    private const string _toolName = "vgmstream";

    private readonly string _executable;

    private VgmstreamDecoder(string executable)
    {
        _executable = executable;
    }

    private static string ExecutableName => OperatingSystem.IsWindows() ? "vgmstream-cli.exe" : "vgmstream-cli";

    /// <summary>The <see cref="PathOption"/> option, defaulting to the downloaded vgmstream-cli.</summary>
    public static OptionDescriptor PathOptionDescriptor(string formatName) => new(PathOption, OptionType.Path, "vgmstream override")
    {
        Description = $"Optional vgmstream-cli executable for decoding {formatName}. Auto-downloads to tools/ when omitted.",
        DefaultValueFactory = TryFindLocal,
    };

    /// <summary>The decoder at <paramref name="overridePath"/>, or the downloaded one, downloading it if needed.</summary>
    public static VgmstreamDecoder Resolve(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return new VgmstreamDecoder(ToolDownloads.RequireExecutable(overridePath, ExecutableName));
        }

        return new VgmstreamDecoder(EnsureDownloadedAsync().GetAwaiter().GetResult());
    }

    /// <summary>The downloaded vgmstream-cli, or null when it has not been downloaded yet.</summary>
    public static string? TryFindLocal()
    {
        return ToolDownloads.FindExecutable(ToolDownloads.GetToolDirectory(_toolName), ExecutableName);
    }

    /// <summary>Decodes <paramref name="inputPath"/> into a clip named <paramref name="name"/>.</summary>
    public SoundClip Decode(string inputPath, string name)
    {
        var outputPath = Path.Join(Path.GetTempPath(), "GMConverter", "vgmstream", $"{Guid.NewGuid():N}.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        try
        {
            // -i plays the stream once instead of looping it twice and fading out; -L still records
            // the loop in a smpl chunk, which WaveFile reads back as the clip's loop.
            Run("-i", "-L", "-o", outputPath, inputPath);
            return WaveFile.Read(outputPath) with { Name = name };
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    private void Run(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(_executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new GMConverterException($"Could not start {_executable}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(error.Result) ? output.Result : error.Result;
            throw new GMConverterException($"vgmstream could not decode {arguments[^1]}: {message.Trim()}");
        }
    }

    private static async Task<string> EnsureDownloadedAsync()
    {
        var toolDirectory = ToolDownloads.GetToolDirectory(_toolName);
        if (ToolDownloads.FindExecutable(toolDirectory, ExecutableName) is { } existing)
        {
            return existing;
        }

        var assetName = ReleaseAssetName();
        var archiveUrl = await ToolDownloads.GetLatestReleaseAssetUrlAsync(
            _repository,
            name => string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase));
        await ToolDownloads.DownloadAndExtractAsync(archiveUrl, toolDirectory);

        var executable = ToolDownloads.FindExecutable(toolDirectory, ExecutableName)
            ?? throw new GMConverterException($"Downloaded vgmstream, but {ExecutableName} was not found in {toolDirectory}.");
        if (!OperatingSystem.IsWindows())
        {
            // Zip files do not keep the executable bit.
            File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute);
        }

        return executable;
    }

    private static string ReleaseAssetName()
    {
        if (OperatingSystem.IsWindows())
        {
            return RuntimeInformation.OSArchitecture == Architecture.X86 ? "vgmstream-win.zip" : "vgmstream-win64.zip";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "vgmstream-mac.zip";
        }

        return "vgmstream-linux.zip";
    }
}
