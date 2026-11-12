using GMConverter.SDK.Audio;
using GMConverter.UnrealEngine.Audio;
using GMConverter.UnrealEngine.Importers;

namespace GMConverter.UnrealEngine.Tests;

public sealed class WwiseEventFlattenerTests
{
    // FLY_B1_Short_Light from Star Wars Zero Company: a random container at -10 dB with a
    // ±25 cent pitch randomizer, under actor mixers adding -20 dB and the attenuation.
    [Fact]
    public void VolumesAndRandomizersAddUpThroughTheActorMixers()
    {
        var container = new WwiseNode(
            WwiseNodeKind.Random,
            new WwiseMix(VolumeDb: -10f, PitchMinCents: -25f, PitchMaxCents: 25f),
            [new(WwiseNode.Sound(1), 50000f), new(WwiseNode.Sound(2), 50000f)],
            AvoidRepeats: true);
        var mixers = new WwiseMix(AttenuationMeters: 38f).Then(new WwiseMix(VolumeDb: -20f));

        var mix = WwiseEventFlattener.Flatten(container, mixers)!;

        Assert.Equal(-30f, mix.Mix.VolumeDb);
        Assert.Equal((-25f, 25f), (mix.Mix.PitchMinCents, mix.Mix.PitchMaxCents));
        Assert.Equal(38f, mix.Mix.AttenuationMeters);
        Assert.Equal(SoundSelection.RandomNoRepeat, mix.Selection);
        Assert.Equal([1u, 2u], mix.Entries.Select(entry => entry.MediaId));
        Assert.All(mix.Entries, entry => Assert.Equal(0.5f, entry.Weight));
    }

    [Fact]
    public void QuieterMediaKeepTheirDifferenceAsGain()
    {
        var container = new WwiseNode(
            WwiseNodeKind.Random,
            new WwiseMix(VolumeDb: -6f),
            [new(WwiseNode.Sound(1)), new(WwiseNode.Sound(2, new WwiseMix(VolumeDb: -4f)))]);

        var mix = WwiseEventFlattener.Flatten(container, WwiseMix.None)!;

        Assert.Equal(-6f, mix.Mix.VolumeDb);
        Assert.Equal([0f, -4f], mix.Entries.Select(entry => entry.GainDb));
    }

    [Fact]
    public void WeightsMultiplyDownNestedContainers()
    {
        var inner = new WwiseNode(WwiseNodeKind.Random, WwiseMix.None, [new(WwiseNode.Sound(2)), new(WwiseNode.Sound(3))]);
        var outer = new WwiseNode(WwiseNodeKind.Random, WwiseMix.None, [new(WwiseNode.Sound(1), 1f), new(inner, 3f)]);

        var weights = WwiseEventFlattener.Flatten(outer, WwiseMix.None)!.Entries.ToDictionary(entry => entry.MediaId, entry => entry.Weight);

        Assert.Equal(0.25f, weights[1]);
        Assert.Equal(0.375f, weights[2]);
        Assert.Equal(0.375f, weights[3]);
    }

    [Fact]
    public void LayersKeepOnlyTheirFirstPart()
    {
        // An event with two play actions: the footstep itself and a gear rattle layered on top.
        var footstep = new WwiseNode(WwiseNodeKind.Random, new WwiseMix(VolumeDb: -18f), [new(WwiseNode.Sound(1)), new(WwiseNode.Sound(2))]);
        var rattle = new WwiseNode(WwiseNodeKind.Random, WwiseMix.None, [new(WwiseNode.Sound(3))]);
        var layered = new WwiseNode(WwiseNodeKind.Layer, WwiseMix.None, [new(footstep), new(rattle)]);

        var mix = WwiseEventFlattener.Flatten(layered, WwiseMix.None)!;

        Assert.Equal([1u, 2u], mix.Entries.Select(entry => entry.MediaId));
        Assert.Equal(-18f, mix.Mix.VolumeDb);
    }

    [Fact]
    public void TheAttenuationNearestTheSoundWins()
    {
        var sound = WwiseNode.Sound(1, new WwiseMix(AttenuationMeters: 10f));

        Assert.Equal(10f, WwiseEventFlattener.Flatten(sound, new WwiseMix(AttenuationMeters: 50f))!.Mix.AttenuationMeters);
    }

    [Fact]
    public void SequencesAndLoopsAreKept()
    {
        var sequence = new WwiseNode(WwiseNodeKind.Sequence, new WwiseMix(Loops: true), [new(WwiseNode.Sound(1)), new(WwiseNode.Sound(2))]);

        var mix = WwiseEventFlattener.Flatten(sequence, WwiseMix.None)!;

        Assert.Equal(SoundSelection.Sequence, mix.Selection);
        Assert.True(mix.Mix.Loops);
    }

    [Fact]
    public void NothingToPlayFlattensToNothing()
    {
        Assert.Null(WwiseEventFlattener.Flatten(new WwiseNode(WwiseNodeKind.Random, WwiseMix.None, []), WwiseMix.None));
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(-6f, 0.501f)]
    [InlineData(-30f, 0.0316f)]
    public void DecibelsBecomeLinearGain(float decibels, float gain)
    {
        Assert.Equal(gain, WwiseEventImporter.Gain(decibels), 0.001f);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1200f, 2f)]
    [InlineData(-25f, 0.9857f)]
    public void CentsBecomePlaybackRate(float cents, float ratio)
    {
        Assert.Equal(ratio, WwiseEventImporter.PitchRatio(cents), 0.0005f);
    }
}
