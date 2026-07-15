using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace IruTagUpdater.Configuration;

/// <summary>
/// config.json のルート。デプロイ時は Base64 の環境変数、開発時はルートの config.json から読む。
/// </summary>
public sealed class AppConfig
{
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; init; } = string.Empty;

    [JsonPropertyName("updates")]
    public IReadOnlyList<TagUpdate> Updates { get; init; } = [];

    /// <summary>設定内容の妥当性を検証する。不正なら <see cref="InvalidConfigException"/> を投げる。</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Endpoint))
            throw new InvalidConfigException("config: 'endpoint' が空です。");

        if (Updates.Count == 0)
            throw new InvalidConfigException("config: 'updates' が空です。");

        for (var i = 0; i < Updates.Count; i++)
        {
            var u = Updates[i];
            if (string.IsNullOrWhiteSpace(u.Tag))
                throw new InvalidConfigException($"config: updates[{i}].tag が空です。");

            if (u.Conditions.Count == 0)
                throw new InvalidConfigException($"config: updates[{i}].conditions が空です。");

            for (var j = 0; j < u.Conditions.Count; j++)
            {
                var c = u.Conditions[j];
                if (string.IsNullOrWhiteSpace(c.Query))
                    throw new InvalidConfigException($"config: updates[{i}].conditions[{j}].query が空です。");
                if (c.Filter is null)
                    throw new InvalidConfigException($"config: updates[{i}].conditions[{j}].filter が空です。");
            }
        }
    }
}

/// <summary>1 つのタグとその付与条件。</summary>
public sealed class TagUpdate
{
    [JsonPropertyName("tag")]
    public string Tag { get; init; } = string.Empty;

    [JsonPropertyName("conditions")]
    public IReadOnlyList<Condition> Conditions { get; init; } = [];
}

/// <summary>1 つの Prism クエリ。<see cref="Filter"/> は Prism にそのまま渡す不透明な JSON。</summary>
public sealed class Condition
{
    [JsonPropertyName("query")]
    public string Query { get; init; } = string.Empty;

    [JsonPropertyName("filter")]
    public JsonObject? Filter { get; init; }
}
