using System.Text.Json;
using System.Diagnostics;
using Windows.Graphics.Capture;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var output = Argument(args, "--out") ?? Path.Combine(AppContext.BaseDirectory, "results");
        Directory.CreateDirectory(output);
        var report = new Dictionary<string, object?> { ["startedUtc"] = DateTimeOffset.UtcNow, ["scope"] = "Own synthetic board in Windows Sandbox only; no audio, no encoder, no host capture" };
        int exitCode = 1;
        try
        {
            if (!Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Capture is forbidden outside a fresh Windows Sandbox (WDAGUtilityAccount).");
            report["isSupported"] = GraphicsCaptureSession.IsSupported();
            var attempts = new List<object>();
            report["deviceAttempts"] = attempts;
            using var graphics = NativeGraphics.Create(attempts);
            report["device"] = graphics.Driver;
            var target = Argument(args, "--target") ?? "monitor";
            if (target is not ("monitor" or "window")) throw new ArgumentException("Target must be monitor or window.");
            report["target"] = target;
            GraphicsCaptureItem? item = null;
            if (target == "monitor" || args.Contains("--capabilities"))
            {
                item = NativeGraphics.CreateForPrimaryMonitor();
                report["item"] = new { width = item.Size.Width, height = item.Size.Height, name = item.DisplayName };
            }
            if (!(bool)report["isSupported"]!) { report["status"] = "unsupported"; exitCode = 3; }
            else if (args.Contains("--capabilities")) { report["status"] = "supported"; exitCode = 0; }
            else
            {
                int duration = int.TryParse(Argument(args, "--duration"), out var parsed) ? parsed : 10;
                if (duration is < 3 or > 12) throw new ArgumentOutOfRangeException(nameof(duration));
                var interval = Argument(args, "--min-update-ms") ?? "8";
                int? intervalMs = interval switch { "8" => 8, "16" => 16, "default" => null, _ => throw new ArgumentException("Expected --min-update-ms 8, 16 or default.") };
                var intervalReport = new Dictionary<string, object?> { ["requestedMs"] = intervalMs };
                report["minUpdateInterval"] = intervalReport;
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                using var board = new ProbeBoard();
                using var cancellation = new CancellationTokenSource();
                board.StopRequested += () => cancellation.Cancel();
                board.Shown += async (_, _) =>
                {
                    try
                    {
                        await Task.Delay(500, cancellation.Token);
                        var captureItem = target == "window" ? NativeGraphics.CreateForWindow(board.Handle) : item!;
                        report["item"] = new { width = captureItem.Size.Width, height = captureItem.Size.Height, name = captureItem.DisplayName };
                        report["capture"] = await CaptureMeasurement.RunAsync(graphics.Device, captureItem, board, duration, output, cancellation.Token, intervalMs, intervalReport);
                        report["status"] = "measured";
                        exitCode = 0;
                    }
                    catch (Exception ex) { report["status"] = "failed"; report["error"] = ex.ToString(); report["hresult"] = $"0x{ex.HResult:X8}"; report["innerHresult"] = ex.InnerException is null ? null : $"0x{ex.InnerException.HResult:X8}"; }
                    finally { board.AllowClose = true; board.Close(); }
                };
                Application.Run(board);
            }
        }
        catch (Exception ex)
        {
            report["status"] = "failed";
            report["error"] = ex.ToString();
            report["hresult"] = $"0x{ex.HResult:X8}";
        }
        report["exitCode"] = exitCode;
        report["finishedUtc"] = DateTimeOffset.UtcNow;
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return exitCode;
    }

    private static string? Argument(string[] args, string key)
    {
        int index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
