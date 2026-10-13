namespace GMConverter.SDK.Explorer;

/// <summary>What an explorer entry is, so the host can treat it without knowing engine asset classes.</summary>
public enum ExplorerEntryKind
{
    /// <summary>A mesh or scene that converts into a model.</summary>
    Model,

    /// <summary>An animation asset: a clip, a sequence of clips, a blend space and the like.</summary>
    Animation,
}
