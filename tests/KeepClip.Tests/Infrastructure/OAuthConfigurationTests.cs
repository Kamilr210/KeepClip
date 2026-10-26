namespace KeepClip.Tests.Infrastructure;

public class OAuthConfigurationTests
{
    [Theory]
    [InlineData("{\"web\":{\"client_id\":\"test-id\",\"client_secret\":\"private-web-secret\"}}")]
    [InlineData("{\"client_id\":\"test-id\",\"client_secret\":\"untyped-secret\"}")]
    public void Confidential_or_untyped_clients_are_not_used_in_the_desktop_flow(string json)
    {
        try
        {
            File.WriteAllText(Config.GoogleClientPath, json);
            Assert.Throws<InvalidOperationException>(() => new OAuthBackend().LoadClientId());
        }
        finally { File.Delete(Config.GoogleClientPath); }
    }

    [Theory]
    [InlineData("{\"installed\":{\"client_id\":\"test-id\",\"client_secret\":\"legacy-value-is-ignored\"}}")]
    [InlineData("{\"installed\":{\"client_id\":\"test-id\"}}")]
    public void Only_the_public_desktop_client_id_is_loaded_even_from_legacy_configuration(string json)
    {
        Assert.StartsWith(TestEnvironment.DataDir, Config.GoogleClientPath);
        Assert.False(File.Exists(Config.GoogleClientPath));
        try
        {
            File.WriteAllText(Config.GoogleClientPath, json);
            var backend = new OAuthBackend();
            Assert.True(backend.IsConfigured);
            Assert.Equal("test-id", backend.LoadClientId());
        }
        finally { File.Delete(Config.GoogleClientPath); }
    }

    [Fact]
    public void Missing_client_identifier_is_reported_instead_of_starting_authentication()
    {
        Assert.StartsWith(TestEnvironment.DataDir, Config.GoogleClientPath);
        Assert.False(File.Exists(Config.GoogleClientPath));
        try
        {
            File.WriteAllText(Config.GoogleClientPath, "{\"installed\":{}}");
            Assert.Throws<InvalidOperationException>(() => new OAuthBackend().LoadClientId());
        }
        finally { File.Delete(Config.GoogleClientPath); }
    }
}