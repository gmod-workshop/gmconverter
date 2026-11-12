namespace GMConverter.SourceEngine.Flexes;

/// <summary>A morph target as a Source flex, with the flex controller of the same name that drives it.</summary>
/// <param name="MorphName">The morph target's name in the model.</param>
/// <param name="Name">The flex and controller name in the QC.</param>
/// <param name="Frame">The flex's frame in the VTA; frame 0 is the rest shape.</param>
internal sealed record SourceFlex(string MorphName, string Name, int Frame);
