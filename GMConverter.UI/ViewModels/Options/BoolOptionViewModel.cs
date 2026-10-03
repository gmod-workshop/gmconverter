using CommunityToolkit.Mvvm.ComponentModel;
using GMConverter.SDK.Exporters;

namespace GMConverter.UI.ViewModels.Options;

public sealed partial class BoolOptionViewModel : OptionViewModel
{
    [ObservableProperty]
    private bool _value;

    public BoolOptionViewModel(OptionDescriptor descriptor) : base(descriptor)
    {
        Value = descriptor.ResolveDefault() is bool b && b;
    }

    public override object? GetCurrentValue() => Value;

    public override bool TryLoad(object? value)
    {
        value = UnwrapJsonValue(value);
        bool? parsedValue = value switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            _ => null,
        };
        if (parsedValue is { } loaded)
        {
            Value = loaded;
            return true;
        }
        return false;
    }
}
