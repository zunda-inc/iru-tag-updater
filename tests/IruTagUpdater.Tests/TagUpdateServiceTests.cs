using System.Text.Json.Nodes;
using IruTagUpdater;
using IruTagUpdater.Configuration;
using IruTagUpdater.Kandji;
using Microsoft.Extensions.Logging.Abstractions;

namespace IruTagUpdater.Tests;

public sealed class TagUpdateServiceTests
{
    private const string TagName = "Iru Installed";

    private static AppConfig ConfigWithConditions(params string[] queries)
    {
        var conditions = queries
            .Select(q => new Condition { Query = q, Filter = new JsonObject() })
            .ToList();
        return new AppConfig
        {
            Endpoint = "https://x.api.kandji.io",
            Updates = [new TagUpdate { Tag = TagName, Conditions = conditions }],
        };
    }

    private static IReadOnlyDictionary<string, ResolvedTag> Resolved()
        => new Dictionary<string, ResolvedTag> { [TagName] = new(TagName, "tag-id-1") };

    private static PrismRow Row(string deviceId, params string[] tags)
        => new() { DeviceId = deviceId, Tags = tags.Length == 0 ? null : tags };

    private static TagUpdateService CreateService(FakeKandjiClient client)
        => new(client, NullLogger<TagUpdateService>.Instance);

    [Fact]
    public async Task Run_TagsOnlyDevicesMatchingAllConditions_Intersection()
    {
        var client = new FakeKandjiClient();
        // d1: apps + certs (両方) / d2: apps のみ / d3: certs のみ
        client.PrismByCategory["apps"] = [Row("d1"), Row("d2")];
        client.PrismByCategory["certificates"] = [Row("d1"), Row("d3")];

        var results = await CreateService(client).RunAsync(
            ConfigWithConditions("apps", "certificates"), Resolved(), dryRun: false);

        Assert.Equal(1, results[0].MatchedDevices);
        Assert.Equal(1, results[0].Tagged);
        Assert.Single(client.Patches);
        Assert.Equal("d1", client.Patches[0].DeviceId);
    }

    [Fact]
    public async Task Run_SkipsAlreadyTaggedDevices()
    {
        var client = new FakeKandjiClient();
        // d1 は既に対象タグ付き / d2 は未付与
        client.PrismByCategory["apps"] = [Row("d1", TagName), Row("d2")];

        var results = await CreateService(client).RunAsync(
            ConfigWithConditions("apps"), Resolved(), dryRun: false);

        Assert.Equal(2, results[0].MatchedDevices);
        Assert.Equal(1, results[0].AlreadyTagged);
        Assert.Equal(1, results[0].Tagged);
        Assert.Single(client.Patches);
        Assert.Equal("d2", client.Patches[0].DeviceId);
    }

    [Fact]
    public async Task Run_PreservesExistingTags_UnionOnPatch()
    {
        var client = new FakeKandjiClient();
        client.PrismByCategory["apps"] = [Row("d1")];
        // GetDevice 時点で他タグが付いている。
        client.Devices["d1"] = new DeviceRecord { DeviceId = "d1", Tags = ["Existing", "Other"] };

        await CreateService(client).RunAsync(
            ConfigWithConditions("apps"), Resolved(), dryRun: false);

        var patched = Assert.Single(client.Patches);
        Assert.Contains("Existing", patched.Tags);
        Assert.Contains("Other", patched.Tags);
        Assert.Contains(TagName, patched.Tags);
        Assert.Equal(3, patched.Tags.Count);
    }

    [Fact]
    public async Task Run_DryRun_DoesNotPatch()
    {
        var client = new FakeKandjiClient();
        client.PrismByCategory["apps"] = [Row("d1"), Row("d2")];

        var results = await CreateService(client).RunAsync(
            ConfigWithConditions("apps"), Resolved(), dryRun: true);

        Assert.Equal(2, results[0].Tagged);
        Assert.Empty(client.Patches);
    }

    [Fact]
    public async Task Run_NoMatches_WhenAConditionIsEmpty()
    {
        var client = new FakeKandjiClient();
        client.PrismByCategory["apps"] = [Row("d1")];
        client.PrismByCategory["certificates"] = []; // 空 -> 積集合は空

        var results = await CreateService(client).RunAsync(
            ConfigWithConditions("apps", "certificates"), Resolved(), dryRun: false);

        Assert.Equal(0, results[0].MatchedDevices);
        Assert.Empty(client.Patches);
    }

    [Fact]
    public async Task Run_SkipsPatch_WhenDeviceGainedTagBetweenQueryAndPatch()
    {
        var client = new FakeKandjiClient();
        client.PrismByCategory["apps"] = [Row("d1")]; // Prism 上は未付与
        // GetDevice では既に付与済み (競合) -> PATCH しない
        client.Devices["d1"] = new DeviceRecord { DeviceId = "d1", Tags = [TagName] };

        var results = await CreateService(client).RunAsync(
            ConfigWithConditions("apps"), Resolved(), dryRun: false);

        Assert.Empty(client.Patches);
        // PATCH していないので Tagged は 0、付与済みスキップとして計上される。
        Assert.Equal(0, results[0].Tagged);
        Assert.Equal(1, results[0].AlreadyTagged);
    }
}
