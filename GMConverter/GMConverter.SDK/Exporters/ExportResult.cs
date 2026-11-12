namespace GMConverter.SDK.Exporters;

/// <summary>What an export wrote.</summary>
/// <param name="Files">
/// Full paths of the main files written: the ones a user opens or compiles, such as a
/// <c>.glb</c>, an <c>.obj</c> and its <c>.mtl</c>, or a <c>.qc</c>. Supporting files such as
/// textures and animation SMDs need not be listed.
/// </param>
public sealed record ExportResult(IReadOnlyList<string> Files)
{
    /// <summary>
    /// What the export could not write faithfully, for the user to read: features the target can't
    /// express, or approximations it made. Empty when the output matches the asset.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
