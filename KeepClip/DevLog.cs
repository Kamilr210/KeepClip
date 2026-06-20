namespace KeepClip;

#if KEEPCLIP_DEV
using System.Text;

/// <summary>
/// Developer-only in-memory event log. <see cref="Install"/> tees
/// <see cref="Console.Error"/> into a capped ring buffer at startup, so the in-app
/// "Logi aplikacji" panel can show everything the app already writes to stderr
/// (replay/audio status, the watchdog, transcription, errors) plus the explicit
/// user-action lines added throughout the request handlers (<see cref="Add"/>).
///
/// <para><b>Stripped from public releases.</b> When KEEPCLIP_DEV is NOT defined
/// (CI passes -p:PublicRelease=true) the whole capture machinery, the /api/logs
/// endpoint and the UI are gone. <see cref="Add"/>/<see cref="Install"/> still
/// exist but as no-ops (see the #else below) so call sites need no #if guards and
/// compile to nothing.</para>
/// </summary>
public static class DevLog
{
    private const int MaxLines = 4000;   // trims oldest first; generous for a session
    private static readonly object Gate = new();
    private static readonly LinkedList<(long Seq, string Line)> _lines = new();
    private static long _seq;

    /// <summary>Tee Console.Error → ring buffer. Call once, early in startup.</summary>
    public static void Install()
    {
        Console.SetError(TextWriter.Synchronized(new TeeWriter(Console.Error)));
        Add($"KeepClip {typeof(DevLog).Assembly.GetName().Version} — log uruchomiony (build deweloperski)");
    }

    public static void Add(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff}  {message}";
        lock (Gate)
        {
            _lines.AddLast((++_seq, line));
            while (_lines.Count > MaxLines) _lines.RemoveFirst();
        }
    }

    /// <summary>Lines with Seq &gt; <paramref name="since"/> plus the latest Seq, for
    /// incremental polling. <c>since=0</c> returns everything currently retained.</summary>
    public static (long seq, string[] lines) GetSince(long since)
    {
        lock (Gate)
        {
            var outList = new List<string>();
            foreach (var (seq, line) in _lines)
                if (seq > since) outList.Add(line);
            return (_seq, outList.ToArray());
        }
    }

    public static void Clear()
    {
        lock (Gate) _lines.Clear();
        Add("— log wyczyszczony —");
    }

    /// <summary>Forwards to real stderr AND mirrors each completed line into the ring.
    /// Line-buffered so partial Write() calls don't fragment entries.</summary>
    private sealed class TeeWriter : TextWriter
    {
        private readonly TextWriter _inner;
        private readonly StringBuilder _buf = new();
        public TeeWriter(TextWriter inner) => _inner = inner;
        public override Encoding Encoding => _inner.Encoding;

        public override void Write(char value)
        {
            _inner.Write(value);
            if (value == '\n') FlushLine();
            else if (value != '\r') _buf.Append(value);
        }

        public override void Write(string? value)
        {
            _inner.Write(value);
            if (string.IsNullOrEmpty(value)) return;
            foreach (var c in value)
            {
                if (c == '\n') FlushLine();
                else if (c != '\r') _buf.Append(c);
            }
        }

        public override void WriteLine(string? value)
        {
            _inner.WriteLine(value);
            if (_buf.Length > 0) FlushLine();              // emit any pending partial first
            if (!string.IsNullOrEmpty(value)) DevLog.Add(value);
        }

        private void FlushLine()
        {
            if (_buf.Length == 0) return;
            DevLog.Add(_buf.ToString());
            _buf.Clear();
        }
    }
}
#else
/// <summary>Public-release stub: developer log is compiled out. These no-ops let call
/// sites stay clean (no #if), and the JIT inlines them to nothing.</summary>
public static class DevLog
{
    public static void Install() { }
    public static void Add(string message) { }
}
#endif
