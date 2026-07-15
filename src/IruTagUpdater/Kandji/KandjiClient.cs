using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace IruTagUpdater.Kandji;

/// <summary>
/// Iru (旧 Kandji) API クライアント。<see cref="HttpClient"/> の BaseAddress と
/// Authorization ヘッダは DI 側で設定する想定。
/// </summary>
public sealed class KandjiClient(HttpClient http, ILogger<KandjiClient> logger) : IKandjiClient
{
    private const int PrismPageLimit = 300;
    // カーソル/ページの暴走防止 (300 * 5000 = 150 万行相当で十分な上限)。
    private const int MaxPages = 5000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<IReadOnlyList<PrismRow>> QueryPrismAsync(
        string category, JsonObject filter, CancellationToken ct = default)
    {
        var filterJson = filter.ToJsonString();
        var rows = new List<PrismRow>();
        string? cursor = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var url = $"api/v1/prism/{Uri.EscapeDataString(category)}"
                    + $"?filter={Uri.EscapeDataString(filterJson)}"
                    + $"&limit={PrismPageLimit}";
            if (!string.IsNullOrEmpty(cursor))
            {
                url += $"&cursor={Uri.EscapeDataString(cursor)}";
            }

            var response = await GetAsync<PrismResponse>(url, ct);
            if (response.Data.Count > 0)
            {
                rows.AddRange(response.Data);
            }

            if (string.IsNullOrEmpty(response.Cursor) || response.Data.Count == 0)
            {
                return rows;
            }

            cursor = response.Cursor;
        }

        throw new KandjiApiException(
            $"Prism '{category}' のページネーションが上限 ({MaxPages}) を超えました。");
    }

    public async Task<IReadOnlyList<TagInfo>> ListTagsAsync(CancellationToken ct = default)
    {
        var tags = new List<TagInfo>();
        var url = "api/v1/tags";

        for (var page = 0; page < MaxPages; page++)
        {
            var response = await GetAsync<TagListResponse>(url, ct);
            tags.AddRange(response.Results);

            if (string.IsNullOrEmpty(response.Next))
            {
                return tags;
            }

            // next は絶対 URL。BaseAddress と同一ホストなので相対に落として使う。
            url = ToRelativeUrl(response.Next);
        }

        throw new KandjiApiException($"タグ一覧のページネーションが上限 ({MaxPages}) を超えました。");
    }

    public Task<DeviceRecord> GetDeviceAsync(string deviceId, CancellationToken ct = default)
        => GetAsync<DeviceRecord>($"api/v1/devices/{Uri.EscapeDataString(deviceId)}", ct);

    public async Task<DeviceRecord> PatchTagsAsync(
        string deviceId, IReadOnlyList<string> tags, CancellationToken ct = default)
    {
        var url = $"api/v1/devices/{Uri.EscapeDataString(deviceId)}";
        var payload = new { tags };

        using var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = JsonContent.Create(payload),
        };
        using var response = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, HttpMethod.Patch.Method, url, ct);

        var record = await response.Content.ReadFromJsonAsync<DeviceRecord>(JsonOptions, ct);
        return record ?? throw new KandjiApiException($"PATCH {url} のレスポンスが空でした。");
    }

    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        await EnsureSuccessAsync(response, HttpMethod.Get.Method, url, ct);

        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        return value ?? throw new KandjiApiException($"GET {url} のレスポンスが空でした。");
    }

    private async Task EnsureSuccessAsync(
        HttpResponseMessage response, string method, string url, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await SafeReadBodyAsync(response, ct);
        logger.LogError("{Method} {Url} が失敗しました: {Status} {Body}",
            method, url, (int)response.StatusCode, body);
        throw new KandjiApiException(
            $"{method} {url} が失敗しました: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            return string.Empty;
        }
    }

    private string ToRelativeUrl(string absoluteOrRelative)
    {
        if (Uri.TryCreate(absoluteOrRelative, UriKind.Absolute, out var abs))
        {
            return abs.PathAndQuery.TrimStart('/');
        }
        return absoluteOrRelative.TrimStart('/');
    }
}
