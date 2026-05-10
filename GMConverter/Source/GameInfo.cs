using GMConverter.Common;

namespace GMConverter.Source;

internal sealed record GameInfo(
    string GameDirectory,
    string GameInfoPath,
    string? GameName,
    string? GameBin)
{
    public static GameInfo Load(string gameDirectory)
    {
        string fullGameDirectory = Path.GetFullPath(gameDirectory);

        if (!Directory.Exists(fullGameDirectory))
        {
            throw new GMConverterException($"Game directory not found: {fullGameDirectory}");
        }

        string gameInfoPath = Path.Combine(fullGameDirectory, "gameinfo.txt");

        if (!File.Exists(gameInfoPath))
        {
            throw new GMConverterException($"Game directory must contain gameinfo.txt: {fullGameDirectory}");
        }

        string? gameName = null;
        string? gameBin = null;

        foreach (string rawLine in File.ReadLines(gameInfoPath))
        {
            string line = StripComment(rawLine).Trim();

            if (line.Length == 0 || line is "{" or "}")
            {
                continue;
            }

            string[] tokens = Tokenize(line);

            if (tokens.Length < 2)
            {
                continue;
            }

            if (gameName is null && string.Equals(tokens[0], "game", StringComparison.OrdinalIgnoreCase))
            {
                gameName = tokens[1];
            }
            else if (gameBin is null && string.Equals(tokens[0], "gamebin", StringComparison.OrdinalIgnoreCase))
            {
                gameBin = tokens[1];
            }
        }

        return new GameInfo(fullGameDirectory, gameInfoPath, gameName, gameBin);
    }

    public string? InferEngineDirectory()
    {
        if (string.IsNullOrWhiteSpace(this.GameBin))
        {
            return null;
        }

        string normalizedGameBin = this.GameBin.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        string gameDirectoryName = Path.GetFileName(this.GameDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        DirectoryInfo? parent = Directory.GetParent(this.GameDirectory);

        if (Path.IsPathRooted(normalizedGameBin))
        {
            return InferEngineDirectoryFromGameBinPath(normalizedGameBin);
        }

        if (normalizedGameBin.StartsWith("|gameinfo_path|", StringComparison.OrdinalIgnoreCase))
        {
            return parent?.FullName;
        }

        string firstSegment = normalizedGameBin.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

        if (parent is not null && string.Equals(firstSegment, gameDirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            return parent.FullName;
        }

        return parent?.FullName;
    }

    private static string? InferEngineDirectoryFromGameBinPath(string gameBinPath)
    {
        var directory = new DirectoryInfo(gameBinPath);

        if (string.Equals(directory.Name, "bin", StringComparison.OrdinalIgnoreCase) && directory.Parent?.Parent is not null)
        {
            return directory.Parent.Parent.FullName;
        }

        return directory.Parent?.FullName;
    }

    private static string StripComment(string line)
    {
        bool inQuote = false;

        for (int i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && line[i] == '/' && line[i + 1] == '/')
            {
                return line[..i];
            }
        }

        return line;
    }

    private static string[] Tokenize(string line)
    {
        var tokens = new List<string>();
        int i = 0;

        while (i < line.Length)
        {
            while (i < line.Length && char.IsWhiteSpace(line[i]))
            {
                i++;
            }

            if (i >= line.Length)
            {
                break;
            }

            if (line[i] == '"')
            {
                int start = ++i;

                while (i < line.Length && line[i] != '"')
                {
                    i++;
                }

                tokens.Add(line[start..i]);

                if (i < line.Length && line[i] == '"')
                {
                    i++;
                }
            }
            else
            {
                int start = i;

                while (i < line.Length && !char.IsWhiteSpace(line[i]))
                {
                    i++;
                }

                tokens.Add(line[start..i]);
            }
        }

        return tokens.ToArray();
    }
}
