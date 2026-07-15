using System.Text.Json.Serialization;

namespace IruTagUpdater.Kandji;

/// <summary>GET /api/v1/tags のレスポンス (DRF 形式ページネーション)。</summary>
public sealed class TagListResponse
{
    [JsonPropertyName("next")]
    public string? Next { get; init; }

    [JsonPropertyName("results")]
    public IReadOnlyList<TagInfo> Results { get; init; } = [];
}

/// <summary>タグ 1 件 (ID と名前)。</summary>
public sealed class TagInfo
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}
