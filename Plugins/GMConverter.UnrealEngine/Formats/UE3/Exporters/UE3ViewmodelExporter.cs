using System.Numerics;
using GMConverter.SDK.Common;
using GMConverter.UnrealEngine.Formats.UE3.Animations;
using GMConverter.UnrealEngine.Formats.UE3.Meshes;
using GMConverter.UnrealEngine.Formats.UE3.Packages;
using GMConverter.UnrealEngine.Profiles.CrossFireHD;

namespace GMConverter.UnrealEngine.Formats.UE3.Exporters;

/// <summary>
/// Assembles a CrossFire HD first-person view model: the arms the weapon's arm animations are
/// made for, the weapon on the arms' weapon bone, and one clip per action with the arms' and the
/// weapon's animation of that name played together. Skins add their changed clips on top of the
/// base weapon's.
/// </summary>
internal static class UE3ViewmodelExporter
{
    private const int _maximumTextureSize = 4096;

    public static UE3ExportResult Export(UE3GameContent content, UE3Package package, UE3PackageExport weaponExport, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var armsSets = LoadSets(content, package, CrossFireHDViewmodels.ArmsSetPrefix);
        var weaponSets = LoadSets(content, package, CrossFireHDViewmodels.WeaponSetPrefix);
        if (armsSets.Count == 0)
        {
            throw new GMConverterException($"{weaponExport.ObjectName} has no first-person arm animations ({CrossFireHDViewmodels.ArmsSetPrefix}*) in its package.");
        }

        var (armsPackage, armsExport) = FindArms(content, armsSets)
            ?? throw new GMConverterException($"The arms model for {weaponExport.ObjectName}'s animations could not be found.");
        var arms = UE3SkeletalMesh.Read(armsPackage, armsExport);
        var weapon = UE3SkeletalMesh.Read(package, weaponExport);
        var assembled = UE3MeshAssembly.Attach(arms, weapon, CrossFireHDViewmodels.WeaponBone, CrossFireHDViewmodels.WeaponBonePrefix) with
        {
            Name = weapon.Name + "_Viewmodel",
        };

        // Materials are referenced by package, so each part's slots are exported from its own.
        var slots = UE3MaterialExporter.Export(content, armsPackage, arms.Materials, outputDirectory, _maximumTextureSize)
            .Concat(UE3MaterialExporter.Export(content, package, weapon.Materials, outputDirectory, _maximumTextureSize))
            .ToArray();
        var meshPath = Path.Combine(outputDirectory, assembled.Name + ".psk");
        UE3PskWriter.Write(meshPath, assembled, [.. slots.Select(slot => slot.Name)], [.. slots.Select(slot => (slot.AddressX, slot.AddressY))]);
        UE3MeshExporter.WriteSockets(assembled, meshPath);
        UE3MeshExporter.WriteModel(assembled, meshPath, firstPerson: true);

        var animationPath = Path.Combine(outputDirectory, assembled.Name + ".psa");
        WriteClips(animationPath, assembled, armsSets, weaponSets);
        return new UE3ExportResult(meshPath, [animationPath]);
    }

