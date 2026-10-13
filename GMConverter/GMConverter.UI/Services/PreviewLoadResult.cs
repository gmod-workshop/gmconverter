namespace GMConverter.UI.Services;

/// <summary>What a preview loaded: its summary, the files to show, and the model's animations.</summary>
internal sealed record PreviewLoadResult(PreviewSummary Summary, string ModelPath, string? PhysicsModelPath)
{
    public IReadOnlyList<PreviewAnimation> Animations { get; init; } = [];
}
