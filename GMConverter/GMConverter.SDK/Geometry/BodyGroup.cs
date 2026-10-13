namespace GMConverter.SDK.Geometry;

/// <summary>
/// A part of a model that can be swapped or switched off, such as the body of a character with
/// several variants, a helmet, or a limb that hides when it is blown off. Exactly one option of each
/// group shows at a time; the first is the default.
/// </summary>
/// <param name="Name">The group's name, such as <c>body</c> or <c>helmet</c>.</param>
/// <param name="Options">The ways the part can look, the default first.</param>
public sealed record BodyGroup(string Name, IReadOnlyList<BodyGroupOption> Options);
