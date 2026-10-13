namespace GMConverter.SDK.Games;

/// <summary>
/// How sure a <see cref="IGameProfile"/> is that a folder holds its game. When several profiles
/// claim a folder, the host picks the strongest match.
/// </summary>
public enum GameMatch
{
    /// <summary>Not this game.</summary>
    None,

    /// <summary>A generic profile that can read the folder but knows nothing game-specific about it.</summary>
    Fallback,

    /// <summary>Probably this game, from a weaker sign such as a folder name.</summary>
    Likely,

    /// <summary>Certainly this game, from something only it has, such as its executable.</summary>
    Exact,
}