    // The weapon's own sets plus, for a skin, the base weapon's sets they build on, base first.
    private static List<LoadedSet> LoadSets(UE3GameContent content, UE3Package package, string prefix)
    {
        var own = package.Exports
            .Where(export => package.GetClassName(export) == "AnimSet" && CrossFireHDViewmodels.IsViewmodelSet(export.ObjectName, prefix))
            .ToArray();
        List<LoadedSet> sets = [];
        foreach (var baseName in own.Select(export => CrossFireHDViewmodels.GetBaseSetName(export.ObjectName)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (own.Any(export => export.ObjectName.Equals(baseName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (content.FindTopLevel(baseName, "AnimSet").FirstOrDefault() is { Package: not null } found)
            {
                PerfTimer.Log("ue3.viewmodel", $"skin set builds on {baseName} from {Path.GetFileName(found.Package.FilePath)}");
                sets.Add(LoadSet(found.Package, found.Export));
            }
        }

        sets.AddRange(own.OrderBy(export => CrossFireHDViewmodels.GetBaseSetName(export.ObjectName) is null ? 0 : 1).Select(export => LoadSet(package, export)));
        return sets;
    }

    private static LoadedSet LoadSet(UE3Package package, UE3PackageExport export)
    {
        var set = UE3AnimSet.Read(package, export);
        List<UE3AnimSequence> sequences = [];
        foreach (var index in set.Sequences)
        {
            if (package.GetExport(index) is not { } sequence)
            {
                continue;
            }

            try
            {
                sequences.Add(UE3AnimSequence.Read(package, sequence, set.TrackBones.Count));
            }
            catch (GMConverterException)
            {
                // A sequence that can't be read leaves the others usable.
            }
        }

        return new LoadedSet(set, sequences);
    }

    private static (UE3Package Package, UE3PackageExport Export)? FindArms(UE3GameContent content, IReadOnlyList<LoadedSet> armsSets)
    {
        foreach (var preview in armsSets.AsEnumerable().Reverse().Select(loaded => loaded.Set.PreviewMeshName).OfType<string>())
        {
            var meshName = preview[(preview.LastIndexOf('.') + 1)..];
            if (content.FindTopLevel(meshName, "SkeletalMesh").FirstOrDefault() is { Package: not null } found)
            {
                return found;
            }
        }

        return null;
    }

    private static void WriteClips(string path, UE3SkeletalMesh mesh, List<LoadedSet> armsSets, List<LoadedSet> weaponSets)
    {
        var armsClips = LatestClips(armsSets, string.Empty);
        var weaponClips = LatestClips(weaponSets, CrossFireHDViewmodels.WeaponBonePrefix);
        var bones = mesh.Bones.Select(bone => bone.Name).ToArray();
        var boneIndex = bones.Select((name, index) => (name, index)).ToDictionary(pair => pair.name, pair => pair.index, StringComparer.OrdinalIgnoreCase);

        List<UE3AnimSequence> clips = [];
        foreach (var (name, arms) in armsClips)
        {
            var tracks = mesh.Bones.Select(bone => new UE3AnimTrack([bone.Position], null, [bone.Rotation], null)).ToArray();
            void Place(Clip clip)
            {
                for (var track = 0; track < clip.TrackBones.Count; track++)
                {
                    if (boneIndex.TryGetValue(clip.TrackBones[track], out var bone))
                    {
                        tracks[bone] = Resample(clip.Sequence.Tracks[track], clip.Sequence, arms.Sequence, tracks[bone]);
                    }
                }
            }

            Place(arms);
            if (weaponClips.TryGetValue(name, out var weapon))
            {
                Place(weapon);
            }

            clips.Add(arms.Sequence with { Tracks = tracks });
        }

        if (clips.Count == 0)
        {
            throw new GMConverterException($"No first-person clips could be read for {mesh.Name}.");
        }

        // Bones the sets animate with their translation; every other bone keeps the mesh's own.
        var translated = armsSets.Concat(weaponSets)
            .SelectMany(loaded =>
            {
                var prefix = weaponSets.Contains(loaded) ? CrossFireHDViewmodels.WeaponBonePrefix : string.Empty;
                return (loaded.Set.RotationOnly ? loaded.Set.TranslatedBones : loaded.Set.TrackBones).Select(bone => prefix + bone);
            })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var combined = new UE3AnimSet(mesh.Name, bones, [], RotationOnly: true, [.. bones.Where(translated.Contains)], null);
        UE3PsaWriter.Write(path, combined, clips);
    }

    // Each clip name's newest version: skins come after the base, so theirs win. Layers are left out.
    private static Dictionary<string, Clip> LatestClips(List<LoadedSet> sets, string bonePrefix)
    {
        Dictionary<string, Clip> clips = new(StringComparer.OrdinalIgnoreCase);
        foreach (var loaded in sets)
        {
            var trackBones = loaded.Set.TrackBones.Select(bone => bonePrefix + bone).ToArray();
            foreach (var sequence in loaded.Sequences.Where(sequence => !sequence.IsAdditive))
            {
                clips[sequence.Name] = new Clip(sequence, trackBones);
            }
        }

        return clips;
    }

    // A track sampled at the target clip's frames, so clips with different key counts line up in time.
    private static UE3AnimTrack Resample(UE3AnimTrack track, UE3AnimSequence source, UE3AnimSequence target, UE3AnimTrack fallback)
    {
        var positions = new Vector3[target.FrameCount];
        var rotations = new Quaternion[target.FrameCount];
        for (var frame = 0; frame < target.FrameCount; frame++)
        {
            var seconds = target.FrameCount > 1 ? frame / target.FrameRate : 0f;
            var sourceFrame = Math.Clamp(seconds * source.FrameRate, 0, source.FrameCount - 1);
            positions[frame] = track.SamplePosition(sourceFrame, source.FrameCount) ?? fallback.Positions[0];
            rotations[frame] = track.SampleRotation(sourceFrame, source.FrameCount) ?? fallback.Rotations[0];
        }

        return new UE3AnimTrack(positions, null, rotations, null);
    }

    private sealed record LoadedSet(UE3AnimSet Set, List<UE3AnimSequence> Sequences);

    private sealed record Clip(UE3AnimSequence Sequence, IReadOnlyList<string> TrackBones);
}
