using System.Collections.ObjectModel;
using GMConverter.SDK.Options;

namespace GMConverter.UI.ViewModels.Options;

/// <summary>
/// A group of options in an exporter's schema, rendered as a section in the generic options panel.
/// </summary>
public sealed class OptionGroupViewModel
{
    public string Key { get; }

    public string Label { get; }

    public ObservableCollection<OptionViewModel> Options { get; }

    public OptionGroupViewModel(OptionGroup group)
    {
        Key = group.Key;
        Label = group.Label;
        Options = [.. group.Options.Select(BuildOption)];
    }

    private static OptionViewModel BuildOption(OptionDescriptor descriptor)
    {
        return descriptor.Type switch
        {
            OptionType.String => new StringOptionViewModel(descriptor),
            OptionType.Path => new PathOptionViewModel(descriptor),
            OptionType.Bool => new BoolOptionViewModel(descriptor),
            OptionType.Int => new IntOptionViewModel(descriptor),
            OptionType.Float => new FloatOptionViewModel(descriptor),
            OptionType.Enum => new EnumOptionViewModel(descriptor),
            _ => throw new ArgumentOutOfRangeException(nameof(descriptor), $"Unknown option type {descriptor.Type}."),
        };
    }
}
