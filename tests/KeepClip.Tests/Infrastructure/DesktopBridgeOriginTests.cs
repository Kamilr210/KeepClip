namespace KeepClip.Tests.Infrastructure;

public class DesktopBridgeOriginTests
{
    [Theory]
    [InlineData("http://127.0.0.1:8770/", true)]
    [InlineData("http://127.0.0.1:8770/?lang=pl", true)]
    [InlineData("http://127.0.0.1:8771/", false)]
    [InlineData("https://127.0.0.1:8770/", false)]
    [InlineData("http://127.0.0.1.evil.invalid:8770/", false)]
    [InlineData("http://localhost:8770/", false)]
    [InlineData("file:///C:/test.html", false)]
    [InlineData("null", false)]
    [InlineData(null, false)]
    public void Native_bridge_accepts_only_the_application_origin(string? source, bool expected)
        => Assert.Equal(expected, LocalAppOrigin.Matches(source, "http://127.0.0.1:8770"));
}