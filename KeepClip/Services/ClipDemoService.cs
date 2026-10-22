using KeepClip.Application;

namespace KeepClip.Services;

public sealed class ClipDemoService(ClipRepository clips)
{
    public Result<ClipDemoResponse> Get(long clipId)
    {
        var clip = clips.GetRow(clipId);
        return clip is null
            ? Result<ClipDemoResponse>.Fail(404, Strings.Get("clip.notFound"))
            : Result<ClipDemoResponse>.Ok(Describe(clipId, clip.GetValueOrDefault("demo_path") as string));
    }

    public async Task<Result<ClipDemoResponse>> AssignAsync(long clipId, string? path, CancellationToken ct)
    {
        if (clips.GetById(clipId) is null) return Result<ClipDemoResponse>.Fail(404, Strings.Get("clip.notFound"));
        var fullPath = NormalizeLocalPath(path);
        if (fullPath is null) return Result<ClipDemoResponse>.Fail(400, Strings.Get("demo.invalidPath"));
        if (!Path.GetExtension(fullPath).Equals(".dem", StringComparison.OrdinalIgnoreCase))
            return Result<ClipDemoResponse>.Fail(400, Strings.Get("demo.invalidExtension"));
        if (!File.Exists(fullPath)) return Result<ClipDemoResponse>.Fail(404, Strings.Get("demo.fileMissing"));

        try
        {
            await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
            if (stream.Length < 16) return Result<ClipDemoResponse>.Fail(422, Strings.Get("demo.invalidHeader"));
            var header = new byte[8];
            await stream.ReadExactlyAsync(header, ct);
            if (!header.AsSpan().SequenceEqual("PBDEMS2\0"u8))
                return Result<ClipDemoResponse>.Fail(422, Strings.Get("demo.invalidHeader"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<ClipDemoResponse>.Fail(422, Strings.Get("demo.unreadable"));
        }

        ct.ThrowIfCancellationRequested();
        return clips.SetDemoPath(clipId, fullPath)
            ? Result<ClipDemoResponse>.Ok(Describe(clipId, fullPath))
            : Result<ClipDemoResponse>.Fail(404, Strings.Get("clip.notFound"));
    }

    public Result<ClipDemoResponse> Remove(long clipId)
        => clips.SetDemoPath(clipId, null)
            ? Result<ClipDemoResponse>.Ok(Describe(clipId, null))
            : Result<ClipDemoResponse>.Fail(404, Strings.Get("clip.notFound"));

    internal static string? NormalizeLocalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 32767) return null;
        path = path.Trim();
        if (path.Length > 1 && path[0] == '"' && path[^1] == '"') path = path[1..^1];
        if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] is not ('\\' or '/')) return null;
        if (path.AsSpan(2).Contains(':') || path.Any(char.IsControl) || path.IndexOfAny(['"', '<', '>', '|', '*', '?']) >= 0) return null;
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    private static ClipDemoResponse Describe(long clipId, string? path)
    {
        long? size = null;
        if (path is not null)
        {
            try
            {
                var file = new FileInfo(path);
                if (file.Exists) size = file.Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        }
        return new(clipId, path, path is null ? null : Path.GetFileName(path), size.HasValue, size);
    }
}