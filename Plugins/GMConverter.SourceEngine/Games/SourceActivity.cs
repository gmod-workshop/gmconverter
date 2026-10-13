namespace GMConverter.SourceEngine.Games;

/// <summary>
/// An activity to give a clip's sequence. A sequence holds one activity, so a clip that needs
/// several (an NPC and a player activity) is written once per activity; every copy after the first
/// gets <paramref name="SequenceSuffix"/> appended to its name.
/// </summary>
/// <param name="Name">The activity, e.g. <c>ACT_RUN</c>.</param>
/// <param name="SequenceSuffix">Empty for the clip's own sequence, otherwise the suffix of the copy that carries this activity.</param>
internal sealed record SourceActivity(string Name, string SequenceSuffix = "");
