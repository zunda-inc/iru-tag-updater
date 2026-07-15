using System.Net;
using System.Text.Json.Nodes;
using IruTagUpdater.Kandji;
using Microsoft.Extensions.Logging.Abstractions;

namespace IruTagUpdater.Tests;

public sealed class KandjiClientTests
{
    private static KandjiClient CreateClient(StubHttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://x.api.kandji.io/") };
        return new KandjiClient(http, NullLogger<KandjiClient>.Instance);
    }

    [Fact]
    public async Task QueryPrism_FollowsCursorPagination()
    {
        var page1 = """{ "data": [ { "device_id": "d1", "tags": ["Existing"] } ], "cursor": "CUR1" }""";
        var page2 = """{ "data": [ { "device_id": "d2", "tags": null } ], "cursor": null }""";

        var handler = new StubHttpMessageHandler(req =>
            req.RequestUri!.Query.Contains("cursor=CUR1")
                ? StubHttpMessageHandler.Json(page2)
                : StubHttpMessageHandler.Json(page1));
        var client = CreateClient(handler);

        var filter = new JsonObject { ["bundle_id"] = new JsonObject { ["in"] = new JsonArray("a") } };
        var rows = await client.QueryPrismAsync("apps", filter);

        Assert.Equal(2, rows.Count);
        Assert.Equal("d1", rows[0].DeviceId);
        Assert.Equal("d2", rows[1].DeviceId);
        Assert.Equal(2, handler.Requests.Count);

        // filter が URL エンコードされて渡っていること。
        Assert.Contains("filter=", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("prism/apps", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ListTags_FollowsNextPagination()
    {
        var page1 = """
            { "next": "https://x.api.kandji.io/api/v1/tags?cursor=abc",
              "results": [ { "id": "id1", "name": "T1" } ] }
            """;
        var page2 = """{ "next": null, "results": [ { "id": "id2", "name": "T2" } ] }""";

        var handler = new StubHttpMessageHandler(req =>
            req.RequestUri!.Query.Contains("cursor=abc")
                ? StubHttpMessageHandler.Json(page2)
                : StubHttpMessageHandler.Json(page1));
        var client = CreateClient(handler);

        var tags = await client.ListTagsAsync();

        Assert.Equal(2, tags.Count);
        Assert.Equal("id1", tags[0].Id);
        Assert.Equal("T2", tags[1].Name);
    }

    [Fact]
    public async Task PatchTags_SendsTagsArray()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(req =>
        {
            captured = req;
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return StubHttpMessageHandler.Json("""{ "device_id": "d1", "tags": ["A", "B"] }""");
        });
        var client = CreateClient(handler);

        var result = await client.PatchTagsAsync("d1", ["A", "B"]);

        Assert.Equal(HttpMethod.Patch, captured!.Method);
        Assert.Contains("devices/d1", captured.RequestUri!.AbsolutePath);
        Assert.Contains("\"tags\"", capturedBody);
        Assert.Contains("\"A\"", capturedBody);
        Assert.Equal(2, result.Tags!.Count);
    }

    [Fact]
    public async Task GetAsync_Throws_OnErrorStatus()
    {
        var handler = new StubHttpMessageHandler(_ =>
            StubHttpMessageHandler.Json("""{ "detail": "nope" }""", HttpStatusCode.Forbidden));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<KandjiApiException>(() => client.GetDeviceAsync("d1"));
    }
}
