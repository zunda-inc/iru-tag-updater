using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using IruTagUpdater.Kandji;

namespace IruTagUpdater.Tests;

/// <summary>テスト用の差し替え可能な <see cref="IKandjiClient"/>。</summary>
public sealed class FakeKandjiClient : IKandjiClient
{
    public List<TagInfo> Tags { get; } = [];

    /// <summary>category -> 返す Prism 行。</summary>
    public Dictionary<string, List<PrismRow>> PrismByCategory { get; } = new(StringComparer.Ordinal);

    /// <summary>device_id -> GetDevice が返すレコード。</summary>
    public Dictionary<string, DeviceRecord> Devices { get; } = new(StringComparer.Ordinal);

    /// <summary>実際に発行された PATCH の記録。</summary>
    public List<(string DeviceId, IReadOnlyList<string> Tags)> Patches { get; } = [];

    public Func<string, JsonObject, IReadOnlyList<PrismRow>>? PrismOverride { get; set; }

    public Task<IReadOnlyList<PrismRow>> QueryPrismAsync(
        string category, JsonObject filter, CancellationToken ct = default)
    {
        if (PrismOverride is not null)
            return Task.FromResult(PrismOverride(category, filter));

        var rows = PrismByCategory.TryGetValue(category, out var r) ? r : [];
        return Task.FromResult<IReadOnlyList<PrismRow>>(rows);
    }

    public Task<IReadOnlyList<TagInfo>> ListTagsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TagInfo>>(Tags);

    public Task<DeviceRecord> GetDeviceAsync(string deviceId, CancellationToken ct = default)
    {
        var record = Devices.TryGetValue(deviceId, out var d)
            ? d
            : new DeviceRecord { DeviceId = deviceId, Tags = [] };
        return Task.FromResult(record);
    }

    public Task<DeviceRecord> PatchTagsAsync(
        string deviceId, IReadOnlyList<string> tags, CancellationToken ct = default)
    {
        Patches.Add((deviceId, tags));
        return Task.FromResult(new DeviceRecord { DeviceId = deviceId, Tags = tags });
    }
}

/// <summary>指定したハンドラでレスポンスを返す <see cref="HttpMessageHandler"/>。</summary>
public sealed class StubHttpMessageHandler(
    Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(handler(request));
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
}
