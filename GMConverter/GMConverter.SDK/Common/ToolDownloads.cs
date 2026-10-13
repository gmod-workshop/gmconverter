using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GMConverter.SDK.Common;

/// <summary>
/// Finds and downloads the portable command-line tools plugins run, such as studiomdl or an audio
/// decoder. Each tool lives in its own folder under <c>tools/</c> next to the application, so a
/// tool downloaded once is found again without network access.
/// </summary>
public static class ToolDownloads
{
    /// <summary>The folder <paramref name="toolName"/> is kept in, whether or not it has been downloaded.</summary>
    public static string GetToolDirectory(string toolName)
    {
        return Path.Join(Path.GetFullPath(AppContext.BaseDirectory), GetPathSegment("tools"), GetPathSegment(toolName));
    }

    /// <summary>The first file named <paramref name="executableName"/> anywhere under <paramref name="rootDirectory"/>, or null.</summary>
    public static string? FindExecutable(string rootDirectory, string executableName)
    {
        return Directory.Exists(rootDirectory)
            ? Directory.EnumerateFiles(rootDirectory, executableName, SearchOption.AllDirectories).FirstOrDefault()
            : null;
    }

    /// <summary>
    /// The download URL of the asset of <paramref name="repository"/>'s latest GitHub release that
    /// <paramref name="isAsset"/> picks by file name.
    /// </summary>
    /// <param name="repository">The repository as <c>owner/name</c>.</param>
    /// <param name="isAsset">Picks the asset by its file name.</param>
    public static async Task<string> GetLatestReleaseAssetUrlAsync(string repository, Func<string, bool> isAsset)
    {
        using var client = CreateHttpClient();
        await using var stream = await client.GetStreamAsync($"https://api.github.com/repos/{repository}/releases/latest");
        using var document = await JsonDocument.ParseAsync(stream);

        foreach (var asset in document.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var downloadUrl = asset.GetProperty("browser_download_url").GetString();
            if (name is not null && downloadUrl is not null && isAsset(name))
            {
                return downloadUrl;
            }
        }

        throw new GMConverterException($"The latest {repository} release has no matching download.");
    }

    /// <summary>
    /// Downloads the zip at <paramref name="archiveUrl"/> and extracts it into
    /// <paramref name="targetDirectory"/>, replacing anything already there with the same name.
    /// </summary>
    public static async Task DownloadAndExtractAsync(string archiveUrl, string targetDirectory)
    {
        targetDirectory = Path.GetFullPath(targetDirectory);
        Directory.CreateDirectory(targetDirectory);
        var archivePath = Path.Join(targetDirectory, GetPathSegment("download.zip"));
        var extractDirectory = Path.Join(targetDirectory, GetPathSegment("extracting"));

        if (Directory.Exists(extractDirectory))
        {
            Directory.Delete(extractDirectory, recursive: true);
        }

        Directory.CreateDirectory(extractDirectory);

        using (var client = CreateHttpClient())
        await using (var archiveStream = await client.GetStreamAsync(archiveUrl))
        await using (var fileStream = File.Create(archivePath))
        {
            await archiveStream.CopyToAsync(fileStream);
        }

        ZipFile.ExtractToDirectory(archivePath, extractDirectory, overwriteFiles: true);
        File.Delete(archivePath);

        foreach (var path in Directory.EnumerateFileSystemEntries(extractDirectory))
        {
            var entryName = GetPathSegment(path);
            var destination = Path.Join(targetDirectory, entryName);
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

    /// <summary>The full path of an executable the user chose, which must exist.</summary>
    public static string RequireExecutable(string path, string executableName)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        if (!File.Exists(fullPath))
        {
            throw new GMConverterException($"{executableName} not found: {fullPath}");
        }

        return fullPath;
    }

    private static string GetPathSegment(string path)
    {
        var segment = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(segment) || Path.IsPathRooted(segment))
        {
            throw new GMConverterException($"Invalid path segment: {path}");
        }

        return segment;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GMConverter", "1.0"));
        return client;
    }
}
