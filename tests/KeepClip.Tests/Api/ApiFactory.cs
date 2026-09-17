using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace KeepClip.Tests.Api;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.UseEnvironment("Development");
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory> { }

internal static class HttpJson
{
    public static async Task<JsonNode> Json(this HttpResponseMessage response)
        => JsonNode.Parse(await response.Content.ReadAsStringAsync())
           ?? throw new InvalidOperationException("Pusta odpowiedź JSON.");

    public static async Task<JsonNode> GetJson(this HttpClient http, string url)
    {
        var response = await http.GetAsync(url);
        Assert.True(response.IsSuccessStatusCode, $"GET {url} -> {(int)response.StatusCode}");
        return await response.Json();
    }

    public static Task<HttpResponseMessage> PostJson(this HttpClient http, string url, object body)
        => http.PostAsJsonAsync(url, body);

    public static Task<HttpResponseMessage> PatchJson(this HttpClient http, string url, object body)
        => http.PatchAsJsonAsync(url, body);

    public static Task<HttpResponseMessage> PostEmpty(this HttpClient http, string url)
        => http.PostAsync(url, null);
}
