using System.Net.Http.Headers;
using IruTagUpdater;
using IruTagUpdater.Configuration;
using IruTagUpdater.Kandji;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

const int ExitSuccess = 0;
const int ExitConfigError = 1;
const int ExitRuntimeError = 2;

// --- 設定・トークンの読み込み (失敗したら例外 -> 非 0 終了) ---
AppConfig config;
string token;
try
{
    config = ConfigLoader.Load();
    token = TokenProvider.GetToken();
}
catch (InvalidConfigException ex)
{
    Console.Error.WriteLine($"[設定エラー] {ex.Message}");
    return ExitConfigError;
}

var dryRun = IsTruthy(Environment.GetEnvironmentVariable("DRY_RUN"));

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    o.UseUtcTimestamp = true;
});

builder.Services.AddSingleton(config);

builder.Services.AddHttpClient<IKandjiClient, KandjiClient>(http =>
{
    http.BaseAddress = new Uri(EnsureTrailingSlash(config.Endpoint));
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    http.Timeout = TimeSpan.FromMinutes(2);
})
.AddStandardResilienceHandler(); // 429/5xx/タイムアウトへの指数バックオフ・リトライ。

builder.Services.AddSingleton<TagResolver>();
builder.Services.AddSingleton<TagUpdateService>();

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

try
{
    logger.LogInformation("Iru タグアップデーターを開始します。endpoint={Endpoint} dryRun={DryRun}",
        config.Endpoint, dryRun);

    var resolver = host.Services.GetRequiredService<TagResolver>();
    var service = host.Services.GetRequiredService<TagUpdateService>();

    var tagNames = config.Updates.Select(u => u.Tag);
    var resolvedTags = await resolver.ResolveAsync(tagNames);

    var results = await service.RunAsync(config, resolvedTags, dryRun);

    var totalTagged = results.Sum(r => r.Tagged);
    foreach (var r in results)
    {
        logger.LogInformation(
            "結果: タグ='{Tag}' 合致={Matched} 付与済み={Already} 付与={Tagged}",
            r.Tag, r.MatchedDevices, r.AlreadyTagged, r.Tagged);
    }
    logger.LogInformation("完了しました。合計付与 {Total} 台{DryRunNote}。",
        totalTagged, dryRun ? " (DRY_RUN のため実際には未実行)" : string.Empty);

    return ExitSuccess;
}
catch (InvalidConfigException ex)
{
    logger.LogError("[設定エラー] {Message}", ex.Message);
    return ExitConfigError;
}
catch (Exception ex)
{
    logger.LogError(ex, "[実行時エラー] 処理に失敗しました。");
    return ExitRuntimeError;
}

static string EnsureTrailingSlash(string url)
    => url.EndsWith('/') ? url : url + "/";

static bool IsTruthy(string? value)
    => value is not null
       && (value.Equals("true", StringComparison.OrdinalIgnoreCase)
           || value == "1"
           || value.Equals("yes", StringComparison.OrdinalIgnoreCase));
