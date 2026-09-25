namespace PCL.Avalonia.Services.Mods;

public sealed class ResourceSearcherService : IResourceSearchService
{
    private const int DefaultPageSize = 40;
    private readonly IModrinthApi _modrinthApi;
    private readonly ICurseForgeApi _curseForgeApi;

    public ResourceSearcherService(
        IModrinthApi modrinthApi,
        ICurseForgeApi curseForgeApi)
    {
        _modrinthApi = modrinthApi;
        _curseForgeApi = curseForgeApi;
    }

    public async Task<ResourceSearchResult> SearchAsync(
        ResourceType type,
        string query,
        string gameVersion,
        string loader,
        string tag,
        ResourceSource source,
        int page = 0,
        CancellationToken cancellationToken = default)
    {
        var effectiveQuery = ChineseSearchDictionary.TryTranslate(query, out var translated)
            ? translated
            : query.Trim();
        var offset = Math.Max(0, page) * DefaultPageSize;
        var isChineseSearch = ChineseSearchDictionary.ContainsChinese(query);

        var curseForgeTask = RunCurseForgeSearchAsync(
            type,
            effectiveQuery,
            gameVersion,
            loader,
            tag,
            source,
            offset,
            cancellationToken);
        var modrinthTask = RunModrinthSearchAsync(
            type,
            effectiveQuery,
            gameVersion,
            loader,
            tag,
            source,
            offset,
            cancellationToken);
        await Task.WhenAll(curseForgeTask, modrinthTask).ConfigureAwait(false);

        var curseForge = curseForgeTask.Result;
        var modrinth = modrinthTask.Result;
        var errors = new List<string>();
        if (curseForge.Error is not null)
        {
            errors.Add("CurseForge:" + curseForge.Error.Message);
        }

        if (modrinth.Error is not null)
        {
            errors.Add("Modrinth:" + modrinth.Error.Message);
        }

        var merged = MergeResults(curseForge.Items, modrinth.Items);
        if (merged.Count == 0)
        {
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join("；", errors));
            }

            if (isChineseSearch && type is ResourceType.Mod or ResourceType.DataPack)
            {
                throw new InvalidOperationException("无搜索结果，请尝试搜索其英文名称");
            }

