using System.Globalization;

namespace KeepClip;

/// <summary>
/// Background batch transcription — a port of app.py's <c>_transcribe_worker</c> plus
/// the start/cancel glue around it. A single run drains clips needing transcription
/// (all of them with <c>force</c>, otherwise only <c>transcribed_at IS NULL</c>),
/// updating <see cref="TranscribeState"/> so /status and the SSE stream can follow
/// along. Never more than one run at a time.
/// </summary>
public static class TranscribeWorker
{
    private static readonly object StartLock = new();
    private static Task? _task;

    /// <summary>UTC timestamp matching Python's <c>datetime.now(utc).isoformat(timespec="seconds")</c>
    /// (e.g. <c>2026-06-02T14:30:45+00:00</c>) — used for clips.transcribed_at and finished_at.</summary>
    public static string NowIso() =>
        DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>
    /// Begin a run if none is active. Returns false if one is already running (the
    /// caller maps that to <c>{started:false, reason:"already_running"}</c>). Flags are
    /// flipped synchronously so /status reflects "running" the instant this returns.
    /// The work list is computed here too (not inside the background task), so
    /// <paramref name="total"/> reports the exact clip count the moment this returns —
    /// letting the caller answer "no new clips" without waiting on / attaching to the
    /// stream (which would otherwise flash an empty "Zakończono 0/0" bar).
    /// </summary>
    public static bool Start(bool force, out int total)
    {
        total = 0;
        lock (StartLock)
        {
            lock (TranscribeState.Lock)
            {
                if (TranscribeState.Running) return false;
                TranscribeState.Running = true;
                TranscribeState.Cancel = false;
                TranscribeState.Error = null;
                TranscribeState.FinishedAt = null;
                TranscribeState.Total = 0;
                TranscribeState.Done = 0;
                TranscribeState.Current = null;
            }

            // Resolve the clip list synchronously (all clips with force, otherwise only
            // those not yet transcribed) so the count is known before we return.
            List<Dictionary<string, object?>> clips;
            using (var con = Db.Open())
            {
                clips = con.Query(force
                    ? "SELECT id, filepath, storage FROM clips ORDER BY mtime DESC"
                    : "SELECT id, filepath, storage FROM clips WHERE transcribed_at IS NULL ORDER BY mtime DESC");
            }
            total = clips.Count;
            lock (TranscribeState.Lock) TranscribeState.Total = total;

            _task = Task.Run(() => Work(clips));
            return true;
        }
    }

    private static void Work(List<Dictionary<string, object?>> clips)
    {
        try
        {
            if (clips.Count == 0)
            {
                TranscribeState.PushLog("Brak nowych klipów do transkrypcji.");
                return;
            }

            foreach (var clip in clips)
            {
                lock (TranscribeState.Lock)
                {
                    if (TranscribeState.Cancel)
                    {
                        TranscribeState.PushLog("Anulowano.");
                        break;
                    }
                }

                long cid = Convert.ToInt64(clip["id"]);
                string fp = clip["filepath"] as string ?? "";
                string name = Path.GetFileName(fp);

                // Cloud-offloaded clips have no local file — the Python worker would hit
                // the !exists branch below and DELETE the row, silently dropping a clip the
                // user deliberately moved to Drive. We diverge from Python here on purpose
                // (user decision): skip storage='cloud' without deleting or transcribing.
                string storage = clip["storage"] as string ?? "local";
                if (storage == "cloud")
                {
                    TranscribeState.PushLog($"Pomijam (w chmurze): {name}");
                    lock (TranscribeState.Lock) TranscribeState.Done++;
                    continue;
                }

                // File vanished since the scan: drop the row and move on (no orphan).
                if (!File.Exists(fp))
                {
                    TranscribeState.PushLog($"[!] Brak pliku, pomijam: {name}");
                    using (var con = Db.Open())
                        con.Exec("DELETE FROM clips WHERE id=$id", ("$id", cid));
                    lock (TranscribeState.Lock) TranscribeState.Done++;
                    continue;
                }

                string game = new DirectoryInfo(Path.GetDirectoryName(fp) ?? "").Name;
                lock (TranscribeState.Lock)
                {
                    TranscribeState.Current = new Dictionary<string, object?>
                    {
                        ["id"] = cid, ["filename"] = name, ["game"] = game,
                    };
                }
                TranscribeState.PushLog($"-> {game} / {name}");

                var sw = System.Diagnostics.Stopwatch.StartNew();

                // Opportunistic housekeeping (mirrors the Python worker): fill duration
                // and a thumbnail while we have the file open anyway.
                double? dur = Media.ProbeDuration(fp);
                using (var con = Db.Open())
                {
                    if (dur is not null)
                        con.Exec("UPDATE clips SET duration=$d WHERE id=$id", ("$d", dur), ("$id", cid));
                    if (!File.Exists(Config.ThumbPath(cid)) && Media.MakeThumbnail(cid, fp))
                        con.Exec("UPDATE clips SET has_thumb=1 WHERE id=$id", ("$id", cid));
                }

                try
                {
                    var segs = Transcriber.Transcribe(fp);
                    using (var con = Db.Open())
                    {
                        con.Exec("DELETE FROM segments WHERE clip_id=$id", ("$id", cid));
                        foreach (var s in segs)
                            con.Exec(
                                "INSERT INTO segments (clip_id, start_s, end_s, text) VALUES ($c,$s,$e,$t)",
                                ("$c", cid), ("$s", s.Start), ("$e", s.End), ("$t", s.Text));
                        con.Exec("UPDATE clips SET transcribed_at=$ts, language=$lang WHERE id=$id",
                            ("$ts", NowIso()), ("$lang", Config.WhisperLang), ("$id", cid));
                    }
                    sw.Stop();
                    TranscribeState.PushLog(
                        $"   OK ({segs.Count} segm., {sw.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}s)");
                }
                catch (Exception ex)
                {
                    TranscribeState.PushLog($"   BŁĄD: {ex.Message}");
                    lock (TranscribeState.Lock) TranscribeState.Error = ex.Message;
                }

                lock (TranscribeState.Lock)
                {
                    TranscribeState.Done++;
                    TranscribeState.Current = null;
                }
            }
        }
        catch (Exception ex)
        {
            lock (TranscribeState.Lock) TranscribeState.Error = ex.Message;
            TranscribeState.PushLog($"FATAL: {ex.Message}");
        }
        finally
        {
            lock (TranscribeState.Lock)
            {
                TranscribeState.Running = false;
                TranscribeState.Cancel = false;
                TranscribeState.FinishedAt = NowIso();
            }
        }
    }
}
