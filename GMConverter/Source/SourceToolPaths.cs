using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using GMConverter.Common;

namespace GMConverter.Source;

internal sealed record SourceToolPaths(
    string StudioMdlPath,
    string? VtfCmdPath)
{
    private const string _vtfEditLatestReleaseApiUrl = "https://api.github.com/repos/Sky-rym/VTFEdit-Reloaded/releases/latest";
    private const string _studioMdlMainZipUrl = "https://github.com/DeadZoneLuna/StudioMDL-CE/archive/refs/heads/main.zip";

    public bool CanCompileMaterials => !string.IsNullOrWhiteSpace(VtfCmdPath);

    public static SourceToolPaths Resolve(string? studioMdlPath, string? vtfCmdPath, bool requireVtfCmd)
    {
        string resolvedStudioMdlPath = ResolveToolPath(
            studioMdlPath,
            "cestudiomdl.exe",
            EnsureStudioMdlAsync);
        string? resolvedVtfCmdPath = requireVtfCmd
            ? ResolveToolPath(vtfCmdPath, "VTFCmd.exe", EnsureVtfCmdAsync)
            : ResolveOptionalToolPath(vtfCmdPath, "VTFCmd.exe");

        return new SourceToolPaths(resolvedStudioMdlPath, resolvedVtfCmdPath);
    }

    private static string ResolveToolPath(string? overridePath, string executableName, Func<Task<string>> ensureDefaultToolAsync)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return RequireExecutable(overridePath, executableName);
        }

        return ensureDefaultToolAsync().GetAwaiter().GetResult();
    }

    private static string? ResolveOptionalToolPath(string? overridePath, string executableName)
    {
        return string.IsNullOrWhiteSpace(overridePath)
            ? null
            : RequireExecutable(overridePath, executableName);
    }

    private static string RequireExecutable(string path, string executableName)
    {
        string fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        if (!File.Exists(fullPath))
        {
            throw new GMConverterException($"{executableName} not found: {fullPath}");
        }

        return fullPath;
    }

    private static async Task<string> EnsureStudioMdlAsync()
    {
        string toolDirectory = GetToolDirectory("studiomdl-ce");
        string? existingPath = FindStudioMdl(toolDirectory);
        if (existingPath is not null)
        {
            return existingPath;
        }

        await DownloadAndExtractAsync(_studioMdlMainZipUrl, toolDirectory);

        return FindStudioMdl(toolDirectory) ??
            throw new GMConverterException($"Downloaded StudioMDL-CE, but cestudiomdl.exe was not found in {toolDirectory}.");
    }

    private static async Task<string> EnsureVtfCmdAsync()
    {
        string toolDirectory = GetToolDirectory("vtfedit-reloaded");
        string? existingPath = FindExecutable(toolDirectory, "VTFCmd.exe");
        if (existingPath is not null)
        {
            return existingPath;
        }

        string archiveUrl = await GetLatestVtfEditArchiveUrlAsync();
        await DownloadAndExtractAsync(archiveUrl, toolDirectory);

        return FindExecutable(toolDirectory, "VTFCmd.exe") ??
            throw new GMConverterException($"Downloaded VTFEdit Reloaded, but VTFCmd.exe was not found in {toolDirectory}.");
    }

    private static string GetToolsDirectory()
    {
        string baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        return Path.Combine(baseDirectory, "tools");
    }

    private static string GetToolDirectory(string toolName)
    {
        return Path.Combine(GetToolsDirectory(), Path.GetFileName(toolName));
    }

    private static string? FindStudioMdl(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return null;
        }

        string? x86Path = Directory
            .EnumerateFiles(rootDirectory, "cestudiomdl.exe", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}x64{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        return x86Path ?? FindExecutable(rootDirectory, "cestudiomdl.exe");
    }

    private static string? FindExecutable(string rootDirectory, string executableName)
    {
        return Directory.Exists(rootDirectory)
            ? Directory.EnumerateFiles(rootDirectory, executableName, SearchOption.AllDirectories).FirstOrDefault()
            : null;
    }

    private static async Task<string> GetLatestVtfEditArchiveUrlAsync()
    {
        using HttpClient client = CreateHttpClient();
        await using Stream stream = await client.GetStreamAsync(_vtfEditLatestReleaseApiUrl);
        using JsonDocument document = await JsonDocument.ParseAsync(stream);

        foreach (JsonElement asset in document.RootElement.GetProperty("assets").EnumerateArray())
        {
            string? name = asset.GetProperty("name").GetString();
            string? downloadUrl = asset.GetProperty("browser_download_url").GetString();
            if (name is not null &&
                downloadUrl is not null &&
                name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return downloadUrl;
            }
        }

        throw new GMConverterException("The latest VTFEdit Reloaded release does not contain a zip asset.");
    }

    private static async Task DownloadAndExtractAsync(string archiveUrl, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        string archivePath = Path.Combine(targetDirectory, "download.zip");
        string extractDirectory = Path.Combine(targetDirectory, "extracting");

        if (Directory.Exists(extractDirectory))
        {
            Directory.Delete(extractDirectory, recursive: true);
        }

        Directory.CreateDirectory(extractDirectory);

        using (HttpClient client = CreateHttpClient())
        await using (Stream archiveStream = await client.GetStreamAsync(archiveUrl))
        await using (FileStream fileStream = File.Create(archivePath))
        {
            await archiveStream.CopyToAsync(fileStream);
        }

        ZipFile.ExtractToDirectory(archivePath, extractDirectory, overwriteFiles: true);
        File.Delete(archivePath);

        foreach (string path in Directory.EnumerateFileSystemEntries(extractDirectory))
        {
            string destination = Path.Combine(targetDirectory, Path.GetFileName(path));
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }
            else if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            if (Directory.Exists(path))
            {
                Directory.Move(path, destination);
            }
            else
            {
                File.Move(path, destination);
            }
        }

        Directory.Delete(extractDirectory, recursive: true);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GMConverter", "1.0"));
        return client;
    }
}
