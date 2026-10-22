namespace KeepClip.Tests.Infrastructure;

public class CredentialStoreTests
{
    [Theory]
    [InlineData("test-token")]
    [InlineData("Zażółć гęślą 🔒")]
    [InlineData("")]
    public void Credential_round_trip_and_deletion_use_only_an_isolated_test_target(string value)
    {
        var target = "KeepClip.Tests:" + Guid.NewGuid().ToString("N");
        try
        {
            Assert.Null(CredentialStore.Read(target));
            CredentialStore.Write(target, value, "test@example.invalid");
            Assert.Equal(value, CredentialStore.Read(target));
            CredentialStore.Write(target, "replacement");
            Assert.Equal("replacement", CredentialStore.Read(target));
        }
        finally
        {
            CredentialStore.Delete(target);
            Assert.Null(CredentialStore.Read(target));
        }
    }

    [Fact]
    public void Oversized_credentials_are_rejected_before_writing_any_data()
    {
        var target = "KeepClip.Tests:" + Guid.NewGuid().ToString("N");
        Assert.Throws<ArgumentException>(() => CredentialStore.Write(target, new string('a', 1281)));
        Assert.Null(CredentialStore.Read(target));
        CredentialStore.Delete(target);
    }
}