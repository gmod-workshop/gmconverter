using CUE4Parse.Compression;
using CUE4Parse.FileProvider;
using CUE4Parse_Conversion.Textures;
using CUE4Parse_Conversion.Textures.BC;

using GMConverter.SDK.Common;
using GMConverter.SDK.Explorer;

namespace GMConverter.UnrealEngine.Formats.Unreal;

internal static class Cue4ParseProviderFactory
{
    private static readonly string[] _archivePatterns = ["*.pak", "*.utoc"];
    private static readonly HashSet<string> _archiveExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pak", ".utoc" };

    /// <summary>
    /// Opens the archives under <paramref name="rootPath"/> with the profile for
    /// <paramref name="gameId"/>, or the best-detected profile when it is null or another plugin's.
    /// </summary>
    public static Cue4ParseProviderContext Create(string rootPath, string? gameId)
    {
        using var createScope = PerfTimer.Measure("ue4.provider", "Create", rootPath);

        using (PerfTimer.Measure("ue4.provider", "InitializeNativeLibraries"))
        {
            InitializeNativeLibraries();
        }

        var archiveDirectory = ResolveArchiveDirectory(rootPath);
        var profile = UnrealGameProfiles.Select(archiveDirectory, gameId);
        PerfTimer.Log("ue4.provider", $"profile={profile.GetType().Name} archive={archiveDirectory}");
        var gameData = profile.TryGetGameData(archiveDirectory);
        var provider = new DefaultFileProvider(
            archiveDirectory,
            SearchOption.AllDirectories,
            profile.CreateVersionContainer(),
            StringComparer.OrdinalIgnoreCase);

        // The provider owns native handles and disk-mounted archives, so if any step between
        // construction and successful context wrap-up throws we must dispose it ourselves before
        // the exception escapes — otherwise the caller has no reference and the resources leak.
        try
        {
            using (PerfTimer.Measure("ue4.provider", "ConfigureProvider"))
            {
                profile.ConfigureProvider(provider, gameData);
            }

            using (PerfTimer.Measure("ue4.provider", "Initialize"))
            {
                provider.Initialize();
            }
            PerfTimer.Log("ue4.provider", $"mounted Files.Count={provider.Files.Count}");

            using (PerfTimer.Measure("ue4.provider", "Mount"))
            {
                profile.Mount(provider, gameData);
            }
            PerfTimer.Log("ue4.provider", $"post-mount Files.Count={provider.Files.Count}");

            return new Cue4ParseProviderContext(provider, profile, archiveDirectory);
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    /// <summary>True for a folder of Unreal archives (or one level above it) and for a single archive file.</summary>
    public static bool IsArchiveTarget(ExplorerTarget target)
    {
        return target.IsDirectory && LooksLikeArchiveRoot(target.FullPath) ||
            target.IsFile && _archiveExtensions.Contains(Path.GetExtension(target.FullPath));
    }

    public static bool LooksLikeArchiveRoot(string path)
    {
        var directory = Directory.Exists(path)
            ? path
            : Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        // A child holding the archives covers a game's install folder, whose paks usually sit at
        // <install>/<Project>/Content/Paks.
        return TryResolveKnownArchiveDirectory(directory, out _) ||
            Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly)
                .Take(64)
                .Any(child => TryResolveKnownArchiveDirectory(child, out _));
    }

    public static string ResolveArchiveDirectory(string rootPath)
    {
        var fullPath = Path.GetFullPath(rootPath);
        if (File.Exists(fullPath))
        {
            return Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
        }

        return TryResolveKnownArchiveDirectory(fullPath, out var archiveDirectory)
            ? archiveDirectory
            : fullPath;
    }

    private static void InitializeNativeLibraries()
    {
        var zlibName = OperatingSystem.IsLinux() ? "libz-ng.so" : "zlib-ng2.dll";
        var oodleName = OperatingSystem.IsLinux() ? "liboodle-data-shared.so" : "oodle-data-shared.dll";
        var detexName = OperatingSystem.IsLinux() ? "libDetex.so" : "Detex.dll";
        ZlibHelper.Initialize(GetNativeDependencyPath(zlibName));
        OodleHelper.Initialize(GetNativeDependencyPath(oodleName));
        TextureDecoder.UseAssetRipperTextureDecoder = true;
        var detexPath = GetNativeDependencyPath(detexName);
        if (DetexHelper.LoadDll(detexPath))
        {
            DetexHelper.Initialize(detexPath);
        }
    }

    private static string GetNativeDependencyPath(string fileName)
    {
        var dependencyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GMConverter",
            "CUE4Parse");
        Directory.CreateDirectory(dependencyDirectory);
        return Path.Combine(dependencyDirectory, fileName);
    }

    private static bool TryResolveKnownArchiveDirectory(string directory, out string archiveDirectory)
    {
        if (ContainsArchiveTopLevel(directory))
        {
            archiveDirectory = directory;
            return true;
        }

        var paksDirectory = Path.Combine(directory, "Paks");
        if (ContainsArchiveTopLevel(paksDirectory))
        {
            archiveDirectory = paksDirectory;
            return true;
        }

        var contentPaksDirectory = Path.Combine(directory, "Content", "Paks");
        if (ContainsArchiveTopLevel(contentPaksDirectory))
        {
            archiveDirectory = contentPaksDirectory;
            return true;
        }

        archiveDirectory = directory;
        return false;
    }

    private static bool ContainsArchiveTopLevel(string directory)
    {
        return Directory.Exists(directory) &&
            _archivePatterns.Any(pattern => Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).Any());
    }
}
