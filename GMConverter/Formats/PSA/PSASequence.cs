namespace GMConverter.Formats.PSA;

internal sealed record PSASequence(
    string Name,
    string Group,
    int BoneCount,
    int RootInclude,
    int CompressionStyle,
    int KeyQuota,
    float KeyReduction,
    float TrackTime,
    float Fps,
    int StartBone,
    int FrameStartIndex,
    int FrameCount);
