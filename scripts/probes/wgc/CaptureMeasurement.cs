using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;

internal static class CaptureMeasurement
{
    private sealed record FrameReading(long TimestampTicks, double ArrivalSeconds, int? Marker, double ReadbackMilliseconds, int Width, int Height, int BitmapWidth, int BitmapHeight, string PixelFormat, int RowStride, uint BufferCapacity);

    public static async Task<object> RunAsync(IDirect3DDevice device, GraphicsCaptureItem item, ProbeBoard board, int seconds, string output, CancellationToken cancellationToken, int? intervalMs, IDictionary<string, object?> intervalReport)
    {
        var clock = Stopwatch.StartNew();
        using var process = Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime;
        long paintStart = board.PaintCount;
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, item.Size);
        using var session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;
        bool supported = Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "MinUpdateInterval");
        intervalReport["supported"] = supported;
        if (supported)
        {
            intervalReport["defaultMs"] = session.MinUpdateInterval.TotalMilliseconds;
            if (intervalMs.HasValue) session.MinUpdateInterval = TimeSpan.FromMilliseconds(intervalMs.Value);
            intervalReport["effectiveMs"] = session.MinUpdateInterval.TotalMilliseconds;
        }
        else if (intervalMs.HasValue) throw new PlatformNotSupportedException("This guest does not expose GraphicsCaptureSession.MinUpdateInterval.");
        var readings = new List<FrameReading>();
        var firstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new object();
        Exception? captureError = null;
        int events = 0, busyCallbacks = 0, stopping = 0;
        void OnFrame(Direct3D11CaptureFramePool sender, object args)
        {
            Interlocked.Increment(ref events);
            if (!Monitor.TryEnter(gate)) { Interlocked.Increment(ref busyCallbacks); return; }
            try
            {
                if (Volatile.Read(ref stopping) != 0) return;
                try
                {
                    for (int attempt = 0; attempt < 3 && Volatile.Read(ref stopping) == 0; attempt++)
                    {
                        using var frame = sender.TryGetNextFrame();
                        if (frame is null) break;
                        var readback = Stopwatch.StartNew();
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        using var bitmap = SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Ignore).AsTask(timeout.Token).GetAwaiter().GetResult();
                        int? marker = ReadMarker(bitmap, frame.ContentSize.Width, frame.ContentSize.Height, out int stride, out uint capacity);
                        readback.Stop();
                        readings.Add(new(frame.SystemRelativeTime.Ticks, clock.Elapsed.TotalSeconds, marker, readback.Elapsed.TotalMilliseconds, frame.ContentSize.Width, frame.ContentSize.Height, bitmap.PixelWidth, bitmap.PixelHeight, bitmap.BitmapPixelFormat.ToString(), stride, capacity));
                        firstFrame.TrySetResult();
                    }
                }
                catch (Exception ex) { captureError = ex; firstFrame.TrySetException(ex); }
            }
            finally { Monitor.Exit(gate); }
        }

        pool.FrameArrived += OnFrame;
        try
        {
            session.StartCapture();
            await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
        }
        finally
        {
            Volatile.Write(ref stopping, 1);
            lock (gate) { pool.FrameArrived -= OnFrame; }
        }
        clock.Stop();
        if (captureError is not null) throw new InvalidOperationException("WGC frame readback failed.", captureError);
        long paintEnd = board.PaintCount;
        var snapshot = readings.ToArray();
        File.WriteAllText(Path.Combine(output, "frames.json"), JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        var markerValues = snapshot.Where(frame => frame.Marker.HasValue).Select(frame => frame.Marker!.Value).ToArray();
        int transitions = markerValues.Zip(markerValues.Skip(1)).Count(pair => pair.First != pair.Second);
        var timestamps = snapshot.Select(frame => frame.TimestampTicks).ToArray();
        var deltaMs = timestamps.Zip(timestamps.Skip(1)).Select(pair => (pair.Second - pair.First) / 10000.0).Order().ToArray();
        double measuredSeconds = timestamps.Length > 1 ? (timestamps[^1] - timestamps[0]) / 10000000.0 : 0;
        double cpuSeconds = (process.TotalProcessorTime - cpuStart).TotalSeconds;
        return new
        {
            events, busyCallbacks, frameCount = snapshot.Length, uniqueTimestamps = timestamps.Distinct().Count(),
            markerReadCount = markerValues.Length, distinctMarkers = markerValues.Distinct().Count(), markerTransitions = transitions,
            invalidMarkers = snapshot.Length - markerValues.Length, repeatedMarkers = Math.Max(0, markerValues.Length - 1 - transitions),
            wallSeconds = clock.Elapsed.TotalSeconds, timestampSpanSeconds = measuredSeconds,
            arrivalFps = (snapshot.Length - 1) / Math.Max(0.000001, measuredSeconds),
            freshMarkerFps = transitions / Math.Max(0.000001, measuredSeconds),
            producerPaints = paintEnd - paintStart, producerFps = (paintEnd - paintStart) / clock.Elapsed.TotalSeconds,
            timestampDeltaMinMs = deltaMs.FirstOrDefault(), timestampDeltaMedianMs = deltaMs.Length > 0 ? deltaMs[deltaMs.Length / 2] : 0,
            timestampDeltaP95Ms = deltaMs.Length > 0 ? deltaMs[Math.Min(deltaMs.Length - 1, (int)(deltaMs.Length * 0.95))] : 0,
            readbackMeanMs = snapshot.Length > 0 ? snapshot.Average(frame => frame.ReadbackMilliseconds) : 0,
            readbackMaxMs = snapshot.Length > 0 ? snapshot.Max(frame => frame.ReadbackMilliseconds) : 0,
            readbackBufferBytes = snapshot.Length > 0 ? snapshot.Max(frame => frame.BufferCapacity) : 0,
            readbackDimensions = snapshot.Select(frame => new { frame.BitmapWidth, frame.BitmapHeight, frame.PixelFormat, frame.RowStride }).Distinct().ToArray(),
            cpuSeconds, cpuPercentMachine = 100 * cpuSeconds / clock.Elapsed.TotalSeconds / Environment.ProcessorCount,
            peakWorkingSetMiB = process.PeakWorkingSet64 / 1048576.0,
            limitations = new[] { "Includes own board rendering and full-surface SoftwareBitmap CPU readback; not FFmpeg encoding or game FPS.", "The marker uses only a small CPU ROI, but CreateCopyFromSurfaceAsync copies the complete surface; its cost is included.", "Fresh frames are assessed by the counter encoded in actual captured pixels, not just callbacks.", "Measurement begins at StartCapture and includes first-frame startup." }
        };
    }

    private static int? ReadMarker(SoftwareBitmap bitmap, int contentWidth, int contentHeight, out int rowStride, out uint bufferCapacity)
    {
        rowStride = 0;
        bufferCapacity = 0;
        if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || contentWidth < ProbeBoard.MarkerX + (ProbeBoard.Bits + 3) * ProbeBoard.Cell || contentHeight < ProbeBoard.MarkerY + ProbeBoard.Cell) return null;
        using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
        using var reference = buffer.CreateReference();
        var plane = buffer.GetPlaneDescription(0);
        Marshal.ThrowExceptionForHR(reference.As<IMemoryBufferByteAccess>().GetBuffer(out var pointer, out var capacity));
        rowStride = plane.Stride;
        bufferCapacity = capacity;
        int Bit(int cell)
        {
            int x = ProbeBoard.MarkerX + cell * ProbeBoard.Cell + ProbeBoard.Cell / 2;
            int y = ProbeBoard.MarkerY + ProbeBoard.Cell / 2;
            long offset = plane.StartIndex + (long)y * plane.Stride + x * 4L;
            if (offset < 0 || offset + 2 >= capacity) return -1;
            int brightness = (Marshal.ReadByte(pointer, (int)offset) + Marshal.ReadByte(pointer, (int)offset + 1) + Marshal.ReadByte(pointer, (int)offset + 2)) / 3;
            return brightness >= 200 ? 1 : brightness <= 55 ? 0 : -1;
        }
        if (Bit(0) != 1 || Bit(1) != 0) return null;
        int value = 0, parity = 0;
        for (int bit = 0; bit < ProbeBoard.Bits; bit++)
        {
            int state = Bit(bit + 2);
            if (state < 0) return null;
            value |= state << bit;
            parity ^= state;
        }
        return parity == Bit(ProbeBoard.Bits + 2) ? value : null;
    }

    [ComImport, Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMemoryBufferByteAccess
    {
        [PreserveSig] int GetBuffer(out IntPtr buffer, out uint capacity);
    }
}
