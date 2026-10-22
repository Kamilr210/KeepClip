using System.Net;
using KeepClip.Application;

namespace KeepClip.Endpoints;

public static class ClipDemoEndpoints
{
    public static void MapClipDemoEndpoints(this WebApplication app)
    {
        app.MapGet("/api/clips/{clipId:long}/demo", (long clipId, ClipDemoService demos) => Respond(demos.Get(clipId)))
            .AddEndpointFilter(RequireLocalPage);

        app.MapPut("/api/clips/{clipId:long}/demo", async (long clipId, ClipDemoPayload body, ClipDemoService demos, CancellationToken ct) =>
        {
            Heartbeat.Touch();
            return Respond(await demos.AssignAsync(clipId, body.path, ct));
        }).AddEndpointFilter(RequireLocalPage);

        app.MapDelete("/api/clips/{clipId:long}/demo", (long clipId, ClipDemoService demos) =>
        {
            Heartbeat.Touch();
            return Respond(demos.Remove(clipId));
        }).AddEndpointFilter(RequireLocalPage);
    }

    private static IResult Respond(Result<ClipDemoResponse> result)
        => result.IsSuccess ? Results.Json(result.Value) : Api.Detail(result.Code, result.Error!);

    private static ValueTask<object?> RequireLocalPage(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        var remote = context.HttpContext.Connection.RemoteIpAddress;
        if (remote?.IsIPv4MappedToIPv6 == true) remote = remote.MapToIPv4();
        var host = request.Host.Host;
        bool local = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
        var origin = request.Headers.Origin.ToString();
        if (!local || (remote is not null && !IPAddress.IsLoopback(remote)) || request.Headers["Sec-Fetch-Site"] == "cross-site"
            || (origin.Length > 0 && !LocalAppOrigin.Matches(origin, $"{request.Scheme}://{request.Host}")))
            return ValueTask.FromResult<object?>(Results.StatusCode(StatusCodes.Status403Forbidden));
        return next(context);
    }
}

record ClipDemoPayload(string? path);