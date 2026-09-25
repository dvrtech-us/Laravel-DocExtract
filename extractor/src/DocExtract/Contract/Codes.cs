namespace DocExtract.Contract;

public static class ExitCodes
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int Unsupported = 2;
    public const int Limits = 3;
    public const int CorruptOrEncrypted = 4;
    public const int Timeout = 5;
}

public static class ErrorCodes
{
    public const string Usage = "E_USAGE";
    public const string Unsupported = "E_UNSUPPORTED";
    public const string LimitInputSize = "E_LIMIT_INPUT_SIZE";
    public const string LimitDecompressed = "E_LIMIT_DECOMPRESSED";
    public const string LimitZipRatio = "E_LIMIT_ZIP_RATIO";
    public const string LimitPixels = "E_LIMIT_PIXELS";
    public const string LimitCpu = "E_LIMIT_CPU";
    public const string LimitMemory = "E_LIMIT_MEMORY";
    public const string Encrypted = "E_ENCRYPTED";
    public const string Corrupt = "E_CORRUPT";
    public const string Timeout = "E_TIMEOUT";
    public const string Failed = "E_FAILED";
    public const string SelfTest = "E_SELF_TEST";
    public const string UnsafePath = "E_UNSAFE_PATH";
    public const string Sandbox = "E_SANDBOX";
}

public static class WarningCodes
{
    public const string OcrUsed = "W_OCR_USED";
    public const string OcrUnavailable = "W_OCR_UNAVAILABLE";
    public const string NoTextLayer = "W_NO_TEXT_LAYER";
    public const string TableLowConfidence = "W_TABLE_LOW_CONFIDENCE";
    public const string Truncated = "W_TRUNCATED";
    public const string DepthLimit = "W_DEPTH_LIMIT";
    public const string ChildLimit = "W_CHILD_LIMIT";
    public const string UnsupportedChild = "W_UNSUPPORTED_CHILD";
    public const string Encrypted = "W_ENCRYPTED";
    public const string PartFailed = "W_PART_FAILED";
    public const string ImageDownscaled = "W_IMAGE_DOWNSCALED";
    public const string ImageSkipped = "W_IMAGE_SKIPPED";
    public const string FormulasCachedValues = "W_FORMULAS_CACHED_VALUES";
    public const string HiddenContent = "W_HIDDEN_CONTENT";

    public static readonly string[] ClosedSet =
    [
        OcrUsed, OcrUnavailable, NoTextLayer, TableLowConfidence, Truncated, DepthLimit, ChildLimit,
        UnsupportedChild, Encrypted, PartFailed, ImageDownscaled, ImageSkipped, FormulasCachedValues,
        HiddenContent,
    ];

    public static bool MarksPartial(string code) => code is
        Truncated or DepthLimit or ChildLimit or UnsupportedChild or Encrypted or PartFailed
        or ImageSkipped or OcrUnavailable or NoTextLayer;
}

public sealed class ExtractionException : Exception
{
    public int ExitCode { get; }
    public string ErrorCode { get; }

    public ExtractionException(int exitCode, string errorCode, Exception? inner = null)
        : base(errorCode, inner)
    {
        ExitCode = exitCode;
        ErrorCode = errorCode;
    }

    public static ExtractionException Limit(string code) => new(ExitCodes.Limits, code);
    public static ExtractionException Unsupported() => new(ExitCodes.Unsupported, ErrorCodes.Unsupported);
    public static ExtractionException Corrupt(Exception? inner = null) => new(ExitCodes.CorruptOrEncrypted, ErrorCodes.Corrupt, inner);
    public static ExtractionException Encrypted(Exception? inner = null) => new(ExitCodes.CorruptOrEncrypted, ErrorCodes.Encrypted, inner);
    public static ExtractionException Timeout() => new(ExitCodes.Timeout, ErrorCodes.Timeout);
    public static ExtractionException Failed(Exception? inner = null) => new(ExitCodes.Failure, ErrorCodes.Failed, inner);
    public static ExtractionException UnsafePath() => new(ExitCodes.Failure, ErrorCodes.UnsafePath);
    public static ExtractionException Sandbox(Exception? inner = null) => new(ExitCodes.Failure, ErrorCodes.Sandbox, inner);

    public static bool IsEncryptedError(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            var name = e.GetType().Name;
            if (name.Contains("Encrypt", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Password", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsCorruptError(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is InvalidDataException or FormatException)
            {
                return true;
            }

            var name = e.GetType().Name;
            if (name.Contains("Format", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Corrupt", StringComparison.OrdinalIgnoreCase)
                || name.Contains("InvalidPdf", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
