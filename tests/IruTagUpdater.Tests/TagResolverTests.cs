using IruTagUpdater;
using IruTagUpdater.Configuration;
using IruTagUpdater.Kandji;
using Microsoft.Extensions.Logging.Abstractions;

namespace IruTagUpdater.Tests;

public sealed class TagResolverTests
{
    private static TagResolver CreateResolver(FakeKandjiClient client)
        => new(client, NullLogger<TagResolver>.Instance);

    [Fact]
    public async Task Resolve_MapsNamesToIds()
    {
        var client = new FakeKandjiClient();
        client.Tags.Add(new TagInfo { Id = "id-a", Name = "Alpha" });
        client.Tags.Add(new TagInfo { Id = "id-b", Name = "Beta" });

        var resolved = await CreateResolver(client).ResolveAsync(["Alpha", "Beta"]);

        Assert.Equal("id-a", resolved["Alpha"].Id);
        Assert.Equal("id-b", resolved["Beta"].Id);
    }

    [Fact]
    public async Task Resolve_Throws_WhenTagMissing()
    {
        var client = new FakeKandjiClient();
        client.Tags.Add(new TagInfo { Id = "id-a", Name = "Alpha" });

        var ex = await Assert.ThrowsAsync<InvalidConfigException>(
            () => CreateResolver(client).ResolveAsync(["Alpha", "Missing"]));
        Assert.Contains("Missing", ex.Message);
    }

    [Fact]
    public async Task Resolve_Throws_WhenTagNameAmbiguous()
    {
        var client = new FakeKandjiClient();
        client.Tags.Add(new TagInfo { Id = "id-1", Name = "Dup" });
        client.Tags.Add(new TagInfo { Id = "id-2", Name = "Dup" });

        await Assert.ThrowsAsync<InvalidConfigException>(
            () => CreateResolver(client).ResolveAsync(["Dup"]));
    }
}
