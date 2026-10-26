using Microsoft.Web.WebView2.Core;

namespace KeepClip.Infrastructure;

internal sealed class DesktopRequestAuthentication(string baseUrl, string credential)
{
    internal string? AuthorizationFor(string uri) => LocalAppOrigin.Matches(uri, baseUrl) ? "Bearer " + credential : null;

    internal void Attach(CoreWebView2 core)
    {
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += (_, args) =>
        {
            var authorization = AuthorizationFor(args.Request.Uri);
            if (authorization is not null) args.Request.Headers.SetHeader("Authorization", authorization);
            else if (args.Request.Headers.Contains("Authorization") && args.Request.Headers.GetHeader("Authorization") == "Bearer " + credential)
                args.Request.Headers.RemoveHeader("Authorization");
        };
        core.NavigationStarting += (_, args) =>
        {
            if (!LocalAppOrigin.Matches(args.Uri, baseUrl)) args.Cancel = true;
        };
    }
}