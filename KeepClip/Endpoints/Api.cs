namespace KeepClip.Endpoints;

internal static class Api
{
    // `error` pozwala interfejsowi pokazać komunikat w języku użytkownika; `detail`
    // zostaje jako zapasowy tekst dla błędów, które nie mają jeszcze własnego kodu.
    public static IResult Detail(int code, string msg, string? error = null)
        => Results.Json(
            new Dictionary<string, object?> { ["detail"] = msg, ["error"] = error },
            statusCode: code);
}
