using System.Text.Json.Nodes;

namespace IruTagUpdater.Kandji;

public interface IKandjiClient
{
    /// <summary>Prism カテゴリを filter で検索し、全ページの行を返す。</summary>
    Task<IReadOnlyList<PrismRow>> QueryPrismAsync(string category, JsonObject filter, CancellationToken ct = default);

    /// <summary>タグ一覧を全ページ取得する。</summary>
    Task<IReadOnlyList<TagInfo>> ListTagsAsync(CancellationToken ct = default);

    /// <summary>デバイス 1 件を取得する (最新のタグ確認用)。</summary>
    Task<DeviceRecord> GetDeviceAsync(string deviceId, CancellationToken ct = default);

    /// <summary>デバイスのタグを指定した配列で置き換える。</summary>
    Task<DeviceRecord> PatchTagsAsync(string deviceId, IReadOnlyList<string> tags, CancellationToken ct = default);
}