            throw new InvalidOperationException("没有搜索结果");
        }

        var errorMessage = errors.Count switch
        {
            > 0 when curseForge.Error is not null => "无法连接到 CurseForge，所以目前仅显示了来自 Modrinth 的内容，搜索结果可能不全。请稍后再试，或使用 VPN 改善网络环境。",
            > 0 => "无法连接到 Modrinth，所以目前仅显示了来自 CurseForge 的内容，搜索结果可能不全。请稍后再试，或使用 VPN 改善网络环境。",
            _ => null,
        };
        var totalCount = Math.Max(curseForge.TotalCount, modrinth.TotalCount);
        return new ResourceSearchResult(merged, totalCount, errorMessage);
    }

    private async Task<SourceSearchPage> RunCurseForgeSearchAsync(
        ResourceType type,
        string query,
        string gameVersion,
        string loader,
        string tag,
        ResourceSource source,
        int offset,
        CancellationToken cancellationToken)
    {
        if (source != ResourceSource.All && source != ResourceSource.CurseForge)
        {
            return new SourceSearchPage([], 0, null);
        }

        var (curseForgeTag, _) = SplitTag(tag);
        if (curseForgeTag is null)
        {
            return new SourceSearchPage([], 0, null);
        }

        try
        {
            var page = await _curseForgeApi.SearchProjectsAsync(
                query,
                type.ToCurseForgeClassId(),
                gameVersion,
                EffectiveLoader(type, loader),
                curseForgeTag,
                offset,
                DefaultPageSize,
                cancellationToken).ConfigureAwait(false);
            var items = page.Projects
                .Where(project => type != ResourceType.ResourcePack || !project.Categories.Contains("数据包"))
                .Select(project => ToItem(project, type))
                .ToList();
            return new SourceSearchPage(items, page.TotalCount, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SourceSearchPage([], -1, ex);
        }
    }

    private async Task<SourceSearchPage> RunModrinthSearchAsync(
        ResourceType type,
        string query,
        string gameVersion,
        string loader,
        string tag,
        ResourceSource source,
        int offset,
        CancellationToken cancellationToken)
    {
        if (source != ResourceSource.All && source != ResourceSource.Modrinth)
        {
            return new SourceSearchPage([], 0, null);
        }

        var (_, modrinthTag) = SplitTag(tag);
        if (modrinthTag is null)
        {
            return new SourceSearchPage([], 0, null);
        }

        try
        {
            var page = await _modrinthApi.SearchProjectsAsync(
                query,
                gameVersion,
                EffectiveLoader(type, loader),
                type.ToModrinthProjectType(),
                offset,
                DefaultPageSize,
                modrinthTag,
                cancellationToken).ConfigureAwait(false);
            return new SourceSearchPage(
                page.Hits.Select(project => ToItem(project, type)).ToList(),
                page.TotalHits,
                null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SourceSearchPage([], -1, ex);
        }
    }

    private static IReadOnlyList<ResourceProjectItem> MergeResults(
        IReadOnlyList<ResourceProjectItem> curseForge,
        IReadOnlyList<ResourceProjectItem> modrinth)
    {
        var merged = new List<ResourceProjectItem>(curseForge);
        var seen = new HashSet<string>(curseForge.Select(NormalizeKey), StringComparer.OrdinalIgnoreCase);
        foreach (var item in modrinth)
        {
            if (seen.Add(NormalizeKey(item)))
            {
                merged.Add(item);
            }
        }

        return merged;
    }

    private static string NormalizeKey(ResourceProjectItem item)
        => new string(item.Title
            .Where(char.IsLetterOrDigit)
            .ToArray())
            .ToLowerInvariant();

    private static (string? CurseForge, string? Modrinth) SplitTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return (null, null);
        }

        var trimmed = tag.Trim();
        if (trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            return (null, trimmed[1..]);
        }

        if (trimmed.EndsWith("/", StringComparison.Ordinal))
        {
            return (trimmed[..^1], null);
        }

        var slash = trimmed.IndexOf('/');
        return slash < 0
            ? (trimmed, trimmed)
            : (trimmed[..slash], trimmed[(slash + 1)..]);
    }

    private static string EffectiveLoader(ResourceType type, string loader)
        => type == ResourceType.Mod && loader is not "" and not "any"
            ? loader.Trim()
            : "";

    private static ResourceProjectItem ToItem(ModrinthProject project, ResourceType type)
    {
        var author = string.IsNullOrWhiteSpace(project.Author)
            ? (string.IsNullOrWhiteSpace(project.Slug) ? project.ProjectId : project.Slug)
            : project.Author;
        return new ResourceProjectItem
        {
            ProjectId = project.ProjectId,
            Title = project.Title,
            Description = project.Description,
            AuthorText = author,
            DownloadsText = FormatDownloads(project.Downloads),
            CategoriesText = string.Join(" · ", project.Categories),
            Source = ResourceSource.Modrinth,
            Type = type,
        };
    }

    private static ResourceProjectItem ToItem(CurseForgeProject project, ResourceType type)
    {
        var author = project.Authors.Count > 0
            ? project.Authors[0].Name
            : (string.IsNullOrWhiteSpace(project.Slug) ? project.Id.ToString() : project.Slug);
        return new ResourceProjectItem
        {
            ProjectId = project.Id.ToString(),
            Title = project.Name,
            Description = project.Summary,
            AuthorText = author,
            DownloadsText = FormatDownloads(project.DownloadCount),
            CategoriesText = string.Join(" · ", project.Categories),
            Source = ResourceSource.CurseForge,
            Type = type,
        };
    }

    private static string FormatDownloads(int downloads)
    {
        if (downloads >= 1_000_000)
        {
            return $"{downloads / 1_000_000.0:F1}M 下载";
        }

        if (downloads >= 1_000)
        {
            return $"{downloads / 1_000.0:F1}K 下载";
        }

        return $"{downloads} 下载";
    }

    private sealed record SourceSearchPage(
        IReadOnlyList<ResourceProjectItem> Items,
        int TotalCount,
        Exception? Error);
}
