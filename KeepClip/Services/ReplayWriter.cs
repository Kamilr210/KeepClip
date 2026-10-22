using System.Text;

namespace KeepClip.Services;

internal sealed class ReplayWriter(Func<string[], int, Task<(int code, string stderr)>> run, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    internal async Task<Dictionary<string, object?>> SaveAsync(string segmentDirectory, string game, int durationSeconds, bool audio, bool hevc)
    {
        string? listPath = null, snapPath = null, tmpOut = null;
        try
        {
            var files = Directory.GetFiles(segmentDirectory, "seg_*.ts")
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
            if (files.Count == 0)
                throw new InvalidOperationException(Strings.Get("replay.bufferEmpty"));

            var current = files[^1];
            var completed = files.Take(files.Count - 1).ToList();

            int needed = (durationSeconds + ReplayService.SegmentSeconds - 1) / ReplayService.SegmentSeconds;
            var take = completed.Skip(Math.Max(0, completed.Count - needed)).ToList();

            snapPath = Path.Combine(Config.TmpDir, $"replay_part_{Guid.NewGuid():N}.ts");
            try
            {
                using var src = new FileStream(current, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var dst = File.Create(snapPath);
                await src.CopyToAsync(dst);
            }
            catch { snapPath = null; }
            if (snapPath is not null && new FileInfo(snapPath).Length > 50_000)
                take.Add(snapPath);
            if (take.Count == 0)
                throw new InvalidOperationException(Strings.Get("replay.bufferEmpty"));

            listPath = Path.Combine(Config.TmpDir, $"replay_list_{Guid.NewGuid():N}.txt");
            var sb = new StringBuilder();
            foreach (var f in take)
                sb.Append("file '").Append(f.Replace('\\', '/').Replace("'", "'\\''")).Append("'\n");
            await File.WriteAllTextAsync(listPath, sb.ToString());

            var outDir = Path.Combine(Settings.GetClipsRoot(), game);
            Directory.CreateDirectory(outDir);
            foreach (var stale in Directory.GetFiles(outDir, "replay_out_*.part"))
                try { File.Delete(stale); } catch { }
            var stem = $"{game} {_clock.GetLocalNow():yyyy-MM-dd HH-mm-ss}";
            var outPath = AvailableReplayPath(outDir, stem);

            tmpOut = Path.Combine(outDir, $"replay_out_{Guid.NewGuid():N}.mp4.part");
            var args = new List<string>
            {
                "-y", "-f", "concat", "-safe", "0", "-i", listPath,
                "-map", "0:v:0", "-c:v", "copy",
            };
            if (hevc) { args.Add("-tag:v"); args.Add("hvc1"); }
            if (audio)
                args.AddRange(new[] { "-map", "0:a:0?", "-c:a", "aac", "-b:a", "192k",
                                      "-af", "aresample=async=1:first_pts=0,apad", "-shortest" });
            args.AddRange(new[] { "-f", "mp4", tmpOut });
            var (code, err) = await run(args.ToArray(), 120_000);
            if (code != 0 || !File.Exists(tmpOut) || new FileInfo(tmpOut).Length < 10_000)
                throw new Exception(Strings.Get("replay.muxFailed", ReplayService.Tail(err)));

            for (int attempt = 0; ; attempt++)
            {
                try { File.Move(tmpOut, outPath); break; }
                catch (IOException) when (File.Exists(outPath) && attempt < 5) { outPath = AvailableReplayPath(outDir, stem); }
                catch (IOException) when (attempt < 5) { await Task.Delay(400); }
            }
            tmpOut = null;

            return new Dictionary<string, object?>
            {
                ["ok"] = true, ["file"] = Path.GetFileName(outPath), ["game"] = game, ["path"] = outPath,
            };
        }
        finally
        {
            foreach (var p in new[] { listPath, snapPath, tmpOut })
                try { if (p is not null && File.Exists(p)) File.Delete(p); } catch { }
        }
    }

    private static string AvailableReplayPath(string directory, string stem)
    {
        var path = Path.Combine(directory, stem + ".mp4");
        for (int copy = 2; File.Exists(path); copy++)
            path = Path.Combine(directory, $"{stem} ({copy}).mp4");
        return path;
    }
}