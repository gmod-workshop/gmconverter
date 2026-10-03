using System.Text.Json;
using GMConverter.SDK.Exporters;

namespace GMConverter.UI.ViewModels.Options;

/// <summary>
/// Base class for a single configurable option in an exporter's schema. The Avalonia
/// <c>DataTemplate</c> matching system picks a per-type DataTemplate (TextBox for
/// <see cref="StringOptionViewModel"/>, CheckBox for <see cref="BoolOptionViewModel"/>, etc.)
/// based on the concrete subclass at render time, so a single ItemsControl bound to a
/// heterogeneous collection produces the right control per option.
/// </summary>
public abstract class OptionViewModel : ViewModelBase
{
    public string Key { get; }

    public string Label { get; }

    public string? Description { get; }

    public decimal Minimum { get; }

    public decimal Maximum { get; }

    public decimal Increment { get; }

    protected static object? UnwrapJsonValue(object? value)
    {
        return value is JsonElement json ? json.ValueKind switch
        {
            JsonValueKind.String => json.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when json.TryGetInt32(out var number) => number,
            JsonValueKind.Number => json.GetDouble(),
            JsonValueKind.Null => null,
            _ => json
        } : value;
    }

    protected OptionViewModel(OptionDescriptor descriptor)
    {
        Key = descriptor.Key;
        Label = descriptor.Label;
        Description = descriptor.Description;
        Minimum = descriptor.Minimum ?? (descriptor.Type == OptionType.Int ? int.MinValue : decimal.MinValue);
        Maximum = descriptor.Maximum ?? (descriptor.Type == OptionType.Int ? int.MaxValue : decimal.MaxValue);
        Increment = descriptor.Increment ?? (descriptor.Type == OptionType.Float ? 0.01m : 1m);
    }

    /// <summary>
    /// Snapshot of the current value, boxed to object. The host builds an
    /// <see cref="ExportOptions"/> bag by collecting <see cref="GetCurrentValue"/> across all
    /// options just before invoking the exporter.
    /// </summary>
    public abstract object? GetCurrentValue();

    /// <summary>
    /// Replace the option's value from a persisted store. Returns false if the value's runtime
    /// type doesn't match what this VM expects — caller can decide whether to skip silently or
    /// surface an error. Default implementation handles the common case of an already-typed
    /// value; subclasses override for type coercion (e.g. string → int for legacy JSON loads).
    /// </summary>
    public abstract bool TryLoad(object? value);
}
