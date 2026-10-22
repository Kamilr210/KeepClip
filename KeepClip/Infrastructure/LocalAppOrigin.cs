namespace KeepClip.Infrastructure;

internal static class LocalAppOrigin
{
    internal static bool Matches(string? source, string baseUrl)
        => Uri.TryCreate(source, UriKind.Absolute, out var actual) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var expected)
           && actual.Scheme == expected.Scheme && actual.Host.Equals(expected.Host, StringComparison.OrdinalIgnoreCase) && actual.Port == expected.Port;
}