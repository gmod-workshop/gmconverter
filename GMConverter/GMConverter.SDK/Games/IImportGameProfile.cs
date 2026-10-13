using GMConverter.SDK.Geometry;

namespace GMConverter.SDK.Games;

/// <summary>
/// A game assets are converted from. The host lists these in its game choice for browsing and
/// detects them from folders.
/// </summary>
public interface IImportGameProfile : IGameProfile
{
    /// <summary>
    /// Fills in what an imported model leaves out but this game's conventions reveal, such as what
    /// each animation is for. Runs after import, before any export target's changes.
    /// </summary>
    Model Prepare(Model model) => model;
}
