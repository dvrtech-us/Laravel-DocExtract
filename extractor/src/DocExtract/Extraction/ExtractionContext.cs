using DocExtract.Contract;
using DocExtract.Images;
using DocExtract.Limits;
using DocExtract.Ocr;

namespace DocExtract.Extraction;

public sealed class ExtractionContext
{
    private readonly ExtractionPipeline _pipeline;

    internal ExtractionContext(ExtractionPipeline pipeline, LimitBudget budget, ExtractSettings settings, IOcrEngine ocr, CancellationToken cancellation)
    {
        _pipeline = pipeline;
        Budget = budget;
        Settings = settings;
        Ocr = ocr;
        Cancellation = cancellation;
    }

    public LimitBudget Budget { get; }
    public ExtractSettings Settings { get; }
    public IOcrEngine Ocr { get; }
    public CancellationToken Cancellation { get; }
    public List<PartDraft> Parts { get; } = [];
    public List<ImageDraft> Images { get; } = [];

    public PartDraft AddPart(string path, int depth, string kind, string mime, long size, string engine)
    {
        var part = new PartDraft
        {
            Index = Parts.Count,
            Path = path,
            Depth = depth,
            Kind = kind,
            Mime = mime,
            SizeBytes = size,
            Engine = engine,
        };
        Parts.Add(part);
        return part;
    }

    public string Take(string text, PartDraft part)
    {
        var taken = Budget.TakeOutput(text);
        if (taken.Length < text.Length)
        {
            part.Truncated = true;
            part.Warn(WarningCodes.Truncated, "output char cap");
        }

        return taken;
    }

    public bool CanAddPart() => Parts.Count < Budget.MaxParts;

    public void ProcessChild(byte[] data, string logicalPath, int depth) =>
        _pipeline.Process(data, logicalPath, depth, isRoot: false, this);

    public string? TryAttachImage(PartDraft part, byte[] raw)
    {
        Cancellation.ThrowIfCancellationRequested();
        if (!ImageNormalizer.TryGetPixelSize(raw, out var width, out var height))
        {
            part.Warn(WarningCodes.PartFailed, "image header");
            return null;
        }

        try
        {
            ImageNormalizer.EnsureWithinPixelCap(width, height, Budget.MaxRasterPixels);
        }
        catch (ExtractionException ex) when (ex.ErrorCode == ErrorCodes.LimitPixels && part.Depth > 0)
        {
            part.Warn(WarningCodes.ImageSkipped, "pixel cap");
            return null;
        }
        if (Images.Count >= Budget.MaxImages)
        {
            part.Warn(WarningCodes.ImageSkipped, "image cap");
            return null;
        }

        if (!ImageNormalizer.TryNormalize(raw, out var normalized, out var skipReason))
        {
            part.Warn(WarningCodes.ImageSkipped, skipReason);
            return null;
        }

        if (normalized.Downscaled)
        {
            part.Warn(WarningCodes.ImageDownscaled, $"{normalized.Width}x{normalized.Height}");
        }

        var id = "img-" + Images.Count;
        string? ocrText = null;
        var ocrChars = 0;
        if (Settings.Ocr == OcrChoice.Auto)
        {
            if (!Ocr.IsAvailable)
            {
                part.Warn(WarningCodes.OcrUnavailable, Ocr.UnavailableReason ?? "tessdata/eng.traineddata missing");
            }
            else if (!Budget.TryUseOcrPage())
            {
                part.Warn(WarningCodes.Truncated, "ocr page cap");
            }
            else
            {
                Cancellation.ThrowIfCancellationRequested();
                ocrText = (Ocr.Recognize(raw, Cancellation) ?? "").Trim();
                ocrChars = ocrText.Length;
                if (ocrChars > 0)
                {
                    part.Warn(WarningCodes.OcrUsed, id);
                }
            }
        }

        Images.Add(new ImageDraft
        {
            Id = id,
            PartIndex = part.Index,
            Bytes = normalized.Bytes,
            Mime = normalized.Mime,
            Extension = normalized.Extension,
            Width = normalized.Width,
            Height = normalized.Height,
            OcrChars = ocrChars,
        });
        part.ImageIds.Add(id);
        return ocrText;
    }
}

internal sealed class ChildLimitException : Exception;
