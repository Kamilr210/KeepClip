namespace KeepClip.Endpoints;

/// <summary>Small shared helpers for the endpoint groups.</summary>
internal static class Api
{
    /// <summary>FastAPI's HTTPException shape: <c>{"detail": "..."}</c> with the given status.</summary>
    public static IResult Detail(int code, string msg)
        => Results.Json(new Dictionary<string, object?> { ["detail"] = msg }, statusCode: code);
}
