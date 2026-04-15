using System.Diagnostics;
using OllamaSharp;

namespace MicroCode.Tools;

/// <summary>
/// A collection of unsafe tools that are not meant to be used in production
/// </summary>
public static class UnsafeTools
{
    /// <summary>
    /// Executes a bash command in the current working directory and returns stdout.
    /// </summary>
    /// <param name="command">The bash command to execute.</param>
    [OllamaTool]
    public static string UnsafeBash(string command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            WorkingDirectory = Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(command);

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }
}