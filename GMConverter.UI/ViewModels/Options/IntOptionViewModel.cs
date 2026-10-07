using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GMConverter.SDK.Exporters;

namespace GMConverter.UI.ViewModels.Options;

public sealed partial class IntOptionViewModel : OptionViewModel
{
    [ObservableProperty]
    private int _value;

    public IntOptionViewModel(OptionDescriptor descriptor) : base(descriptor)
    {
        Value = descriptor.ResolveDefault() switch
        {
            int i => i,
            long l => checked((int)l),
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0,
        };
    }

    public override object? GetCurrentValue() => Value;

    public override bool TryLoad(object? value)
    {
        value = UnwrapJsonValue(value);
        int? parsedValue = value switch
        {
            int i => i,
            long l when l >= int.MinValue && l <= int.MaxValue => (int)l,
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
        if (parsedValue is { } loaded && loaded >= Minimum && loaded <= Maximum)
        {
            Value = loaded;
            return true;
        }
        return false;
    }
}
