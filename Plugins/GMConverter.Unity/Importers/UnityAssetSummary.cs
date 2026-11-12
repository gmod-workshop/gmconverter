namespace GMConverter.Unity.Importers;

/// <summary>What a <c>.unityasset</c> reference points at, for the host's file summary.</summary>
internal sealed record UnityAssetSummary(string Name, string ClassName, string Object);
