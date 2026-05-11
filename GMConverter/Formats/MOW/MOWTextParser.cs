using GMConverter.Common;

namespace GMConverter.Formats.MOW;

internal static class MOWTextParser
{
    public static MOWNode ParseFile(string path)
    {
        var text = string.Join(
            " ",
            File.ReadLines(path)
                .Select(StripComment)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0));

        return Parse(text, Path.GetFileName(path));
    }

    public static MOWNode Parse(string text, string documentName)
    {
        var tokens = Tokenize(text).ToArray();
        var index = 0;
        List<MOWNode> children = [];

        while (index < tokens.Length)
        {
            children.Add(ParseNode(tokens, ref index, documentName));
        }

        return new MOWNode("root", [], children);
    }

    private static MOWNode ParseNode(IReadOnlyList<string> tokens, ref int index, string documentName)
    {
        if (index >= tokens.Count || tokens[index] != "{")
        {
            throw new GMConverterException($"Expected '{{' while parsing {documentName}.");
        }

        index++;
        if (index >= tokens.Count || tokens[index] is "{" or "}")
        {
            throw new GMConverterException($"Expected node name while parsing {documentName}.");
        }

        var name = tokens[index++];
        List<string> values = [];
        List<MOWNode> children = [];

        while (index < tokens.Count && tokens[index] != "}")
        {
            if (tokens[index] == "{")
            {
                children.Add(ParseNode(tokens, ref index, documentName));
            }
            else
            {
                values.Add(tokens[index++]);
            }
        }

        if (index >= tokens.Count || tokens[index] != "}")
        {
            throw new GMConverterException($"Unclosed node '{name}' while parsing {documentName}.");
        }

        index++;
        return new MOWNode(name, values, children);
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            if (index >= text.Length)
            {
                yield break;
            }

            var c = text[index];
            if (c is '{' or '}')
            {
                index++;
                yield return c.ToString();
                continue;
            }

            if (c == '"')
            {
                index++;
                var start = index;
                while (index < text.Length && text[index] != '"')
                {
                    index++;
                }

                yield return text[start..index];
                if (index < text.Length)
                {
                    index++;
                }

                continue;
            }

            var tokenStart = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] is not '{' and not '}')
            {
                index++;
            }

            yield return text[tokenStart..index];
        }
    }

    private static string StripComment(string line)
    {
        var inQuote = false;
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && line[index] == ';')
            {
                return line[..index];
            }
        }

        return line;
    }
}
