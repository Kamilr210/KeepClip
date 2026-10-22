namespace KeepClip.Infrastructure;

public static class OAuthService
{
    private static readonly OAuthSession Session = new(new OAuthBackend());
    public static bool IsConfigured() => Session.IsConfigured();
    public static bool IsConnected() => Session.IsConnected();
    public static Task<Dictionary<string, object?>> StatusAsync() => Session.StatusAsync();
    internal static Task<string> GetAccessTokenAsync() => Session.GetAccessTokenAsync();
    public static Task<GoogleDrive.ProfilePhoto?> GetProfilePhotoAsync() => Session.GetProfilePhotoAsync();
    internal static void InvalidateAbout() => Session.InvalidateAbout();
    public static string BeginConnect() => Session.BeginConnect();
    public static Task DisconnectAsync() => Session.DisconnectAsync();
}
