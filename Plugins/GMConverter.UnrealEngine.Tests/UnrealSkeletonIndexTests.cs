using CUE4Parse.UE4.IO.Objects;
using GMConverter.UnrealEngine.Formats.Unreal;

namespace GMConverter.UnrealEngine.Tests;

public sealed class UnrealSkeletonIndexTests
{
    private const string _skeleton = "/Game/Droid/SKEL_Droid";
    private const string _otherSkeleton = "/Game/Human/SKEL_Human";

    private static readonly UnrealSkeletonIndex _index = new(
        new Dictionary<FPackageId, FPackageId[]>
        {
            [Id("/Game/Droid/SK_Droid")] = [Id("/Game/Droid/M_Droid"), Id(_skeleton)],
            [Id("/Game/Droid/A_Walk")] = [Id(_skeleton)],
            [Id("/Game/Human/A_Wave")] = [Id(_otherSkeleton)],
            [Id("/Game/Props/SM_Crate")] = [Id("/Game/Props/M_Crate")],
        },
        [_skeleton, _otherSkeleton]);

    [Theory]
    [InlineData("/Game/Droid/SK_Droid", _skeleton)]
    [InlineData("/Game/Droid/A_Walk", _skeleton)]
    [InlineData("/Game/Human/A_Wave", _otherSkeleton)]
    [InlineData(_skeleton, _skeleton)]
    public void FindsTheImportedSkeleton(string package, string expected)
    {
        Assert.Equal(expected, _index.FindSkeleton(package));
    }

    [Theory]
    [InlineData("/Game/Props/SM_Crate")]
    [InlineData("/Game/Unknown/A_Missing")]
    public void ReturnsNullWithoutASkeletonImport(string package)
    {
        Assert.Null(_index.FindSkeleton(package));
    }

    private static FPackageId Id(string name) => FPackageId.FromName(name);
}
