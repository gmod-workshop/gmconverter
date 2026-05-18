using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.Versions;

namespace GMConverter.Formats.Unreal;

internal sealed class FortniteUnrealGameProfile : IUnrealGameProfile
{
    private static readonly HashSet<string> _supportedAssetClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "SkeletalMesh",
        "StaticMesh",
        "AnimationAsset",
        "AnimBlueprint",
        "AnimComposite",
        "AnimMontage",
        "AnimSequence",
        "AnimStreamable",
        "BlendSpace",
        "BlendSpace1D",
        "PoseAsset",
        "AthenaBackpackItemDefinition",
        "AthenaCharacterItemDefinition",
        "AthenaDanceItemDefinition",
        "AthenaGadgetItemDefinition",
        "AthenaGliderItemDefinition",
        "AthenaPickaxeItemDefinition",
        "CosmeticCompanionItemDefinition",
        "CosmeticShoesItemDefinition",
        "FortPlaysetItemDefinition",
        "FortPlaysetPropItemDefinition",
        "FortTrapItemDefinition",
        "FortVehicleItemDefinition",
        "FortWeaponMeleeItemDefinition",
        "FortWeaponRangedItemDefinition"
    };

    public string Id => "fortnite";

    public string DisplayName => "Fortnite";

    public bool Supports(string archiveDirectory)
    {
        return Directory.Exists(archiveDirectory) &&
            (archiveDirectory.Contains("FortniteGame", StringComparison.OrdinalIgnoreCase) ||
                Directory.EnumerateFiles(archiveDirectory, "pakchunk0-WindowsClient.*", SearchOption.TopDirectoryOnly).Any());
    }

    public VersionContainer CreateVersionContainer()
    {
        return new VersionContainer(EGame.GAME_UE5_8, ETexturePlatform.DesktopMobile);
    }

    public Cue4ParseGameData? TryGetGameData()
    {
        return UedbClient.TryGetFortniteData();
    }

    public void ConfigureProvider(DefaultFileProvider provider, Cue4ParseGameData? gameData)
    {
        if (!string.IsNullOrWhiteSpace(gameData?.MappingsPath))
        {
            provider.MappingsContainer = new FileUsmapTypeMappingsProvider(gameData.MappingsPath);
        }

        provider.OnDemandOptions = new IoStoreOnDemandOptions
        {
            ChunkHostUri = new Uri("https://download.epicgames.com/", UriKind.Absolute),
            ChunkCacheDirectory = GetOnDemandCacheDirectory()
        };
    }

    public void Mount(DefaultFileProvider provider, Cue4ParseGameData? gameData)
    {
        if (gameData is not null)
        {
            foreach (var (guid, key) in gameData.AesKeys)
            {
                provider.SubmitKey(guid, new FAesKey(key));
            }
        }
        else
        {
            provider.Mount();
        }

        provider.LoadVirtualPaths();
        provider.PostMount();
    }

    public bool TryGetExplorerAssetClass(string registryClass, out string assetClass)
    {
        assetClass = registryClass;
        return _supportedAssetClasses.Contains(registryClass);
    }

    public string? GetUnavailableEncryptedArchiveMessage(DefaultFileProvider provider)
    {
        if (provider.UnloadedVfs.Count == 0)
        {
            return null;
        }

        var requiredKeys = provider.RequiredKeys
            .Select(guid => guid.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        if (requiredKeys.Length == 0)
        {
            return "Some Fortnite archive containers are still locked after mounting. Current keys may not cover optional encrypted chunks.";
        }

        return "Some Fortnite archive containers are still locked after mounting. " +
            $"Missing key GUIDs include: {string.Join(", ", requiredKeys)}. " +
            "The explorer should still show readable Fortnite assets when the main asset registries are available.";
    }

    private static DirectoryInfo GetOnDemandCacheDirectory()
    {
        var cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GMConverter",
            "CUE4Parse",
            "OnDemand");
        Directory.CreateDirectory(cacheDirectory);
        return new DirectoryInfo(cacheDirectory);
    }
}
