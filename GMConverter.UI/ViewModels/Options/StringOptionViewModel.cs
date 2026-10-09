using CommunityToolkit.Mvvm.ComponentModel;
using GMConverter.SDK.Options;

namespace GMConverter.UI.ViewModels.Options;

public sealed partial class StringOptionViewModel : OptionViewModel
{
    [ObservableProperty]
    private string? _value;

    public StringOptionViewModel(OptionDescriptor descriptor) : base(descriptor)
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
