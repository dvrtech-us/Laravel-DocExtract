using System.Diagnostics;
using System.Text;
using DocExtract.Extraction;
using DocExtract.Text;

namespace DocExtract.Extractors;

public sealed class ImageExtractor : IExtractor
{
    public string Kind => "image";

    public void Extract(ExtractionInput input, ExtractionContext context)
    {
        var watch = Stopwatch.StartNew();
        var part = context.AddPart(input.LogicalPath, input.Depth, "image", input.Mime, input.Data.LongLength, Engine(context));
        try
        {
            var sb = new StringBuilder();
            sb.Append(MarkdownText.Heading(input.LogicalPath));
            var ocr = context.TryAttachImage(part, input.Data);
            AppendOcr(sb, part, ocr);
            part.Markdown = context.Take(sb.ToString(), part);
            part.Engine = Engine(context, part);
        }
        catch (Exception ex)
        {
            TextExtractor.HandleOrRethrow(input, part, context, ex);
        }
        finally
        {
            part.DurationMs = ExtractorTiming.Elapsed(watch);
        }
    }

    internal static void AppendOcr(StringBuilder sb, PartDraft part, string? ocr)
    {
        if (string.IsNullOrWhiteSpace(ocr) || part.ImageIds.Count == 0)
        {
            return;
        }

        sb.Append("### OCR text (").Append(part.ImageIds[^1]).Append(")\n\n");
        sb.Append(ocr.Trim()).Append("\n\n");
    }

    private static string Engine(ExtractionContext context, PartDraft? part = null)
    {
        var used = part?.Warnings.Any(w => w.Code == Contract.WarningCodes.OcrUsed) == true
            || (part is null && context.Settings.Ocr == OcrChoice.Auto && context.Ocr.IsAvailable);
        return used ? "skiasharp+tesseract" : "skiasharp";
    }
}
