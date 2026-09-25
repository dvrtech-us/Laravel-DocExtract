namespace DocExtract.Ocr;

public interface IOcrEngine
{
    bool IsAvailable { get; }

    string? UnavailableReason { get; }

    string Recognize(ReadOnlyMemory<byte> image, CancellationToken cancellationToken);
}
