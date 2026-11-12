using GMConverter.SDK.Explorer;
using GMConverter.SDK.Profiles;
using GMConverter.Unity.Assets;

namespace GMConverter.Unity.Profiles.LiarsBar;

/// <summary>
/// Liar's Bar (Unity 2022.3, HDRP, IL2CPP). It reads like any other Unity build; the profile
/// exists so the game is recognised by name and so game-specific behaviour has somewhere to live.
/// </summary>
internal sealed class LiarsBarProfile : IImportGameProfile
{
    private const string _productName = "Liar's Bar";

    public string Id => "liarsbar";

    public string DisplayName => _productName;

    public string Engine => "Unity";

    /// <summary><see cref="GameMatch.Exact"/> when the build's data folder is <c>Liar's Bar_Data</c>.</summary>
    public GameMatch Detect(ExplorerTarget target)
    {
        var dataDirectory = UnityGameFolder.FindDataDirectory(target.FullPath);
        return dataDirectory is not null &&
            string.Equals(Path.GetFileName(dataDirectory), _productName + "_Data", StringComparison.OrdinalIgnoreCase)
            ? GameMatch.Exact
            : GameMatch.None;
    }
}
