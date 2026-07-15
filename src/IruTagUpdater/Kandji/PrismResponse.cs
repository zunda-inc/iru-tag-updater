using System.Text.Json.Serialization;

namespace IruTagUpdater.Kandji;

/// <summary>Prism カテゴリ検索のレスポンス。</summary>
public sealed class PrismResponse
{
    [JsonPropertyName("data")]
    public IReadOnlyList<PrismRow> Data { get; init; } = [];

    /// <summary>次ページ用カーソル。null / 空 ならページ終端。</summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}

/// <summary>
/// Prism の 1 行。どのカテゴリでもデバイス共通列を含むため、必要な列だけ拾う。
/// </summary>
public sealed class PrismRow
{
    [JsonPropertyName("device_id")]
    public string DeviceId { get; init; } = string.Empty;

    [JsonPropertyName("device__name")]
    public string? DeviceName { get; init; }

    /// <summary>デバイスに付与済みのタグ (名前)。未付与なら null。</summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string>? Tags { get; init; }
}
