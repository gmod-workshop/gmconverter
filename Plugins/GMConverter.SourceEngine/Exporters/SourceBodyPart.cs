namespace GMConverter.SourceEngine.Exporters;

/// <summary>
/// One body part of a Source model: a <c>$body</c> when it has a single option that always shows,
/// otherwise a <c>$bodygroup</c> whose options a game switches between.
/// </summary>
/// <param name="Name">The body part's name in the QC.</param>
/// <param name="Options">Its options, the default first.</param>
/// <param name="IsGroup">Whether it is a body group rather than the always-shown body.</param>
internal sealed record SourceBodyPart(string Name, IReadOnlyList<SourceBodyPartOption> Options, bool IsGroup);
