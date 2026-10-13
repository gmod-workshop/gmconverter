using GMConverter.SDK.Explorer;
using GMConverter.SDK.Games;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Options;

namespace GMConverter.SourceEngine.Games;

/// <summary>
/// Garry's Mod, the MDL exporter's default target. It adds the attachments Garry's Mod code looks
/// up by name; later steps add its activity set and player-model conventions.
/// </summary>
internal sealed class GarrysModGameProfile : IExportGameProfile
{
    public const string HandAttachmentsOption = "handAttachments";

    public string Id => "garrysmod";

    public string DisplayName => "Garry's Mod";

    public string Engine => "Source";

    public string ExporterFormat => "mdl";

    public OptionSchema OptionSchema { get; } = new(
    [
        new OptionGroup("garrysmod", "Garry's Mod",
        [
            new OptionDescriptor(HandAttachmentsOption, OptionType.Bool, "Hand attachments")
            {
                DefaultValue = true,
                Description = "Add anim_attachment_RH and anim_attachment_LH on the hands, copied from the model's hand sockets, which Garry's Mod uses to place held items.",
            },
        ]),
    ]);

    /// <summary>Exact for a <c>garrysmod</c> folder holding <c>gameinfo.txt</c>, or a folder containing one.</summary>
    public GameMatch Detect(ExplorerTarget target)
    {
        if (!target.IsDirectory)
        {
            return GameMatch.None;
        }

        var isGameFolder = string.Equals(Path.GetFileName(target.FullPath.TrimEnd(Path.DirectorySeparatorChar)), "garrysmod", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Join(target.FullPath, "gameinfo.txt"));
        return isGameFolder || File.Exists(Path.Join(target.FullPath, "garrysmod", "gameinfo.txt"))
            ? GameMatch.Exact
            : GameMatch.None;
    }

    public Model Prepare(Model model, OptionValues options)
    {
        return options.GetBool(HandAttachmentsOption, defaultValue: true) ? HandAttachments.AddAliases(model) : model;
    }
}
