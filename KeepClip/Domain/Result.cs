namespace KeepClip.Domain;

/// <summary>
/// A service-layer outcome that carries an HTTP-shaped failure without the service
/// knowing about HttpContext: success holds a <typeparamref name="T"/>, failure holds a
/// status code + message. Endpoints translate it to <c>Results.Json</c> / problem details.
/// </summary>
public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public int Code { get; }
    public string? Error { get; }

    private Result(bool ok, T? value, int code, string? error)
    {
        IsSuccess = ok;
        Value = value;
        Code = code;
        Error = error;
    }

    public static Result<T> Ok(T value) => new(true, value, 0, null);
    public static Result<T> Fail(int code, string error) => new(false, default, code, error);
}
