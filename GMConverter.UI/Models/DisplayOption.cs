namespace GMConverter.UI.Models;

public sealed record DisplayOption(string Value, string Label, string Name)
{
    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Name) ? Label : $"{Label} ({Name})";
    }
}
