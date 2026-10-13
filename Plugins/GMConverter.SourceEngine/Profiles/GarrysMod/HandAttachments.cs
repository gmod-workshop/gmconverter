using GMConverter.SDK.Geometry;
using GMConverter.SourceEngine.Common;

namespace GMConverter.SourceEngine.Profiles.GarrysMod;

// Garry's Mod code finds a character's hands by the attachments anim_attachment_RH and
// anim_attachment_LH (to place held props, effects and some weapon setups), which models built on
// other skeletons don't have. They are added as copies of the model's own hand socket when one is
// recognisable by name, or on the hand bone otherwise.
internal static class HandAttachments
{
    public const string RightName = "anim_attachment_RH";
    public const string LeftName = "anim_attachment_LH";

    // Higher wins: a weapon socket on the hand is where held items go, then a hand socket, then a
    // generic prop or grip socket.
    private static readonly (string Word, int Score)[] _socketWords = [("weapon", 3), ("gun", 3), ("hand", 2), ("prop", 1), ("grip", 1)];
    private static readonly string[] _ignoredWords = ["aim", "ik", "target", "fx"];

    public static Model AddAliases(Model model)
    {
        var attachments = model.Attachments?.ToList() ?? [];
        var added = false;
        foreach (var (name, isRight) in new[] { (RightName, true), (LeftName, false) })
        {
            if (attachments.Any(attachment => string.Equals(attachment.Name, name, StringComparison.OrdinalIgnoreCase)) ||
                FindAlias(model, attachments, isRight) is not { } alias)
            {
                continue;
            }

            attachments.Add(alias with { Name = name });
            added = true;
        }

        return added ? model with { Attachments = attachments } : model;
    }

    private static Attachment? FindAlias(Model model, IReadOnlyList<Attachment> attachments, bool isRight)
    {
        var socket = attachments
            .Select(attachment => (Attachment: attachment, Score: ScoreSocket(attachment.Name, isRight)))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .Select(candidate => candidate.Attachment)
            .FirstOrDefault();
        if (socket is not null)
        {
            return socket;
        }

        var handBone = model.Skeleton?.Bones.FirstOrDefault(bone => IsHandBone(bone.Name, isRight));
        return handBone is null ? null : new Attachment(string.Empty, handBone.Index, Transform.Identity);
    }

    private static int ScoreSocket(string name, bool isRight)
    {
        var words = NameWords.Split(name);
        if (!IsSide(words, isRight) || words.Any(word => _ignoredWords.Contains(word)))
        {
            return 0;
        }

        return _socketWords
            .Where(socketWord => words.Contains(socketWord.Word))
            .Select(socketWord => socketWord.Score)
            .DefaultIfEmpty(0)
            .Max();
    }

    private static bool IsHandBone(string name, bool isRight)
    {
        var words = NameWords.Split(name);
        return words.Contains("hand") && IsSide(words, isRight) && !words.Any(word => _ignoredWords.Contains(word));
    }

    private static bool IsSide(List<string> words, bool isRight)
    {
        var isLeft = NameWords.IsLeft(words);
        var right = NameWords.IsRight(words);
        return isRight ? right && !isLeft : isLeft && !right;
    }
}
