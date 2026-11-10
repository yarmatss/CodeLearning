using System.Text;

namespace CodeLearning.Core.Grading;

// Captures at most a fixed number of bytes from a stream. Everything past the limit is read and
// discarded, so a program flooding its output never blocks on a full pipe and never grows memory
public sealed class BoundedOutputCapture(int limitBytes)
{
    public const string TruncationMarker = "... [Output Truncated]";

    private readonly byte[] _buffer = new byte[limitBytes];
    private readonly Lock _lock = new();
    private int _length;
    private bool _truncated;

    public bool Truncated
    {
        get
        {
            lock (_lock)
            {
                return _truncated;
            }
        }
    }

    public async Task CopyFromAsync(Stream source, CancellationToken cancellationToken = default)
    {
        var chunk = new byte[8192];

        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            Append(chunk.AsSpan(0, read));
        }
    }

    public void Append(ReadOnlySpan<byte> data)
    {
        lock (_lock)
        {
            var room = _buffer.Length - _length;
            var toCopy = Math.Min(room, data.Length);

            data[..toCopy].CopyTo(_buffer.AsSpan(_length));
            _length += toCopy;

            if (data.Length > toCopy)
            {
                _truncated = true;
            }
        }
    }

    // Safe to call while a copy is still running, e.g. after a timeout: returns what arrived so far
    public string GetText()
    {
        lock (_lock)
        {
            var text = Encoding.UTF8.GetString(_buffer, 0, _length);
            return _truncated ? text + "\n" + TruncationMarker : text;
        }
    }
}
