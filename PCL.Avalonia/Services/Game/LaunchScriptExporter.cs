using System.Text;
using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.Services.Game;

public sealed class LaunchScriptExporter : ILaunchScriptExporter
{
    public string Export(LaunchPlan plan, string filePath)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var extension = Path.GetExtension(filePath);
        var isWindows = extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(
            filePath,
            isWindows ? BuildBatch(plan) : BuildShell(plan),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        if (!isWindows)
        {
            TryMakeExecutable(path: filePath);
        }

        return filePath;
    }

    private static string BuildBatch(LaunchPlan plan)
    {
        var builder = new StringBuilder();
        builder.AppendLine("@echo off");
        builder.AppendLine("chcp 65001 >nul");
        builder.Append("cd /d ").Append(QuoteBatch(plan.WorkingDirectory)).AppendLine();
        builder.Append(QuoteBatch(plan.JavaExecutable));
        foreach (var argument in plan.Arguments)
        {
            builder.Append(' ').Append(QuoteBatch(argument));
        }

        builder.AppendLine();
        builder.AppendLine("if errorlevel 1 pause");
        return builder.ToString();
    }

    private static string BuildShell(LaunchPlan plan)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#!/bin/sh");
        builder.Append("cd ").Append(QuoteShell(plan.WorkingDirectory)).Append(" || exit 1").AppendLine();
        builder.Append("exec ").Append(QuoteShell(plan.JavaExecutable));
        foreach (var argument in plan.Arguments)
        {
            builder.Append(' ').Append(QuoteShell(argument));
        }

        builder.AppendLine();
        return builder.ToString();
    }

    private static string QuoteBatch(string value)
        => "\"" + value.Replace("%", "%%", StringComparison.Ordinal).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string QuoteShell(string value)
        => "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal) + "\"";

    private static void TryMakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
        }
    }
}
