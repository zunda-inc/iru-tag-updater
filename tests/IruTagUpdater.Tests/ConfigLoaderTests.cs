using System.Text;
using IruTagUpdater.Configuration;

namespace IruTagUpdater.Tests;

public sealed class ConfigLoaderTests : IDisposable
{
    private readonly string _dir;
    private readonly string? _originalEnv;

    public ConfigLoaderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "iru-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _originalEnv = Environment.GetEnvironmentVariable(ConfigLoader.EnvVarName);
        Environment.SetEnvironmentVariable(ConfigLoader.EnvVarName, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ConfigLoader.EnvVarName, _originalEnv);
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    private const string ValidJson = """
        {
          "endpoint": "https://x.api.kandji.io",
          "updates": [
            { "tag": "T1", "conditions": [ { "query": "apps", "filter": { "bundle_id": { "in": ["a"] } } } ] }
          ]
        }
        """;

    [Fact]
    public void Load_ReadsConfigJsonFile_WhenPresent()
    {
        File.WriteAllText(Path.Combine(_dir, ConfigLoader.FileName), ValidJson);

        var config = ConfigLoader.Load(_dir);

        Assert.Equal("https://x.api.kandji.io", config.Endpoint);
        Assert.Single(config.Updates);
        Assert.Equal("T1", config.Updates[0].Tag);
        Assert.Equal("apps", config.Updates[0].Conditions[0].Query);
        Assert.NotNull(config.Updates[0].Conditions[0].Filter);
    }

    [Fact]
    public void Load_FallsBackToBase64EnvVar_WhenNoFile()
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(ValidJson));
        Environment.SetEnvironmentVariable(ConfigLoader.EnvVarName, base64);

        var config = ConfigLoader.Load(_dir);

        Assert.Equal("https://x.api.kandji.io", config.Endpoint);
    }

    [Fact]
    public void Load_PrefersFileOverEnvVar()
    {
        File.WriteAllText(Path.Combine(_dir, ConfigLoader.FileName), ValidJson);
        var otherJson = ValidJson.Replace("https://x.api.kandji.io", "https://from-env.api.kandji.io");
        Environment.SetEnvironmentVariable(ConfigLoader.EnvVarName,
            Convert.ToBase64String(Encoding.UTF8.GetBytes(otherJson)));

        var config = ConfigLoader.Load(_dir);

        Assert.Equal("https://x.api.kandji.io", config.Endpoint);
    }

    [Fact]
    public void Load_Throws_WhenNeitherFileNorEnvVar()
    {
        var ex = Assert.Throws<InvalidConfigException>(() => ConfigLoader.Load(_dir));
        Assert.Contains(ConfigLoader.EnvVarName, ex.Message);
    }

    [Fact]
    public void Load_Throws_OnInvalidBase64()
    {
        Environment.SetEnvironmentVariable(ConfigLoader.EnvVarName, "not-valid-base64!!!");
        Assert.Throws<InvalidConfigException>(() => ConfigLoader.Load(_dir));
    }

    [Fact]
    public void Load_Throws_WhenEndpointMissing()
    {
        File.WriteAllText(Path.Combine(_dir, ConfigLoader.FileName),
            """{ "updates": [ { "tag": "T1", "conditions": [ { "query": "apps", "filter": {} } ] } ] }""");
        Assert.Throws<InvalidConfigException>(() => ConfigLoader.Load(_dir));
    }

    [Fact]
    public void Load_Throws_WhenUpdatesEmpty()
    {
        File.WriteAllText(Path.Combine(_dir, ConfigLoader.FileName),
            """{ "endpoint": "https://x.api.kandji.io", "updates": [] }""");
        Assert.Throws<InvalidConfigException>(() => ConfigLoader.Load(_dir));
    }
}
