namespace DocExtract.Extraction;

public enum OcrChoice
{
    Auto,
    Off,
}

public enum TableChoice
{
    Auto,
    Off,
}

public sealed class ExtractSettings
{
    public OcrChoice Ocr { get; init; } = OcrChoice.Auto;
    public TableChoice Tables { get; init; } = TableChoice.Auto;
}

public sealed class WarningDraft
{
    public string Code { get; set; } = "";
    public string Detail { get; set; } = "";
}

public sealed class PartDraft
{
    public int Index { get; set; }
    public string Path { get; set; } = "";
    public int Depth { get; set; }
    public string Kind { get; set; } = "";
    public string Mime { get; set; } = "";
    public long SizeBytes { get; set; }
    public int? Pages { get; set; }
    public string Engine { get; set; } = "";
    public long DurationMs { get; set; }
    public string Markdown { get; set; } = "";
    public bool Truncated { get; set; }
    public List<WarningDraft> Warnings { get; } = [];
    public List<string> ImageIds { get; } = [];
    public int CharStart { get; set; }
    public int CharEnd { get; set; }

    public void Warn(string code, string detail)
    {
        var existing = Warnings.FirstOrDefault(w => w.Code == code);
        if (existing is null)
        {
            Warnings.Add(new WarningDraft { Code = code, Detail = detail });
            return;
        }

        if (string.IsNullOrEmpty(detail) || existing.Detail.Contains(detail, StringComparison.Ordinal))
        {
            return;
        }

        existing.Detail = string.IsNullOrEmpty(existing.Detail) ? detail : existing.Detail + "; " + detail;
    }
}

public sealed class ImageDraft
{
    public string Id { get; set; } = "";
    public int PartIndex { get; set; }
    public byte[] Bytes { get; set; } = [];
    public string Mime { get; set; } = "image/png";
    public string Extension { get; set; } = ".png";
    public string File { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public int OcrChars { get; set; }
}

public sealed class ExtractionResult
{
    public List<PartDraft> Parts { get; } = [];
    public List<ImageDraft> Images { get; } = [];
    public bool Truncated { get; set; }
    public string Status { get; set; } = "ok";
    public int TotalChars { get; set; }
}
