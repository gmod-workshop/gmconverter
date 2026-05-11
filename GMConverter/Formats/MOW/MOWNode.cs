namespace GMConverter.Formats.MOW;

internal sealed record MOWNode(
    string Name,
    IReadOnlyList<string> Values,
    IReadOnlyList<MOWNode> Children)
{
    public MOWNode? FirstChild(string name)
    {
        return Children.FirstOrDefault(child => string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<MOWNode> Descendants(string name)
    {
        foreach (var child in Children)
        {
            if (string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                yield return child;
            }

            foreach (var descendant in child.Descendants(name))
            {
                yield return descendant;
            }
        }
    }
}
