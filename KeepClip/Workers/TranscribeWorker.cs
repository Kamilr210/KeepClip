using System.Globalization;

namespace KeepClip.Workers;

public static class TranscribeWorker
{
    private static readonly object StartLock = new();
    private static Task? _task;

    public static string NowIso() =>
        DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

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

        // Lista jest wyliczana synchronicznie, aby odpowiedź od razu znała dokładną liczbę.
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

                // Klipy w chmurze nie mają lokalnego pliku, ale nie wolno usuwać ich z bazy.
                string storage = clip["storage"] as string ?? "local";
                if (storage == "cloud")
                {
                    TranscribeState.PushLog($"Pomijam (w chmurze): {name}");
                    lock (TranscribeState.Lock) TranscribeState.Done++;
                    continue;
                }

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
