using System.Numerics;

namespace GMConverter.SourceEngine.Animation;

// Finds where a foot is planted in an animation, for `ikrule footstep`: the frames where the foot
// is near its lowest and hardly moves, with the animation's own motion included, so a walking
// foot counts as planted while the body passes over it. The engine then holds the foot there and
// traces the ground under it, which keeps it on slopes and steps.
internal static class SourceFootPlants
{
    // A planted foot still drifts a little in most animations: under 30 cm/s and within 5 cm of
    // its lowest point is treated as on the ground.
    private const float _maximumSpeed = 0.3f;
    private const float _maximumLift = 0.05f;

    // Rules fade in and out over a tenth of a second, so the foot eases onto its target.
    private const float _rampSeconds = 0.1f;

    // Positions are the foot's on every frame, in metres, Z up. Source loops repeat the first frame
    // as the last, so a loop's cycle is one frame shorter than its frame count, and a plant may
    // wrap around its end.
    public static IReadOnlyList<SourceFootPlant> Find(IReadOnlyList<Vector3> positions, float frameRate, bool loops)
    {
        var frameCount = positions.Count;
        if (frameCount < 3 || frameRate <= 0f)
        {
            return [];
        }

        var lastFrame = frameCount - 1;
        var cycle = loops ? lastFrame : frameCount;
        var lowest = positions.Min(position => position.Z);
        var planted = new bool[cycle];
        for (var frame = 0; frame < cycle; frame++)
        {
            // Still towards either neighbour: a run only plants each foot for two or three frames,
            // so the frames either side of the plant are already moving fast.
            var previous = loops ? (frame + cycle - 1) % cycle : Math.Max(frame - 1, 0);
            var next = loops ? (frame + 1) % cycle : Math.Min(frame + 1, lastFrame);
            var step = MathF.Min(
                previous == frame ? float.MaxValue : Vector3.Distance(positions[frame], positions[previous]),
                next == frame ? float.MaxValue : Vector3.Distance(positions[frame], positions[next]));
            planted[frame] = step * frameRate < _maximumSpeed && positions[frame].Z < lowest + _maximumLift;
        }

        if (planted.All(value => value))
        {
            return [new SourceFootPlant(0, 0, lastFrame, lastFrame, 0)];
        }

        var ramp = Math.Max(1, (int)MathF.Round(frameRate * _rampSeconds));
        List<SourceFootPlant> plants = [];
        foreach (var (first, length) in FindRuns(planted, loops))
        {
            if (length < 2)
            {
                continue;
            }

            var last = first + length - 1;
            var contact = first + (length / 2);
            if (!loops)
            {
                plants.Add(new SourceFootPlant(Math.Max(first - ramp, 0), first, last, Math.Min(last + ramp, lastFrame), contact));
            }
            else if (length + (2 * ramp) >= cycle)
            {
                plants.Add(new SourceFootPlant(0, 0, lastFrame, lastFrame, contact % cycle));
            }
            else
            {
                int Wrap(int frame) => ((frame % cycle) + cycle) % cycle;
                plants.Add(new SourceFootPlant(Wrap(first - ramp), first, Wrap(last), Wrap(last + ramp), Wrap(contact)));
            }
        }

        return plants;
    }

    // Each run of planted frames as its first frame and length. On a loop a run may continue from
    // the end back to the start, and is then reported once, starting near the end.
    private static List<(int First, int Length)> FindRuns(bool[] planted, bool loops)
    {
        var count = planted.Length;
        var origin = 0;
        if (loops)
        {
            // Start scanning just after a frame in the air, so no run is cut in two at the end.
            origin = (Array.IndexOf(planted, false) + 1) % count;
        }

        List<(int, int)> runs = [];
        var runFirst = -1;
        for (var step = 0; step < count; step++)
        {
            var frame = (origin + step) % count;
            if (planted[frame] && runFirst < 0)
            {
                runFirst = step;
            }
            else if (!planted[frame] && runFirst >= 0)
            {
                runs.Add(((origin + runFirst) % count, step - runFirst));
                runFirst = -1;
            }
        }

        if (runFirst >= 0)
        {
            runs.Add(((origin + runFirst) % count, count - runFirst));
        }

        return runs;
    }
}
