using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GMConverter.SDK.Options;

namespace GMConverter.UI.ViewModels.Options;

public sealed partial class FloatOptionViewModel : OptionViewModel
{
    [ObservableProperty]
    private double _value;

    public FloatOptionViewModel(OptionDescriptor descriptor) : base(descriptor)
    {
        Value = descriptor.ResolveDefault() switch
        {
            float f => f,
            double d => d,
            int i => i,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0.0,
        };
    }

    public override object? GetCurrentValue() => (float)Value;

    public override bool TryLoad(object? value)
    {
        value = UnwrapJsonValue(value);
        double? parsedValue = value switch
        {
            float f => f,
            double d => d,
            int i => i,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
        if (parsedValue is { } loaded && double.IsFinite(loaded) &&
            loaded >= (double)Minimum && loaded <= (double)Maximum)
        {
            Value = loaded;
            return true;
        }
        return false;
    }
}
