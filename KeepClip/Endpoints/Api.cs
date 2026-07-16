namespace KeepClip.Endpoints;

internal static class Api
{
    public static IResult Detail(int code, string msg)
        => Results.Json(new Dictionary<string, object?> { ["detail"] = msg }, statusCode: code);
}
