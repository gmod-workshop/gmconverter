using System.Diagnostics;

using GMConverter.SDK.Common;

namespace GMConverter.SourceEngine.Common;

internal static class ProcessRunner
{
    public static void Run(string fileName, IEnumerable<string> arguments, string? workingDirectory = null)
    {
        // CreateNoWindow stops Windows from briefly flashing a console for each shelled-out tool
        // (vtfcmd/studiomdl run dozens to hundreds of times per export). Without stdio redirect
        // the child inherits the GUI's (absent) console, so its output is silently discarded —
        // that's what we want; non-zero exit codes still surface below.
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new GMConverterException($"Failed to start {fileName}.");
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new GMConverterException($"{Path.GetFileName(fileName)} exited with code {process.ExitCode}.");
        }
    }
}
