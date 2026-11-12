using System.Numerics;
using GMConverter.SourceEngine.Animation;

namespace GMConverter.SourceEngine.Tests;

public sealed class SourceFootPlantsTests
{
    [Fact]
    public void AFootThatNeverMovesIsPlantedForTheWholeSequence()
    {
        var plants = SourceFootPlants.Find([.. Enumerable.Repeat(new Vector3(0, 0.2f, 0.1f), 10)], 30f, loops: true);

        Assert.Equal([new SourceFootPlant(0, 0, 9, 9, 0)], plants);
    }

    [Fact]
    public void ARunsBriefPlantIsFoundBetweenFastSwings()
    {
        // Still on frames 4 and 5, otherwise moving 10 cm and more a frame, and lifted in the air.
        Vector3[] positions = [.. Enumerable.Range(0, 12).Select(frame => frame switch
        {
            4 or 5 => new Vector3(0.4f, 0, 0.1f),
            _ => new Vector3(frame * 0.1f, 0, 0.1f + (MathF.Abs(frame - 4.5f) * 0.05f)),
        })];

        var plants = SourceFootPlants.Find(positions, 30f, loops: false);

        // Ramps of 3 frames (a tenth of a second) either side, held on the plant's middle.
        Assert.Equal([new SourceFootPlant(1, 4, 5, 8, 5)], plants);
    }

    [Fact]
    public void APlantAcrossTheEndOfALoopWrapsAround()
    {
        // A 9-frame cycle (frame 9 repeats frame 0) with the foot down on frames 7, 8, 0 and 1.
        var down = new Vector3(0, 0, 0.1f);
        Vector3[] positions = [.. Enumerable.Range(0, 10).Select(frame => frame is 7 or 8 or 0 or 1 or 9
            ? down
            : new Vector3(frame * 0.2f, 0, 0.3f))];

        // At 10 fps the ramps are one frame.
        var plants = SourceFootPlants.Find(positions, 10f, loops: true);

        Assert.Equal([new SourceFootPlant(6, 7, 1, 2, 0)], plants);
    }

    [Fact]
    public void AFootInTheAirIsNotPlantedEvenWhenItPauses()
    {
        // Paused 30 cm above its lowest point, as at the top of a jump.
        Vector3[] positions = [.. Enumerable.Range(0, 10).Select(frame => frame is 4 or 5 or 6
            ? new Vector3(0, 0, 0.4f)
            : new Vector3(frame * 0.2f, 0, 0.1f))];

        Assert.Empty(SourceFootPlants.Find(positions, 30f, loops: false));
    }
}
