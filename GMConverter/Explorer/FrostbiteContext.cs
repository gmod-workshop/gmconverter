using CUE4Parse_Conversion.Textures.BC;
using Frosty.Sdk;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.Managers;
using GMConverter.Common;

namespace GMConverter.Explorer;

// FrostySdk exposes ProfilesLibrary / FileSystemManager / ResourceManager / AssetManager as
// static singletons, so the whole process can only have one Frostbite mount at a time. This
// wrapper centralises that lifecycle: it remembers which install root is mounted, no-ops on
// re-mounts of the same root, and fails loudly when a different root is requested in the same
// process. Restart-required is documented to the user via the exception message.
internal static class FrostbiteContext
{
    private static readonly object _mountLock = new();

    private static string? _mountedRoot;
    private static string? _mountedProfileKey;
    private static string? _mountedDisplayName;

    // Exe filename (without extension) → ProfilesLibrary profile key. The profile key matches the
    // Name field in the corresponding *.json shipped to the output Profiles/ directory.
    private static readonly (string ExeName, string ProfileKey)[] _knownGames =
    [
        ("starwarssquadrons", "starwarssquadrons"),
        ("starwarsbattlefrontii", "starwarsbattlefrontii"),
        ("starwarsbattlefront", "starwarsbattlefront")
    ];

    public static FrostbiteMount Mount(string installRoot)
    {
        var fullPath = Path.GetFullPath(installRoot);

        lock (_mountLock)
        {
            if (_mountedRoot is not null && string.Equals(_mountedRoot, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return new FrostbiteMount(_mountedRoot, _mountedProfileKey!, _mountedDisplayName!, AlreadyMounted: true);
            }

            if (_mountedRoot is not null)
            {
                throw new GMConverterException(
                    $"Frostbite is already mounted at \"{_mountedRoot}\". FrostySdk uses global state and cannot remount in the same process; restart the application to switch installs.");
            }

            var profileKey = DetectProfileKey(fullPath)
                ?? throw new GMConverterException(
                    $"No supported Frostbite game executable found in \"{fullPath}\". Expected one of: {string.Join(", ", _knownGames.Select(g => g.ExeName + ".exe"))}.");

            // Frosty.Sdk.Utils.Utils.BaseDirectory drives the lookup paths for Profiles/ (JSON),
            // Sdk/<InternalName>.dll (EBX type sdk), and Caches/. AppContext.BaseDirectory is
            // GMConverter's own bin output; Profiles/*.json are copied there by the csproj.
            Frosty.Sdk.Utils.Utils.BaseDirectory = AppContext.BaseDirectory;

            FrostyLogger.Logger ??= new FrostyForwardLogger();

            // CUE4Parse-Conversion bundles the Detex BCn decoder DLL as an embedded resource.
            // We use Detex for BC1/BC2/BC3/BC7 because AssetRipper.TextureDecoder produces
            // visibly soft / channel-scrambled results on Squadrons textures (verified against
            // FrostyEditor — diffuses come out crisp in Frosty's preview, blurry through
            // AssetRipper). LoadDll extracts the native binary into the app's base directory
            // on first call; Initialize loads it for the static helper.
            try
            {
                var detexPath = Path.Combine(AppContext.BaseDirectory, DetexHelper.DLL_NAME);
                if (DetexHelper.LoadDll(detexPath))
                {
                    DetexHelper.Initialize(detexPath);
                }
            }
            catch (Exception ex)
            {
                PerfTimer.Log("frostbite.mount", $"Detex init failed: {ex.GetType().Name}: {ex.Message}");
            }

            using (PerfTimer.Measure("frostbite.mount", "Mount", fullPath))
            {
                if (!ProfilesLibrary.Initialize(profileKey))
                {
                    throw new GMConverterException(
                        $"FrostySdk ProfilesLibrary failed to load profile \"{profileKey}\". Expected {profileKey}.json under \"{Path.Combine(AppContext.BaseDirectory, "Profiles")}\".");
                }

                if (!FileSystemManager.Initialize(fullPath))
                {
                    throw new GMConverterException(
                        $"FrostySdk FileSystemManager failed to initialise against \"{fullPath}\". Confirm Data/layout.toc and Data/initfs_Win32 are present.");
                }

                if (!ResourceManager.Initialize())
                {
                    throw new GMConverterException(
                        "FrostySdk ResourceManager failed to initialise. Catalog (cas.cat) parsing did not complete.");
                }

                if (!AssetManager.Initialize())
                {
                    throw new GMConverterException(
                        "FrostySdk AssetManager failed to initialise. SuperBundle loading did not complete.");
                }
            }

            _mountedRoot = fullPath;
            _mountedProfileKey = profileKey;
            _mountedDisplayName = ProfilesLibrary.DisplayName;

            PerfTimer.Log("frostbite.mount",
                $"mounted profile=\"{profileKey}\" display=\"{ProfilesLibrary.DisplayName}\" superBundles={FileSystemManager.EnumerateSuperBundles().Count()}");

            return new FrostbiteMount(_mountedRoot, _mountedProfileKey, _mountedDisplayName, AlreadyMounted: false);
        }
    }

    public static bool LooksLikeFrostbiteRoot(string installRoot)
    {
        if (!Directory.Exists(installRoot))
        {
            return false;
        }

        if (!File.Exists(Path.Combine(installRoot, "Data", "layout.toc")))
        {
            return false;
        }

        return DetectProfileKey(installRoot) is not null;
    }

    private static string? DetectProfileKey(string installRoot)
    {
        foreach (var (exeName, profileKey) in _knownGames)
        {
            if (File.Exists(Path.Combine(installRoot, exeName + ".exe")))
            {
                return profileKey;
            }
        }

        return null;
    }

    private sealed class FrostyForwardLogger : ILogger
    {
        public void LogInfo(string message) => PerfTimer.Log("frostbite.sdk", $"INFO {message}");
        public void LogWarning(string message) => PerfTimer.Log("frostbite.sdk", $"WARN {message}");
        public void LogError(string message) => PerfTimer.Log("frostbite.sdk", $"ERROR {message}");
        public void LogProgress(double progress) { }
    }
}

internal readonly record struct FrostbiteMount(
    string InstallRoot,
    string ProfileKey,
    string DisplayName,
    bool AlreadyMounted);
