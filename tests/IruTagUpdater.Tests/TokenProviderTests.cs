using IruTagUpdater.Configuration;

namespace IruTagUpdater.Tests;

public sealed class TokenProviderTests : IDisposable
{
    private readonly string _dir;
    private readonly string? _originalEnv;

    public TokenProviderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "iru-tok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _originalEnv = Environment.GetEnvironmentVariable(TokenProvider.EnvVarName);
        Environment.SetEnvironmentVariable(TokenProvider.EnvVarName, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(TokenProvider.EnvVarName, _originalEnv);
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void GetToken_ReadsFromEnvFile_WhenPresent()
    {
        File.WriteAllText(Path.Combine(_dir, TokenProvider.EnvFileName), "IRU_API_TOKEN=from-dotenv\n");

        var token = TokenProvider.GetToken(_dir);

        Assert.Equal("from-dotenv", token);
    }

    [Fact]
    public void GetToken_FallsBackToEnvVar_WhenNoEnvFile()
    {
        Environment.SetEnvironmentVariable(TokenProvider.EnvVarName, "from-env");

        var token = TokenProvider.GetToken(_dir);

        Assert.Equal("from-env", token);
    }

    [Fact]
    public void GetToken_PrefersEnvFileOverEnvVar()
    {
        Environment.SetEnvironmentVariable(TokenProvider.EnvVarName, "from-env");
        File.WriteAllText(Path.Combine(_dir, TokenProvider.EnvFileName), "IRU_API_TOKEN=from-dotenv\n");

        var token = TokenProvider.GetToken(_dir);

        Assert.Equal("from-dotenv", token);
    }

    [Fact]
    public void GetToken_Throws_WhenNeitherPresent()
    {
        var ex = Assert.Throws<InvalidConfigException>(() => TokenProvider.GetToken(_dir));
        Assert.Contains(TokenProvider.EnvVarName, ex.Message);
    }
}
