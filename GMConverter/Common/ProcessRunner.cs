using System.Diagnostics;

namespace GMConverter.Common;

internal static class ProcessRunner
{
    public static void Run(string fileName, IEnumerable<string> arguments, string? workingDirectory = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);

        if (process is null)
        {
            throw new GMConverterException($"Failed to start {fileName}.");
        }

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new GMConverterException($"{Path.GetFileName(fileName)} exited with code {process.ExitCode}.");
        }
    }
}
