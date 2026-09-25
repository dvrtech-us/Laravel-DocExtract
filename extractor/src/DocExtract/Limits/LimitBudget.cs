using DocExtract.Contract;

namespace DocExtract.Limits;

/// <summary>
/// Cumulative limits for one extraction tree. Counters move only forward.
/// Checks happen before the allocation they guard.
/// </summary>
public sealed class LimitBudget
{
    public const long DefaultMaxInputBytes = 25L * 1024 * 1024;
    public const long DefaultMaxDecompressedBytes = 200L * 1024 * 1024;
    public const double DefaultMaxZipRatio = 100d;
    public const int DefaultMaxDepth = 3;
    public const int DefaultMaxParts = 200;
    public const int DefaultMaxPdfPages = 300;
    public const int DefaultMaxOcrPages = 50;
    public const long DefaultMaxRasterPixels = 40_000_000;
    public const int DefaultMaxOutputChars = 2_000_000;
    public const int DefaultMaxImages = 50;
    public const long ZipRatioHardFailBytes = 1_048_576;
    public const int MaxImageLongEdge = 1568;
    public const int MaxImageBytes = 3_932_160;

    public long MaxInputBytes { get; init; } = DefaultMaxInputBytes;
    public long MaxDecompressedBytes { get; init; } = DefaultMaxDecompressedBytes;
    public double MaxZipRatio { get; init; } = DefaultMaxZipRatio;
    public int MaxDepth { get; init; } = DefaultMaxDepth;
    public int MaxParts { get; init; } = DefaultMaxParts;
    public int MaxPdfPages { get; init; } = DefaultMaxPdfPages;
    public int MaxOcrPages { get; init; } = DefaultMaxOcrPages;
    public long MaxRasterPixels { get; init; } = DefaultMaxRasterPixels;
    public int MaxOutputChars { get; init; } = DefaultMaxOutputChars;
    public int MaxImages { get; init; } = DefaultMaxImages;

    public long DecompressedUsed { get; private set; }
    public int PdfPagesUsed { get; private set; }
    public int OcrPagesUsed { get; private set; }
    public int OutputCharsUsed { get; private set; }
    public bool OutputTruncated { get; private set; }

    public long RemainingDecompressed => Math.Max(0, MaxDecompressedBytes - DecompressedUsed);

    public static bool ExceedsZipRatio(long uncompressed, long compressed, double maxRatio)
    {
        if (uncompressed <= 0)
        {
            return false;
        }

        if (compressed <= 0)
        {
            return true;
        }

        return uncompressed / (double)compressed > maxRatio;
    }

    public void EnsureDecompressedFits(long bytes)
    {
        if (bytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        if (bytes > RemainingDecompressed)
        {
            throw ExtractionException.Limit(ErrorCodes.LimitDecompressed);
        }
    }

    public void ChargeDecompressed(long bytes)
    {
        EnsureDecompressedFits(bytes);
        DecompressedUsed += bytes;
    }

    public bool TryUsePdfPage()
    {
        if (PdfPagesUsed >= MaxPdfPages)
        {
            return false;
        }

        PdfPagesUsed++;
        return true;
    }

    public bool TryUseOcrPage()
    {
        if (OcrPagesUsed >= MaxOcrPages)
        {
            return false;
        }

        OcrPagesUsed++;
        return true;
    }

    public string TakeOutput(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? "";
        }

        var remain = MaxOutputChars - OutputCharsUsed;
        if (remain <= 0)
        {
            OutputTruncated = true;
            return "";
        }

        var slice = Utf16Units.Truncate(text, remain);
        OutputCharsUsed += slice.Length;
        if (slice.Length < text.Length)
        {
            OutputTruncated = true;
        }

        return slice;
    }
}

public static class Utf16Units
{
    public static string Truncate(string text, int maxUnits)
    {
        if (maxUnits <= 0 || text.Length == 0)
        {
            return "";
        }

        if (text.Length <= maxUnits)
        {
            return text;
        }

        if (char.IsHighSurrogate(text[maxUnits - 1]))
        {
            maxUnits--;
        }

        return maxUnits <= 0 ? "" : text[..maxUnits];
    }
}
