using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GMConverter.SDK.Exporters;

namespace GMConverter.UI.ViewModels.Options;

/// <summary>
/// One-of-N string option. Rendered as a ComboBox. <see cref="Choices"/> mirrors the schema's
/// allowed values; <see cref="Value"/> is one of them (or the default).
/// </summary>
public sealed partial class EnumOptionViewModel : OptionViewModel
{
    [ObservableProperty]
    private string? _value;

    public ObservableCollection<string> Choices { get; }

    public EnumOptionViewModel(OptionDescriptor descriptor) : base(descriptor)
    {
        Choices = descriptor.Choices is null ? [] : [.. descriptor.Choices];
        Value = descriptor.ResolveDefault() as string ?? Choices.FirstOrDefault();
    }

    public override object? GetCurrentValue() => Value;

    public override bool TryLoad(object? value)
    {
        value = UnwrapJsonValue(value);
        if (value is string s && Choices.Contains(s))
        {
            Value = s;
            return true;
        }
        return false;
    }
}
