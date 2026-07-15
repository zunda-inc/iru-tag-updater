using System.Text.Json.Serialization;

namespace IruTagUpdater.Kandji;

/// <summary>GET/PATCH /api/v1/devices/{id} のレスポンス (必要な列のみ)。</summary>
public sealed class DeviceRecord
{
    [JsonPropertyName("device_id")]
    public string DeviceId { get; init; } = string.Empty;

    [JsonPropertyName("device_name")]
    public string? DeviceName { get; init; }

    /// <summary>デバイスに付与済みのタグ (名前)。未付与なら null または空配列。</summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string>? Tags { get; init; }
}
