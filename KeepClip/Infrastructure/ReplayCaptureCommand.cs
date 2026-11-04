using System.Diagnostics;

namespace KeepClip.Infrastructure;

internal sealed record ReplayAudioFormat(string SampleFormat, int SampleRate, int Channels);
internal sealed record ReplayCaptureOptions(int Tier, int Fps, string Quality, string? Encoder, int GrabWidth, int GrabHeight, int CaptureWidth, int CaptureHeight, int SegmentStart, string SegmentDirectory, ReplayAudioFormat? Audio);

internal static class ReplayCaptureCommand
{
    internal static ProcessStartInfo Create(ReplayCaptureOptions options)
    {
        bool gdi = options.Tier == ReplayService.TierGdi;
        var psi = new ProcessStartInfo
        {
            FileName = Config.Ffmpeg,
            RedirectStandardInput = true,
            RedirectStandardOutput = gdi,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var a = psi.ArgumentList;
        void Add(params string[] xs) { foreach (var x in xs) a.Add(x); }

        Add("-hide_banner", "-loglevel", "error");
        if (gdi) Add("-stats_period", "2", "-progress", "pipe:1");

        if (options.Audio is not null)
        {
            Add("-f", options.Audio.SampleFormat, "-ar", options.Audio.SampleRate.ToString(),
                "-ac", options.Audio.Channels.ToString(),
                "-thread_queue_size", "2048", "-i", "pipe:0");
        }

        string normCpu = $"scale={options.CaptureWidth}:{options.CaptureHeight}:force_original_aspect_ratio=decrease," +
                         $"pad={options.CaptureWidth}:{options.CaptureHeight}:(ow-iw)/2:(oh-ih)/2,format=nv12[v]";
        if (gdi)
        {
            Add("-f", "gdigrab", "-framerate", options.Fps.ToString(), "-thread_queue_size", "128",
                "-draw_mouse", "0", "-use_captureblt", "0", "-high_precision", "1",
                "-fflags", "autobsf",
                "-offset_x", "0", "-offset_y", "0", "-video_size", $"{options.GrabWidth}x{options.GrabHeight}",
                "-i", "desktop");
            Add("-filter_complex", $"[{(options.Audio is not null ? 1 : 0)}:v]{normCpu}");
        }
        else if (options.Tier == ReplayService.TierCpu)
        {
            Add("-filter_complex",
                $"ddagrab=framerate={options.Fps}:draw_mouse=0,hwdownload,format=bgra,{normCpu}");
        }
        else
        {
            Add("-filter_complex",
                $"ddagrab=framerate={options.Fps}:draw_mouse=0,null[v]");
        }
        Add("-map", "[v]");
        if (options.Audio is not null)
        {
            Add("-map", "0:a");
            Add("-c:a", "aac", "-b:a", "192k");
        }

        int qp = options.Quality switch { "low" => 28, "medium" => 24, _ => 20 };
        int gop = options.Fps;
        switch (options.Encoder)
        {
            case "h264_nvenc":
                Add("-c:v", "h264_nvenc", "-preset", "p7", "-rc", "constqp", "-qp", qp.ToString());
                break;
            case "h264_amf":
                Add("-c:v", "h264_amf", "-usage", "transcoding", "-quality", "quality",
                    "-rc", "cqp", "-qp_i", qp.ToString(), "-qp_p", qp.ToString(), "-qp_b", qp.ToString());
                break;
            case "h264_qsv":
                Add("-c:v", "h264_qsv", "-preset", "slow", "-global_quality", qp.ToString());
                break;
            default:
                Add("-c:v", "libx264", "-preset", "veryfast", "-crf", qp.ToString());
                break;
        }
        if (gdi)
        {
            // GDI may deliver fewer samples than requested. Keep their clock and start each ring segment on an IDR.
            string forcedIdr = options.Encoder is "h264_amf" or "h264_qsv" ? "-forced_idr" : "-forced-idr";
            Add("-fps_mode:v", "passthrough", "-enc_time_base:v", "filter", "-bf", "0", forcedIdr, "1",
                "-force_key_frames:v", $"expr:gte(t,n_forced*{ReplayService.SegmentSeconds})");
        }
        else if (options.Encoder is "h264_nvenc" or "h264_qsv") Add("-bf", "2");
        Add("-g", gop.ToString());
        Add("-f", "segment", "-segment_time", ReplayService.SegmentSeconds.ToString(),
            "-segment_start_number", options.SegmentStart.ToString(),
            "-reset_timestamps", "1", "-segment_format", "mpegts",
            Path.Combine(options.SegmentDirectory, "seg_%06d.ts"));

        return psi;
    }
}
