using System.Diagnostics;
using DocExtract.Contract;
using DocExtract.Extractors;
using DocExtract.Limits;
using DocExtract.Ocr;
using DocExtract.Sniff;

namespace DocExtract.Extraction;

public sealed class ExtractionPipeline
{
    private readonly IOcrEngine _ocr;
    private readonly Dictionary<string, IExtractor> _extractors;

    public ExtractionPipeline(IOcrEngine? ocr = null)
    {
        _ocr = ocr ?? new TesseractOcrEngine();
        IExtractor[] extractors =
        [
            new TextExtractor(),
            new ImageExtractor(),
            new PdfExtractor(),
            new DocxExtractor(),
            new XlsxExtractor(),
            new PptxExtractor(),
            new EmlExtractor(),
            new MsgExtractor(),
            new ZipExtractor(),
        ];
        _extractors = extractors.ToDictionary(e => e.Kind, StringComparer.Ordinal);
    }

    public IOcrEngine Ocr => _ocr;

    public ExtractionResult Extract(byte[] data, string fileName, LimitBudget? budget = null, ExtractSettings? settings = null, CancellationToken cancellationToken = default)
    {
        budget ??= new LimitBudget();
        settings ??= new ExtractSettings();
        var ctx = new ExtractionContext(this, budget, settings, _ocr, cancellationToken);
        Process(data, string.IsNullOrWhiteSpace(fileName) ? "input" : fileName, depth: 0, isRoot: true, ctx);
        return Finish(ctx);
    }

    internal void Process(byte[] data, string logicalPath, int depth, bool isRoot, ExtractionContext context)
    {
        context.Cancellation.ThrowIfCancellationRequested();
        if (context.Parts.Count >= context.Budget.MaxParts)
        {
            throw new ChildLimitException();
        }

        if (isRoot)
        {
            if (data.LongLength > context.Budget.MaxInputBytes)
            {
                throw ExtractionException.Limit(ErrorCodes.LimitInputSize);
            }

            if (data.Length == 0)
            {
                throw ExtractionException.Corrupt();
            }
        }

        var detection = TypeSniffer.Detect(data, logicalPath);
        if (detection.Kind == DocumentKind.Unsupported)
        {
            if (detection.Mime == "application/vnd.ms-office.encrypted")
            {
                if (isRoot)
                {
                    throw ExtractionException.Encrypted();
                }

                var encrypted = context.AddPart(logicalPath, depth, "unsupported", detection.Mime, data.LongLength, "none");
                encrypted.Warn(WarningCodes.Encrypted, "EncryptedPackage");
                encrypted.Markdown = context.Take(Text.MarkdownText.Heading(logicalPath) + "Encrypted content.\n", encrypted);
                return;
            }

            if (isRoot)
            {
                throw ExtractionException.Unsupported();
            }

            var unsupported = context.AddPart(logicalPath, depth, detection.KindName, detection.Mime, data.LongLength, "none");
            unsupported.Warn(WarningCodes.UnsupportedChild, logicalPath);
            unsupported.Markdown = context.Take(Text.MarkdownText.Heading(logicalPath) + "Unsupported content.\n", unsupported);
            return;
        }

        if (!_extractors.TryGetValue(detection.KindName, out var extractor))
        {
            if (isRoot)
            {
                throw ExtractionException.Unsupported();
            }

            var missing = context.AddPart(logicalPath, depth, "unsupported", detection.Mime, data.LongLength, "none");
            missing.Warn(WarningCodes.UnsupportedChild, detection.KindName);
            missing.Markdown = context.Take(Text.MarkdownText.Heading(logicalPath) + "Unsupported content.\n", missing);
            return;
        }

        var input = new ExtractionInput
        {
            Data = data,
            LogicalPath = logicalPath,
            Depth = depth,
            IsRoot = isRoot,
            Kind = detection.KindName,
            Mime = detection.Mime,
        };
        extractor.Extract(input, context);
    }

    private static ExtractionResult Finish(ExtractionContext context)
    {
        var result = new ExtractionResult();
        var offset = 0;
        foreach (var part in context.Parts)
        {
            part.CharStart = offset;
            offset += part.Markdown.Length;
            part.CharEnd = offset;
            if (part.Truncated)
            {
                result.Truncated = true;
            }

            result.Parts.Add(part);
        }

        result.Images.AddRange(context.Images);
        result.TotalChars = offset;
        result.Truncated |= context.Budget.OutputTruncated;
        var partial = result.Truncated;
        if (!partial)
        {
            foreach (var part in result.Parts)
            {
                if (part.Warnings.Any(w => WarningCodes.MarksPartial(w.Code)))
                {
                    partial = true;
                    break;
                }
            }
        }

        result.Status = partial ? "partial" : "ok";
        return result;
    }
}

public static class ExtractorTiming
{
    public static long Elapsed(Stopwatch watch) => Math.Max(0, watch.ElapsedMilliseconds);
}
