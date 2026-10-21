using System.Net;

namespace KeepClip.Tests.Api;

[Collection("api")]
public class ResourceApiTests
{
    private readonly HttpClient _http;

    public ResourceApiTests(ApiFactory factory) => _http = factory.CreateClient();

    [Fact]
    public async Task Resources_are_dev_only_and_report_the_host_process_with_consistent_totals()
    {
        var config = await _http.GetJson("/api/config");
        var response = await _http.GetAsync("/api/dev/resources");
        if (!(bool)config["dev"]!)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Null(typeof(Settings).Assembly.GetType("KeepClip.Infrastructure.DevResourceMonitor"));
            return;
        }
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.Json();
        var rows = data["processes"]!.AsArray();
        Assert.NotEmpty(rows);
        var host = Assert.Single(rows, row => (int)row!["pid"]! == Environment.ProcessId);
        if (host!["usage"]!["ram_bytes"] is { } ram) Assert.True((long)ram > 0);
        Assert.Equal(rows.Count, rows.Select(row => (int)row!["pid"]!).Distinct().Count());
        if (rows.All(row => row!["usage"]!["ram_bytes"] is not null))
            Assert.Equal(rows.Sum(row => (long)row!["usage"]!["ram_bytes"]!), (long)data["total"]!["ram_bytes"]!);
        foreach (var row in rows)
        {
            if (row!["usage"]!["cpu_percent"] is { } cpu) Assert.InRange((double)cpu, 0, 100);
            if (row["usage"]!["gpu_percent"] is { } gpu) Assert.InRange((double)gpu, 0, 100);
        }
        await Task.Delay(650);
        var second = await _http.GetJson("/api/dev/resources");
        Assert.False((bool)second["warming_up"]!);
        Assert.True((double)second["sample_seconds"]! >= 0.5);
        Assert.NotNull(second["total"]!["cpu_percent"]);
    }
}
