namespace KeepClip.Tests.Infrastructure;

public class OAuthConfigurationTests
{
    [Theory]
    [InlineData("{\"installed\":{\"client_id\":\"test-id\",\"client_secret\":\"test-secret\"}}", "test-secret")]
    [InlineData("{\"web\":{\"client_id\":\"test-id\",\"client_secret\":\"test-secret\"}}", "test-secret")]
    [InlineData("{\"client_id\":\"test-id\"}", "")]
    public void Supported_client_json_formats_are_loaded_from_the_isolated_test_directory(string json, string expectedSecret)
    {
        Assert.StartsWith(TestEnvironment.DataDir, Config.GoogleClientPath);
        Assert.False(File.Exists(Config.GoogleClientPath));
        try
        {
            File.WriteAllText(Config.GoogleClientPath, json);
            var backend = new OAuthBackend();
            Assert.True(backend.IsConfigured);
            Assert.Equal(("test-id", expectedSecret), backend.LoadClient());
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
            Assert.Throws<InvalidOperationException>(() => new OAuthBackend().LoadClient());
        }
        finally { File.Delete(Config.GoogleClientPath); }
    }
}