using GMConverter.SDK.Games;

namespace GMConverter.Core.Games;

/// <summary>The game chosen for a scan target, and how it was chosen.</summary>
/// <param name="Profile">The game.</param>
/// <param name="Match">How sure detection was. A game the user picked counts as <see cref="GameMatch.Exact"/>.</param>
/// <param name="IsDetected">True when the host detected the game, false when the user picked it.</param>
internal sealed record GameDetection(IGameProfile Profile, GameMatch Match, bool IsDetected);
