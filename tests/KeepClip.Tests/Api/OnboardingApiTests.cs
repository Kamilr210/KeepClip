using System.Net;

namespace KeepClip.Tests.Api;

[Collection("api")]
public class OnboardingApiTests
{
    private readonly HttpClient _http;

    public OnboardingApiTests(ApiFactory factory)
    {
        TestSettings.Reset();
        _http = factory.CreateClient();
    }

    [Fact]
    public async Task Fresh_state_means_the_tutorial_will_start()
    {
        var json = await _http.GetJson("/api/onboarding");

        Assert.False((bool)json["started"]!);
        Assert.False((bool)json["completed"]!);
        Assert.False((bool)json["skipped"]!);
        Assert.Null(json["current_step"]);
        Assert.Equal(OnboardingService.CurrentVersion, (int)json["current_version"]!);
        Assert.Empty(json["seen_hints"]!.AsArray());
    }

    [Fact]
    public async Task Progress_completion_and_restart_round_trip()
    {
        var started = await _http.PostJson("/api/onboarding", new { started = true, current_step = "favorites" });
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        Assert.Equal("favorites", (string)(await started.Json())["current_step"]!);

        var completed = await (await _http.PostJson("/api/onboarding", new { completed = true })).Json();
        Assert.True((bool)completed["completed"]!);
        Assert.Null(completed["current_step"]);
        Assert.Equal(OnboardingService.CurrentVersion, (int)completed["completed_version"]!);

        var skipped = await (await _http.PostJson("/api/onboarding", new { completed = false, skipped = true })).Json();
        Assert.True((bool)skipped["skipped"]!);

        var restarted = await (await _http.PostEmpty("/api/onboarding/restart")).Json();
        Assert.True((bool)restarted["started"]!);
        Assert.False((bool)restarted["skipped"]!);
        Assert.Equal(OnboardingService.CurrentVersion, (int)restarted["completed_version"]!);

        var hinted = await (await _http.PostJson("/api/onboarding", new { seen_hint = "share-v1" })).Json();
        Assert.Equal("share-v1", (string)hinted["seen_hints"]![0]!);
        Assert.Single((await _http.GetJson("/api/onboarding"))["seen_hints"]!.AsArray());
    }

    [Fact]
    public async Task Malformed_body_is_rejected_not_crashing()
    {
        var response = await _http.PostAsync("/api/onboarding",
            new StringContent("{ nie json", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False((bool)(await _http.GetJson("/api/onboarding"))["started"]!);
    }
}
