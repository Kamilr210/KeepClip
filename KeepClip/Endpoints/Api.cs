namespace KeepClip.Endpoints;

internal static class Api
{
    public static IResult Detail(int code, string msg, string? error = null)
        => Results.Json(
            new Dictionary<string, object?> { ["detail"] = msg, ["error"] = error },
            statusCode: code);
}
