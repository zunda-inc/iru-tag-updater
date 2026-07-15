using System.Text;
using System.Text.Json;

namespace IruTagUpdater.Configuration;

/// <summary>
/// config を読み込む。優先順位:
/// 1. ルートの config.json ファイル (開発時)
/// 2. 環境変数 IRU_CONFIG_BASE64 (Base64 エンコードした config.json / Cloud Run Jobs)
/// 3. どちらも無ければ例外
/// </summary>
public static class ConfigLoader
{
    public const string FileName = "config.json";
    public const string EnvVarName = "IRU_CONFIG_BASE64";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static AppConfig Load(string? baseDirectory = null)
    {
        var directory = baseDirectory ?? Directory.GetCurrentDirectory();
        var json = ReadRawJson(directory);
        var config = Parse(json);
        config.Validate();
        return config;
    }

    private static string ReadRawJson(string directory)
    {
        var filePath = Path.Combine(directory, FileName);
        if (File.Exists(filePath))
        {
            return File.ReadAllText(filePath);
        }

        var base64 = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(base64))
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(base64.Trim()));
            }
            catch (FormatException ex)
            {
                throw new InvalidConfigException($"環境変数 {EnvVarName} が正しい Base64 ではありません: {ex.Message}");
            }
        }

        throw new InvalidConfigException(
            $"config が見つかりません。ルートに {FileName} を置くか、環境変数 {EnvVarName} を設定してください。");
    }

    private static AppConfig Parse(string json)
    {
        AppConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidConfigException($"config の JSON パースに失敗しました: {ex.Message}");
        }

        return config ?? throw new InvalidConfigException("config の JSON が null です。");
    }
}
