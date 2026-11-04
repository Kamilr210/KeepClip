namespace KeepClip.Tests.Infrastructure;

public class ReplaySegmentSnapshotTests
{
    private static byte[] Packet(int pid, bool start, int? pesLength = null, int? adaptationLength = null, byte flags = 0)
    {
        var packet = Enumerable.Repeat((byte)0xff, 188).ToArray();
        packet[0] = 0x47;
        packet[1] = (byte)((pid >> 8) | (start ? 0x40 : 0));
        packet[2] = (byte)pid;
        packet[3] = (byte)(adaptationLength.HasValue ? 0x30 : 0x10);
        int payload = 4;
        if (adaptationLength.HasValue)
        {
            packet[4] = (byte)adaptationLength.Value;
            if (adaptationLength.Value > 0) packet[5] = flags;
            payload = 5 + adaptationLength.Value;
        }
        if (start) new byte[] { 0, 0, 1, (byte)(pid == 256 ? 0xe0 : 0xc0), (byte)((pesLength ?? 0) >> 8), (byte)(pesLength ?? 0) }.CopyTo(packet, payload);
        return packet;
    }

    private static async Task<byte[]> Snapshot(byte[] input, bool recording = false)
    {
        var path = Path.Combine(TestEnvironment.NewTempDir("replay-ts"), "snapshot.ts");
        using var source = new MemoryStream(input);
        using (var file = File.Create(path)) await ReplaySegmentSnapshot.CopyAsync(source, file, recording);
        Assert.Equal(input, source.ToArray());
        return File.ReadAllBytes(path);
    }

    [Fact]
    public async Task Every_cut_inside_a_video_or_audio_packet_keeps_only_proven_complete_pes()
    {
        byte[] completeVideo = [.. Packet(256, true, adaptationLength: 7, flags: 0x50), .. Packet(256, false, adaptationLength: 0)];
        byte[] audio = Packet(257, true, 178);
        byte[] nextVideo = [.. Packet(256, true, adaptationLength: 7, flags: 0x50), .. Packet(256, false), .. Packet(256, false, adaptationLength: 1)];
        byte[] stream = [.. completeVideo, .. audio, .. nextVideo];
        for (int length = 0; length <= stream.Length; length++)
        {
            int keep = length >= stream.Length ? stream.Length : length >= completeVideo.Length + audio.Length ? completeVideo.Length + audio.Length : length >= completeVideo.Length ? completeVideo.Length : 0;
            Assert.Equal(stream[..keep], await Snapshot(stream[..length]));
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 0x40)]
    [InlineData(8, 0x50)]
    public async Task Terminal_stuffing_preserves_complete_unbounded_video(int length, byte flags)
    {
        var packet = Packet(256, true, adaptationLength: length, flags: flags);
        Assert.Equal(packet, await Snapshot(packet));
    }

    [Theory]
    [InlineData(1, 0x40)]
    [InlineData(7, 0x50)]
    [InlineData(8, 0x58)]
    public async Task Pcr_random_access_and_unsupported_flags_do_not_prove_the_end_of_video(int length, byte flags)
    {
        Assert.Empty(await Snapshot(Packet(256, true, adaptationLength: length, flags: flags)));
    }

    [Fact]
    public async Task Following_pes_proves_a_video_frame_that_exactly_fills_its_last_ts_packet()
    {
        var video = Packet(256, true);
        var unfinished = Packet(257, true, 1000);
        Assert.Empty(await Snapshot(video));
        Assert.Equal(video, await Snapshot([.. video, .. unfinished]));
    }

    private static byte[] ReorderedPacket(int pts, int dts)
    {
        var packet = Packet(256, true, 178);
        packet[10] = 0x80;
        packet[11] = 0xc0;
        packet[12] = 10;
        static void WriteTime(Span<byte> bytes, int value, int prefix)
        {
            bytes[0] = (byte)(prefix | ((value >> 29) & 0x0e) | 1);
            bytes[1] = (byte)(value >> 22);
            bytes[2] = (byte)(((value >> 14) & 0xfe) | 1);
            bytes[3] = (byte)(value >> 7);
            bytes[4] = (byte)(((value << 1) & 0xfe) | 1);
        }
        WriteTime(packet.AsSpan(13), pts, 0x30);
        WriteTime(packet.AsSpan(18), dts, 0x10);
        return packet;
    }

    [Fact]
    public async Task A_live_tail_keeps_whole_reordered_picture_groups()
    {
        byte[] first = ReorderedPacket(90000, 84000);
        byte[] reference = ReorderedPacket(99000, 87000);
        byte[] b1 = ReorderedPacket(93000, 90000), b2 = ReorderedPacket(96000, 93000);
        byte[] next = ReorderedPacket(108000, 96000);
        Assert.Equal(first, await Snapshot([.. first, .. reference], recording: true));
        Assert.Equal(first, await Snapshot([.. first, .. reference, .. b1], recording: true));
        Assert.Equal(first, await Snapshot([.. first, .. reference, .. b1, .. b2], recording: true));
        byte[] completeGroup = [.. first, .. reference, .. b1, .. b2];
        Assert.Equal(completeGroup, await Snapshot([.. completeGroup, .. next], recording: true));
        Assert.Equal(completeGroup, await Snapshot(completeGroup));
        Assert.Equal(first, await Snapshot([.. first, .. reference, .. b1[..120]]));
    }

    [Fact]
    public async Task Broken_or_incomplete_transport_packet_is_excluded_with_its_open_pes()
    {
        var complete = Packet(257, true, 178);
        var open = Packet(256, true);
        var broken = Packet(256, false);
        broken[0] = 0;
        Assert.Equal(complete, await Snapshot([.. complete, .. open, .. broken]));
        var invalidAdaptation = Packet(256, false);
        invalidAdaptation[3] = 0x30;
        invalidAdaptation[4] = 255;
        Assert.Equal(complete, await Snapshot([.. complete, .. open, .. invalidAdaptation]));
    }

    private sealed class GrowingStream(byte[] initial) : MemoryStream
    {
        private bool extended;
        internal void Initialize() => Write(initial);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!extended)
            {
                extended = true;
                long position = Position;
                Position = Length;
                Write(initial);
                Position = position;
            }
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    [Fact]
    public async Task Bytes_appended_after_the_snapshot_started_are_not_saved()
    {
        var initial = Packet(257, true, 178);
        using var source = new GrowingStream(initial);
        source.Initialize();
        source.Position = 0;
        var path = Path.Combine(TestEnvironment.NewTempDir("replay-growing-ts"), "snapshot.ts");
        using (var snapshot = File.Create(path)) await ReplaySegmentSnapshot.CopyAsync(source, snapshot);
        Assert.Equal(initial, File.ReadAllBytes(path));
        Assert.Equal(initial.Length * 2, source.Length);
    }
}