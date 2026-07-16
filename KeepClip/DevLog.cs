namespace KeepClip;

#if KEEPCLIP_DEV
using System.Text;

// W trybie deweloperskim przechwytuje stderr do ograniczonego bufora kołowego.
// W wydaniu publicznym implementację zastępują puste metody, dzięki czemu miejsca
// wywołań nie wymagają dyrektyw #if.
public static class DevLog
{
    private const int MaxLines = 4000;   // Po przekroczeniu limitu usuwa najstarsze wpisy.
    private static readonly object Gate = new();
    private static readonly LinkedList<(long Seq, string Line)> _lines = new();
    private static long _seq;

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

    // Buforowanie do końca wiersza zapobiega dzieleniu wpisów przez częściowe zapisy.
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
            if (_buf.Length > 0) FlushLine();              // Najpierw zapisuje oczekujący fragment.
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
public static class DevLog
{
    public static void Install() { }
    public static void Add(string message) { }
}
#endif
