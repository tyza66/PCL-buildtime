using System.Text.RegularExpressions;

namespace PCL.Avalonia.Services.Minecraft;

public interface IInstanceClassifier
{
    IReadOnlyDictionary<InstanceGroup, IReadOnlyList<VersionInstance>> Group(
        IEnumerable<VersionInstance> instances,
        bool showHidden);
}

public enum InstanceGroup
{
    Star,
    Api,
    OriginalLike,
    Rubbish,
    Fool,
    Error,
    Hidden,
}

public sealed record VersionInstance(MinecraftVersion Version, VersionSettings Settings)
{
    public bool IsFavorite => Settings.IsFavorite;

    public bool IsHidden => Settings.IsHidden
        || Settings.DisplayType == InstanceDisplayType.Hidden;
}

public sealed class InstanceClassifier : IInstanceClassifier
{
    public IReadOnlyDictionary<InstanceGroup, IReadOnlyList<VersionInstance>> Group(
        IEnumerable<VersionInstance> instances,
        bool showHidden)
    {
        ArgumentNullException.ThrowIfNull(instances);
        var result = new Dictionary<InstanceGroup, List<VersionInstance>>
        {
            [InstanceGroup.Star] = [],
            [InstanceGroup.Api] = [],
            [InstanceGroup.OriginalLike] = [],
            [InstanceGroup.Rubbish] = [],
            [InstanceGroup.Fool] = [],
            [InstanceGroup.Error] = [],
            [InstanceGroup.Hidden] = [],
        };

        var remaining = instances.ToList();
        if (!showHidden)
        {
            remaining.RemoveAll(instance => instance.IsHidden);
        }

        var assigned = new HashSet<VersionInstance>();
        foreach (var instance in remaining.ToList())
        {
            if (!instance.IsFavorite || instance.IsHidden)
            {
                continue;
            }

            result[InstanceGroup.Star].Add(instance);
            assigned.Add(instance);
            remaining.Remove(instance);
        }

        Filter(remaining, assigned, result[InstanceGroup.Error],
            instance => instance.Version.State == InstanceState.Error);
        Filter(remaining, assigned, result[InstanceGroup.Fool],
            instance => instance.Version.State == InstanceState.Fool);
        Filter(remaining, assigned, result[InstanceGroup.Api],
            instance => instance.Version.State is
                InstanceState.Forge
                or InstanceState.NeoForge
                or InstanceState.LiteLoader
                or InstanceState.Fabric);

        var rubbish = result[InstanceGroup.Rubbish];
        Filter(remaining, assigned, rubbish,
            instance => instance.Version.State == InstanceState.Old);

        var useful = new List<VersionInstance>();
        var largest = remaining
            .Where(instance => instance.Version.State is
                InstanceState.Original
                or InstanceState.Snapshot)
            .OrderByDescending(instance => instance.Version.ReleaseTime)
            .FirstOrDefault();
        if (largest is not null && largest.Version.State == InstanceState.Snapshot)
        {
            useful.Add(largest);
            remaining.Remove(largest);
        }

        Filter(remaining, assigned, rubbish,
            instance => instance.Version.State == InstanceState.Snapshot);

        var newerByDropAndState = new Dictionary<string, VersionInstance>();
        var existingDrops = new List<int>();
        foreach (var instance in remaining)
        {
            var drop = instance.Version.Drop;
            if (drop is <= 0 or >= 1000)
            {
                continue;
            }

            if (!existingDrops.Contains(drop))
            {
                existingDrops.Add(drop);
            }

            var key = drop + "-" + instance.Version.State;
            if (!newerByDropAndState.TryGetValue(key, out var current))
            {
                newerByDropAndState[key] = instance;
                continue;
            }

            if (instance.Version.State == InstanceState.OptiFine)
            {
                if (OptiFineCode(instance.Version.LoaderVersion)
                    > OptiFineCode(current.Version.LoaderVersion))
                {
                    newerByDropAndState[key] = instance;
                }
            }
            else if (instance.Version.ReleaseTime > current.Version.ReleaseTime)
            {
                newerByDropAndState[key] = instance;
            }
        }

        foreach (var drop in existingDrops)
        {
            var hasOptiFine = newerByDropAndState.TryGetValue(
                drop + "-" + InstanceState.OptiFine,
                out var optiFine);
            var hasOriginal = newerByDropAndState.TryGetValue(
                drop + "-" + InstanceState.Original,
                out var original);
            if (hasOptiFine && hasOriginal)
            {
                if (original!.Version.Drop > optiFine!.Version.Drop)
                {
                    useful.Add(original);
                    remaining.Remove(original);
                }

                useful.Add(optiFine);
                remaining.Remove(optiFine);
            }
            else if (hasOptiFine)
            {
                useful.Add(optiFine!);
                remaining.Remove(optiFine!);
            }
            else if (hasOriginal)
            {
                useful.Add(original!);
                remaining.Remove(original!);
            }
        }

        if (useful.Count > 0)
        {
            result[InstanceGroup.OriginalLike].AddRange(useful);
            assigned.UnionWith(useful);
        }

        rubbish.AddRange(remaining);
        assigned.UnionWith(remaining);

        foreach (var instance in assigned.ToList())
        {
            if (instance.IsHidden)
            {
                foreach (var group in result.Values)
                {
                    group.Remove(instance);
                }

                if (showHidden)
                {
                    result[InstanceGroup.Hidden].Add(instance);
                }

                continue;
            }

            if (instance.Settings.DisplayType == InstanceDisplayType.Auto)
            {
                continue;
            }

            var target = ToGroup(instance.Settings.DisplayType);
            var current = FindGroup(result, instance);
            if (current == result[InstanceGroup.Star])
            {
                continue;
            }

            if (current is not null && current != result[target])
            {
                current.Remove(instance);
                result[target].Add(instance);
            }
        }

        foreach (var group in new[]
        {
            InstanceGroup.Star,
            InstanceGroup.Api,
            InstanceGroup.OriginalLike,
            InstanceGroup.Rubbish,
            InstanceGroup.Fool,
        })
        {
            result[group].Sort(CompareCardItems);
        }

        return result.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<VersionInstance>)pair.Value);
    }

    private static void Filter(
        List<VersionInstance> source,
        HashSet<VersionInstance> assigned,
        List<VersionInstance> target,
        Func<VersionInstance, bool> predicate)
    {
        var matches = source.Where(predicate).ToList();
        foreach (var instance in matches)
        {
            source.Remove(instance);
            assigned.Add(instance);
            target.Add(instance);
        }
    }

    private static InstanceGroup ToGroup(InstanceDisplayType displayType)
    {
        return displayType switch
        {
            InstanceDisplayType.Original => InstanceGroup.OriginalLike,
            InstanceDisplayType.Api => InstanceGroup.Api,
            InstanceDisplayType.Rubbish => InstanceGroup.Rubbish,
            InstanceDisplayType.Star => InstanceGroup.Star,
            InstanceDisplayType.Fool => InstanceGroup.Fool,
            InstanceDisplayType.Hidden => InstanceGroup.Hidden,
            _ => InstanceGroup.OriginalLike,
        };
    }

    private static List<VersionInstance>? FindGroup(
        IReadOnlyDictionary<InstanceGroup, List<VersionInstance>> groups,
        VersionInstance instance)
    {
        foreach (var group in groups.Values)
        {
            if (group.Contains(instance))
            {
                return group;
            }
        }

        return null;
    }

    private static int OptiFineCode(string? value)
    {
        if (string.IsNullOrEmpty(value) || value == "未知版本")
        {
            return 0;
        }

        var letter = value.ToUpperInvariant()[0];
        var result = letter - 'A' + 1;
        var digits = Regex.Match(value[1..], "[0-9]+");
        result *= 100;
        result += digits.Success ? int.Parse(digits.Value) : 0;
        result *= 100;
        if (value.Contains("pre", StringComparison.OrdinalIgnoreCase))
        {
            result += 50;
        }

        if (value.Contains("pre", StringComparison.OrdinalIgnoreCase)
            || value.Contains("beta", StringComparison.OrdinalIgnoreCase))
        {
            var number = Regex.Match(value, "(?<=((pre)|(beta)))[0-9]+",
                RegexOptions.IgnoreCase);
            result += number.Success ? int.Parse(number.Value) : 1;
        }
        else
        {
            result += 99;
        }

        return result;
    }

    private static int CompareCardItems(VersionInstance left, VersionInstance right)
    {
        var leftTime = left.Version.ReleaseTime;
        var rightTime = right.Version.ReleaseTime;
        if ((leftTime.Year >= 2000 || rightTime.Year >= 2000) && leftTime != rightTime)
        {
            return rightTime.CompareTo(leftTime);
        }

        if (HasLoader(left, LoaderKind.Fabric) != HasLoader(right, LoaderKind.Fabric))
        {
            return HasLoader(left, LoaderKind.Fabric) ? -1 : 1;
        }

        if (HasLoader(left, LoaderKind.NeoForge) != HasLoader(right, LoaderKind.NeoForge))
        {
            return HasLoader(left, LoaderKind.NeoForge) ? -1 : 1;
        }

        if (HasLoader(left, LoaderKind.Forge) != HasLoader(right, LoaderKind.Forge))
        {
            return HasLoader(left, LoaderKind.Forge) ? -1 : 1;
        }

        if (HasLoader(left, LoaderKind.OptiFine) != HasLoader(right, LoaderKind.OptiFine))
        {
            return HasLoader(left, LoaderKind.OptiFine) ? -1 : 1;
        }

        if (HasLoader(left, LoaderKind.LiteLoader) != HasLoader(right, LoaderKind.LiteLoader))
        {
            return HasLoader(left, LoaderKind.LiteLoader) ? -1 : 1;
        }

        var leftCode = ComponentCode(left.Version);
        var rightCode = ComponentCode(right.Version);
        if (leftCode != rightCode)
        {
            return rightCode.CompareTo(leftCode);
        }

        return StringComparer.OrdinalIgnoreCase.Compare(right.Version.Id, left.Version.Id);
    }

    private static bool HasLoader(VersionInstance instance, LoaderKind loader)
    {
        return instance.Version.Loader == loader;
    }

    private static int ComponentCode(MinecraftVersion version)
    {
        if (version.Loader is LoaderKind.Forge or LoaderKind.NeoForge)
        {
            var code = ForgelikeCode(version.LoaderVersion);
            if (code > 0)
            {
                return code;
            }
        }

        if (version.Loader == LoaderKind.OptiFine)
        {
            return OptiFineCode(version.LoaderVersion);
        }

        return 0;
    }

    private static int ForgelikeCode(string? value)
    {
        if (string.IsNullOrEmpty(value) || value == "未知版本")
        {
            return 0;
        }

        var segments = Regex.Matches(value, "\\d+")
            .Select(match => match.Value)
            .ToList();
        if (segments.Count == 0)
        {
            return 0;
        }

        var first = int.Parse(segments[0]);
        if (segments.Count > 4)
        {
            return first * 1_000_000
                + int.Parse(segments[1]) * 10_000
                + int.Parse(segments[3]);
        }

        if (segments.Count == 3)
        {
            return first * 1_000_000
                + int.Parse(segments[1]) * 10_000
                + int.Parse(segments[2]);
        }

        if (segments.Count == 2)
        {
            return first * 1_000_000 + int.Parse(segments[1]) * 10_000;
        }

        return first * 1_000_000;
    }
}
