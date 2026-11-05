namespace GMConverter.UnrealEngine.Formats.UE3.Textures;

/// <summary>One mip level of a texture and where its pixel data is stored.</summary>
internal sealed record UE3TextureMip(int Width, int Height, UE3BulkData Data);
