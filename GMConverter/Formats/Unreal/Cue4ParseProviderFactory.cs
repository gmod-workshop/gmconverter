using CUE4Parse.Compression;
using CUE4Parse.FileProvider;
using CUE4Parse_Conversion.Textures;
using CUE4Parse_Conversion.Textures.BC;

namespace GMConverter.Formats.Unreal;

internal static class Cue4ParseProviderFactory
{
    private static readonly string[] _archivePatterns = ["*.pak", "*.utoc"];
    private static readonly IUnrealGameProfile[] _profiles =
    [
        new FortniteUnrealGameProfile(),
        new GenericUnrealGameProfile()
    ];

    public static Cue4ParseProviderContext Create(string rootPath)
    {
        InitializeNativeLibraries();

        var archiveDirectory = ResolveArchiveDirectory(rootPath);
        var profile = SelectProfile(archiveDirectory);
        var gameData = profile.TryGetGameData();
        var provider = new DefaultFileProvider(
            archiveDirectory,
            SearchOption.AllDirectories,
            profile.CreateVersionContainer(),
            StringComparer.OrdinalIgnoreCase);

        profile.ConfigureProvider(provider, gameData);
        provider.Initialize();
        profile.Mount(provider, gameData);
        return new Cue4ParseProviderContext(provider, profile, archiveDirectory);
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

        return TryResolveKnownArchiveDirectory(directory, out _) ||
            Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly)
                .Take(64)
                .Any(child => ContainsArchiveTopLevel(child));
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

    private static IUnrealGameProfile SelectProfile(string archiveDirectory)
    {
        return _profiles.First(profile => profile.Supports(archiveDirectory));
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
