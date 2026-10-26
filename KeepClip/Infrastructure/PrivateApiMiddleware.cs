using System.Security.Claims;

namespace KeepClip.Infrastructure;

internal sealed class PrivateApiMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, PrivateApiSession session)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers.XFrameOptions = "DENY";
        context.Response.Headers.ContentSecurityPolicy = "frame-ancestors 'none'; object-src 'none'; base-uri 'self'";
        if (!session.AllowsRequest(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        if (!session.Authenticate(context.Request.Headers.Authorization.ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = session.ServerOnly ? "Basic realm=\"KeepClip\", charset=\"UTF-8\"" : "Bearer realm=\"KeepClip\"";
            return;
        }
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "owner") }, "KeepClipSession"));
        await next(context);
    }
}