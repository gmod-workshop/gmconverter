namespace GMConverter.UnrealEngine.Formats.UE3.Packages;

/// <summary>A compressed block of a package body: where it lands once decompressed and where it is stored.</summary>
internal readonly record struct UE3CompressedChunk(int UncompressedOffset, int UncompressedSize, int CompressedOffset, int CompressedSize);
