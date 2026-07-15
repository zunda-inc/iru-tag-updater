using IruTagUpdater.Configuration;
using IruTagUpdater.Kandji;
using Microsoft.Extensions.Logging;

namespace IruTagUpdater;

/// <summary>タグ名 → タグ ID を表す。</summary>
public sealed record ResolvedTag(string Name, string Id);

/// <summary>
/// 起動時にタグ一覧を取得し、config のタグ名を ID へ解決する。
/// 1 つでも解決できないタグがあれば <see cref="InvalidConfigException"/> を投げて終了させる。
/// </summary>
public sealed class TagResolver(IKandjiClient client, ILogger<TagResolver> logger)
{
    public async Task<IReadOnlyDictionary<string, ResolvedTag>> ResolveAsync(
        IEnumerable<string> tagNames, CancellationToken ct = default)
    {
        var wanted = tagNames.Distinct(StringComparer.Ordinal).ToList();

        var allTags = await client.ListTagsAsync(ct);
        logger.LogInformation("タグ一覧を {Count} 件取得しました。", allTags.Count);

        var byName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var tag in allTags)
        {
            if (!byName.TryGetValue(tag.Name, out var ids))
            {
                ids = [];
                byName[tag.Name] = ids;
            }
            ids.Add(tag.Id);
        }

        var resolved = new Dictionary<string, ResolvedTag>(StringComparer.Ordinal);
        var missing = new List<string>();
        var ambiguous = new List<string>();

        foreach (var name in wanted)
        {
            if (!byName.TryGetValue(name, out var ids) || ids.Count == 0)
            {
                missing.Add(name);
            }
            else if (ids.Count > 1)
            {
                ambiguous.Add(name);
            }
            else
            {
                resolved[name] = new ResolvedTag(name, ids[0]);
                logger.LogInformation("タグ '{Name}' を ID {Id} に解決しました。", name, ids[0]);
            }
        }

        if (missing.Count > 0 || ambiguous.Count > 0)
        {
            var parts = new List<string>();
            if (missing.Count > 0)
                parts.Add($"存在しないタグ: {string.Join(", ", missing)}");
            if (ambiguous.Count > 0)
                parts.Add($"同名が複数あり ID を一意に決められないタグ: {string.Join(", ", ambiguous)}");
            throw new InvalidConfigException($"タグ ID を解決できませんでした。{string.Join(" / ", parts)}");
        }

        return resolved;
    }
}
