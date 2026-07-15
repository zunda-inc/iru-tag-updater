using IruTagUpdater.Configuration;
using IruTagUpdater.Kandji;
using Microsoft.Extensions.Logging;

namespace IruTagUpdater;

/// <summary>1 つの <see cref="TagUpdate"/> の処理結果。</summary>
public sealed record TagUpdateResult(
    string Tag,
    int MatchedDevices,
    int AlreadyTagged,
    int Tagged);

/// <summary>
/// コアアルゴリズム: 各 update について全条件を満たす (AND / 積集合) デバイスを求め、
/// タグ未付与のデバイスにのみタグを付与する。条件から外れてもタグは剥がさない (追加のみ)。
/// </summary>
public sealed class TagUpdateService(
    IKandjiClient client,
    ILogger<TagUpdateService> logger)
{
    public async Task<IReadOnlyList<TagUpdateResult>> RunAsync(
        AppConfig config,
        IReadOnlyDictionary<string, ResolvedTag> resolvedTags,
        bool dryRun,
        CancellationToken ct = default)
    {
        var results = new List<TagUpdateResult>();
        foreach (var update in config.Updates)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await ProcessUpdateAsync(update, resolvedTags[update.Tag], dryRun, ct));
        }
        return results;
    }

    private async Task<TagUpdateResult> ProcessUpdateAsync(
        TagUpdate update, ResolvedTag tag, bool dryRun, CancellationToken ct)
    {
        logger.LogInformation("タグ '{Tag}' (ID {Id}) の処理を開始します。", tag.Name, tag.Id);

        // 条件ごとに Prism を検索し、device_id -> 現在のタグ の対応と、各条件のデバイス集合を作る。
        var currentTagsByDevice = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        HashSet<string>? intersection = null;

        foreach (var condition in update.Conditions)
        {
            var rows = await client.QueryPrismAsync(condition.Query, condition.Filter!, ct);
            var deviceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (string.IsNullOrEmpty(row.DeviceId))
                    continue;
                deviceIds.Add(row.DeviceId);
                currentTagsByDevice[row.DeviceId] = row.Tags ?? [];
            }

            logger.LogInformation("  条件 query='{Query}' に {Count} 台が合致しました。",
                condition.Query, deviceIds.Count);

            if (intersection is null)
                intersection = deviceIds;
            else
                intersection.IntersectWith(deviceIds);

            // 積集合が空になったら以降の条件を評価しても無駄。
            if (intersection.Count == 0)
                break;
        }

        var matched = intersection ?? [];
        logger.LogInformation("  全条件を満たすデバイス: {Count} 台。", matched.Count);

        // 既にタグ付きのデバイスを除外する (Prism 行の tags で判定)。
        var candidates = matched
            .Where(id => !HasTag(currentTagsByDevice.GetValueOrDefault(id, []), tag.Name))
            .ToList();

        var alreadyTagged = matched.Count - candidates.Count;
        logger.LogInformation("  付与済み: {Already} 台 / 新規付与対象: {Candidates} 台。",
            alreadyTagged, candidates.Count);

        var tagged = 0;
        var raceSkipped = 0; // Prism では未付与だが GET 時点で付与済みだった (キャッシュ遅延) デバイス。
        foreach (var deviceId in candidates)
        {
            ct.ThrowIfCancellationRequested();

            if (dryRun)
            {
                logger.LogInformation("  [DRY_RUN] device {DeviceId} に '{Tag}' を付与します (実行なし)。",
                    deviceId, tag.Name);
                tagged++;
                continue;
            }

            if (await ApplyTagAsync(deviceId, tag.Name, ct))
                tagged++;
            else
                raceSkipped++;
        }

        var reportedAlreadyTagged = alreadyTagged + raceSkipped;
        logger.LogInformation("タグ '{Tag}' の処理を完了しました。付与 {Tagged} 台 / 付与済みスキップ {Already} 台。",
            tag.Name, tagged, reportedAlreadyTagged);
        return new TagUpdateResult(tag.Name, matched.Count, reportedAlreadyTagged, tagged);
    }

    /// <summary>タグを付与する。実際に PATCH した場合は true、既に付与済みでスキップした場合は false。</summary>
    private async Task<bool> ApplyTagAsync(string deviceId, string tagName, CancellationToken ct)
    {
        // 他タグを消さないよう、PATCH 直前に最新のタグを取得して和集合を送る。
        var device = await client.GetDeviceAsync(deviceId, ct);
        var existing = device.Tags ?? [];

        if (HasTag(existing, tagName))
        {
            logger.LogInformation("  device {DeviceId} は取得時点で既に '{Tag}' 付き (Prism キャッシュ遅延)。スキップ。",
                deviceId, tagName);
            return false;
        }

        var union = new List<string>(existing);
        union.Add(tagName);

        await client.PatchTagsAsync(deviceId, union, ct);
        logger.LogInformation("  device {DeviceId} に '{Tag}' を付与しました。", deviceId, tagName);
        return true;
    }

    private static bool HasTag(IReadOnlyList<string> tags, string tagName)
        => tags.Contains(tagName, StringComparer.Ordinal);
}
