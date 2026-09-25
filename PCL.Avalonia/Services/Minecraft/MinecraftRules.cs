using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PCL.Avalonia.Services.Minecraft;

public static class MinecraftRules
{
    public static string CurrentOsName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "osx";
        }

        return "linux";
    }

    public static string CurrentArchName()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "unknown",
        };
    }

    public static bool RulesMatch(IReadOnlyList<RuleJson>? rules)
    {
        if (rules is null || rules.Count == 0)
        {
            return true;
        }

        var required = false;
        foreach (var rule in rules)
        {
            if (!MatchRule(rule))
            {
                continue;
            }

            required = !string.Equals(rule.Action, "disallow", StringComparison.OrdinalIgnoreCase);
        }

        return required;
    }

    public static string? ResolveNativeTemplate(LibraryJson library)
    {
        if (library.Natives is null)
        {
            return null;
        }

        return CurrentOsName() switch
        {
            "windows" => library.Natives.GetValueOrDefault("windows"),
            "osx" => library.Natives.GetValueOrDefault("osx"),
            _ => library.Natives.GetValueOrDefault("linux"),
        };
    }

    public static string ResolveNativeClassifier(string template)
    {
        return template
            .Replace("${arch}", Environment.Is64BitProcess ? "64" : "32")
            .Trim();
    }

    private static bool MatchRule(RuleJson rule)
    {
        if (rule.Os is not null)
        {
            if (!string.IsNullOrWhiteSpace(rule.Os.Name)
                && !string.Equals(rule.Os.Name, CurrentOsName(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(rule.Os.Arch)
                && !string.Equals(rule.Os.Arch, CurrentArchName(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(rule.Os.Version)
                && !Regex.IsMatch(Environment.OSVersion.VersionString, rule.Os.Version))
            {
                return false;
            }
        }

        if (rule.Features is not null)
        {
            foreach (var (name, required) in rule.Features)
            {
                if (name == "is_demo_user" || name.Contains("quick_play", StringComparison.Ordinal))
                {
                    return false;
                }

                if (name == "has_custom_resolution" || required)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
