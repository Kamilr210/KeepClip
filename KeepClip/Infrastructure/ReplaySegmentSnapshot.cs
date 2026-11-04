namespace KeepClip.Infrastructure;

internal static class ReplaySegmentSnapshot
{
    private const int PacketSize = 188;

    internal static async Task CopyAsync(Stream source, FileStream snapshot, bool recording = false)
    {
        // Freeze the byte limit before copying a segment that the recorder can still extend.
        long remaining = source.Length;
        var buffer = new byte[81920];
        while (remaining > 0)
        {
            int read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)));
            if (read == 0) break;
            await snapshot.WriteAsync(buffer.AsMemory(0, read));
            remaining -= read;
        }
        snapshot.Flush();
        snapshot.Position = 0;
        snapshot.SetLength(CompleteLength(snapshot, recording));
    }

    private static long CompleteLength(Stream snapshot, bool recording)
    {
        Span<byte> packet = stackalloc byte[PacketSize];
        long complete = 0;
        long presentationBoundary = 0, latestPts = -1;
        bool reordered = false;
        int mediaPid = -1, remaining = -1;
        while (snapshot.Length - snapshot.Position >= PacketSize)
        {
            long start = snapshot.Position;
            snapshot.ReadExactly(packet);
            if (packet[0] != 0x47 || (packet[1] & 0x80) != 0 || (packet[3] & 0xc0) != 0) break;
            int control = (packet[3] >> 4) & 3;
            if (control == 0) break;
            if ((control & 1) == 0) continue;
            int payload = control == 3 ? 5 + packet[4] : 4;
            if (payload >= PacketSize) break;
            int pid = ((packet[1] & 0x1f) << 8) | packet[2];
            bool pes = (packet[1] & 0x40) != 0 && payload + 6 <= PacketSize
                && packet[payload] == 0 && packet[payload + 1] == 0 && packet[payload + 2] == 1
                && packet[payload + 3] is >= 0xc0 and <= 0xef;
            if (pes)
            {
                // KeepClip's FFmpeg MPEG-TS muxer writes each audio/video PES contiguously.
                // A following PES proves the preceding unbounded video PES has finished.
                if (mediaPid >= 0 && remaining > 0) break;
                complete = start;
                mediaPid = pid;
                int length = (packet[payload + 4] << 8) | packet[payload + 5];
                remaining = length == 0 ? -1 : length + 6;
                if (packet[payload + 3] >= 0xe0 && payload + 19 <= PacketSize && (packet[payload + 7] & 0xc0) == 0xc0 && packet[payload + 8] >= 10)
                {
                    long pts = Timestamp(packet[(payload + 9)..]);
                    long dts = Timestamp(packet[(payload + 14)..]);
                    reordered |= pts != dts;
                    if (latestPts < 0 || pts > latestPts)
                    {
                        // A new reference picture confirms that the preceding B-frame group has arrived.
                        // Keep the entire last group together so the saved tail has no presentation-time hole.
                        presentationBoundary = start;
                        latestPts = pts;
                    }
                }
            }
            if (pid != mediaPid) continue;
            if (remaining > 0)
            {
                remaining -= PacketSize - payload;
                if (remaining < 0) break;
            }
            if (remaining == 0 || (remaining == -1 && HasEndStuffing(packet, control)))
            {
                complete = snapshot.Position;
                mediaPid = -1;
                remaining = -1;
            }
        }
        return reordered && (recording || complete < snapshot.Length) ? Math.Min(complete, presentationBoundary) : complete;
    }

    private static long Timestamp(ReadOnlySpan<byte> bytes)
        => ((long)(bytes[0] & 0x0e) << 29) | ((long)bytes[1] << 22) | ((long)(bytes[2] & 0xfe) << 14) | ((long)bytes[3] << 7) | ((long)(bytes[4] & 0xfe) >> 1);

    private static bool HasEndStuffing(ReadOnlySpan<byte> packet, int control)
    {
        if (control != 3) return false;
        int length = packet[4];
        if (length == 0) return true;
        int flags = packet[5];
        // FFmpeg uses discontinuity, random-access and PCR flags for the replay's H.264/HEVC/AAC.
        // Extra adaptation bytes (or an empty flags byte) are stuffing added at the end of a PES.
        if ((flags & ~0xd0) != 0) return false;
        int fields = 1 + ((flags & 0x10) != 0 ? 6 : 0);
        return flags == 0 || length > fields;
    }
}