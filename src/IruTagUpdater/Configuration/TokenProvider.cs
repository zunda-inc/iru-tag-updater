namespace IruTagUpdater.Configuration;

/// <summary>
/// Iru API トークンを取得する。優先順位:
/// 1. ルートの .env ファイル内の IRU_API_TOKEN (開発時)
/// 2. 環境変数 IRU_API_TOKEN (Secret Manager -> Cloud Run Jobs)
/// 3. どちらも無ければ例外
/// </summary>
public static class TokenProvider
{
    public const string EnvFileName = ".env";
    public const string EnvVarName = "IRU_API_TOKEN";

    public static string GetToken(string? baseDirectory = null)
    {
        var directory = baseDirectory ?? Directory.GetCurrentDirectory();

        // .env があれば読み込んで環境変数へ流し込む (存在する値は上書きされ、ファイル優先になる)。
        var envFilePath = Path.Combine(directory, EnvFileName);
        if (File.Exists(envFilePath))
        {
            DotNetEnv.Env.Load(envFilePath);
        }

        var token = Environment.GetEnvironmentVariable(EnvVarName);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidConfigException(
                $"API トークンが見つかりません。ルートの {EnvFileName} に {EnvVarName} を書くか、環境変数 {EnvVarName} を設定してください。");
        }

        return token.Trim();
    }
}
