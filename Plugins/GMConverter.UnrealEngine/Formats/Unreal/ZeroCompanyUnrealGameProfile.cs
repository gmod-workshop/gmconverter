using GMConverter.SDK.Explorer;
using GMConverter.SDK.Games;

namespace GMConverter.UnrealEngine.Formats.Unreal;

/// <summary>
/// Star Wars Zero Company (UE 5.6). It reads like any other Unreal game, with the community
/// <c>.usmap</c> at the install root, so it shares <see cref="GenericUnrealGameProfile"/>'s
/// settings; the profile exists so the game is recognised by name and so game-specific behaviour
/// (such as its animation naming) has somewhere to live.
/// </summary>
internal sealed class ZeroCompanyUnrealGameProfile : GenericUnrealGameProfile
{
    private const string _projectName = "SWZeroCompany";

    // The paks sit at <install>/SWZeroCompany/Content/Paks, three levels below the executable.
    private const int _maxAncestorDepth = 4;

    public override string Id => "swzc";

    public override string DisplayName => "Star Wars Zero Company";

    public override string Engine => "Unreal Engine 5";

    /// <summary>
    /// <see cref="GameMatch.Exact"/> when <c>SWZeroCompany.exe</c> sits in the archive folder or a
    /// folder above it, <see cref="GameMatch.Likely"/> when the path only contains the project's
    /// folder name.
    /// </summary>
    public override GameMatch Detect(ExplorerTarget target)
    {
        if (!Cue4ParseProviderFactory.IsArchiveTarget(target))
        {
            return GameMatch.None;
        }

        var directory = new DirectoryInfo(Cue4ParseProviderFactory.ResolveArchiveDirectory(target.FullPath));
        for (var depth = 0; directory is not null && depth <= _maxAncestorDepth; depth++, directory = directory.Parent)
        {
            if (File.Exists(Path.Join(directory.FullName, _projectName + ".exe")))
            {
                return GameMatch.Exact;
            }
        }

        var segments = target.FullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains(_projectName, StringComparer.OrdinalIgnoreCase) ? GameMatch.Likely : GameMatch.None;
    }
}
