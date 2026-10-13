using GMConverter.SDK.Common;

namespace GMConverter.SourceEngine.Tools;

internal sealed record SourceToolPaths(
    string StudioMdlPath,
    string? VtfCmdPath)
{
    private const string _vtfEditRepository = "Sky-rym/VTFEdit-Reloaded";
    private const string _studioMdlMainZipUrl = "https://github.com/DeadZoneLuna/StudioMDL-CE/archive/refs/heads/main.zip";

    public bool CanCompileMaterials => !string.IsNullOrWhiteSpace(VtfCmdPath);

    // Cheap on-disk lookup for the portable tools we already auto-download next to the app. Used
    // as the default for the StudioMDL / VTFCmd options without triggering the download path in
    // EnsureStudioMdlAsync / EnsureVtfCmdAsync. Returns null for any tool whose
    // executable isn't already extracted under the tools/ directory.
    public static (string? StudioMdl, string? VtfCmd) TryFindLocalDefaults()
    {
        return (
            FindStudioMdl(ToolDownloads.GetToolDirectory("studiomdl-ce")),
            ToolDownloads.FindExecutable(ToolDownloads.GetToolDirectory("vtfedit-reloaded"), "VTFCmd.exe"));
    }

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
            return ToolDownloads.RequireExecutable(overridePath, executableName);
        }

        return ensureDefaultToolAsync().GetAwaiter().GetResult();
    }

    private static string? ResolveOptionalToolPath(string? overridePath, string executableName)
    {
        return string.IsNullOrWhiteSpace(overridePath)
            ? null
            : ToolDownloads.RequireExecutable(overridePath, executableName);
    }

    private static async Task<string> EnsureStudioMdlAsync()
    {
        string toolDirectory = ToolDownloads.GetToolDirectory("studiomdl-ce");
        string? existingPath = FindStudioMdl(toolDirectory);
        if (existingPath is not null)
        {
            return existingPath;
        }

        await ToolDownloads.DownloadAndExtractAsync(_studioMdlMainZipUrl, toolDirectory);

        return FindStudioMdl(toolDirectory) ??
            throw new GMConverterException($"Downloaded StudioMDL-CE, but cestudiomdl.exe was not found in {toolDirectory}.");
    }

    private static async Task<string> EnsureVtfCmdAsync()
    {
        string toolDirectory = ToolDownloads.GetToolDirectory("vtfedit-reloaded");
        string? existingPath = ToolDownloads.FindExecutable(toolDirectory, "VTFCmd.exe");
        if (existingPath is not null)
        {
            return existingPath;
        }

        string archiveUrl = await ToolDownloads.GetLatestReleaseAssetUrlAsync(
            _vtfEditRepository,
            name => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        await ToolDownloads.DownloadAndExtractAsync(archiveUrl, toolDirectory);

        return ToolDownloads.FindExecutable(toolDirectory, "VTFCmd.exe") ??
            throw new GMConverterException($"Downloaded VTFEdit Reloaded, but VTFCmd.exe was not found in {toolDirectory}.");
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

        return x86Path ?? ToolDownloads.FindExecutable(rootDirectory, "cestudiomdl.exe");
    }
}
