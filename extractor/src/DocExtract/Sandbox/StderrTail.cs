using System.Text;

namespace DocExtract.Sandbox;

/// <summary>
/// Keeps the trailing bytes of a child's stderr so a long FailFast trace cannot
/// fill the anonymous-pipe buffer while the parent is still waiting.
/// </summary>
public sealed class StderrTail
{
    public const int MaxBytes = 64 * 1024;
    private readonly MemoryStream _stream = new();

    public void Append(ReadOnlySpan<byte> chunk)
    {
        if (chunk.IsEmpty)
        {
            return;
        }

        _stream.Write(chunk);
        if (_stream.Length <= MaxBytes)
        {
            return;
        }

        var all = _stream.ToArray();
        _stream.SetLength(0);
        _stream.Write(all, all.Length - MaxBytes, MaxBytes);
    }

    public string Text => Encoding.UTF8.GetString(_stream.ToArray());
}
