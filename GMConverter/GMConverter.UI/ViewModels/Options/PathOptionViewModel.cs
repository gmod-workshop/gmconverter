using CommunityToolkit.Mvvm.ComponentModel;
using GMConverter.SDK.Options;

namespace GMConverter.UI.ViewModels.Options;

/// <summary>
/// Path-typed option. Rendered with a TextBox + Browse button in the panel; semantically the
/// same as <see cref="StringOptionViewModel"/> but distinguished by type so the panel can pick
/// the right DataTemplate.
/// </summary>
public sealed partial class PathOptionViewModel : OptionViewModel
{
    [ObservableProperty]
    private string? _value;

    public PathOptionViewModel(OptionDescriptor descriptor) : base(descriptor)
    {
        Value = descriptor.ResolveDefault() as string;
    }

    public override object? GetCurrentValue() => Value;

    public override bool TryLoad(object? value)
    {
        value = UnwrapJsonValue(value);
        if (value is not null and not string)
        {
            return false;
        }
        Value = value as string;
        return true;
    }
}
