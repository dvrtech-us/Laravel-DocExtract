using System.Text.Json;
using DocExtract.Contract;
using DocExtract.Extraction;
using DocExtract.Limits;
using DocExtract.Ocr;

namespace DocExtract.SelfTest;

public sealed class SelfTestReport
{
    public string Version { get; set; } = EngineCatalog.Version;
    public bool Passed { get; set; }
    public bool OcrAvailable { get; set; }
    public string? OcrDetail { get; set; }
    public List<SelfTestCheck> Checks { get; set; } = [];
}

public sealed class SelfTestCheck
{
    public string Kind { get; set; } = "";
    public bool Passed { get; set; }
    public string Detail { get; set; } = "";
}

public static class SelfTestRunner
{
    public static SelfTestReport Run(IOcrEngine? ocr = null, bool requireOcr = false)
    {
        ocr ??= new TesseractOcrEngine();
        var pipeline = new ExtractionPipeline(ocr);
        var report = new SelfTestReport
        {
            OcrAvailable = ocr.IsAvailable,
            OcrDetail = ocr.IsAvailable ? "eng" : ocr.UnavailableReason,
        };
        if (requireOcr && !ocr.IsAvailable)
        {
            report.Checks.Add(new SelfTestCheck
            {
                Kind = "ocr",
                Passed = false,
                Detail = ocr.UnavailableReason ?? "OCR unavailable",
            });
        }

        Check(report, pipeline, "text", "text.txt", FixtureFactory.Text(), FixtureFactory.TextToken, "text");
        Check(report, pipeline, "png", "pixel.png", FixtureFactory.Png(), null, "image");
        Check(report, pipeline, "pdf", "page.pdf", FixtureFactory.Pdf(), FixtureFactory.PdfToken, "pdf");
        Check(report, pipeline, "docx", "doc.docx", FixtureFactory.Docx(), FixtureFactory.DocxToken, "docx");
        Check(report, pipeline, "xlsx", "book.xlsx", FixtureFactory.Xlsx(), FixtureFactory.XlsxToken, "xlsx");
        Check(report, pipeline, "pptx", "deck.pptx", FixtureFactory.Pptx(), FixtureFactory.PptxToken, "pptx");
        Check(report, pipeline, "eml", "mail.eml", FixtureFactory.Eml(), FixtureFactory.EmlToken, "eml");
        Check(report, pipeline, "zip", "bundle.zip", FixtureFactory.Zip(), FixtureFactory.ZipToken, "zip");
        Check(report, pipeline, "msg", "note.msg", FixtureFactory.Msg(), FixtureFactory.MsgToken, "msg");
        report.Passed = report.Checks.All(c => c.Passed);
        return report;
    }

    public static string ToJson(SelfTestReport report) =>
        JsonSerializer.Serialize(report, Output.ResultWriter.JsonOptions);

    private static void Check(SelfTestReport report, ExtractionPipeline pipeline, string name, string fileName, byte[] data, string? token, string kind)
    {
        try
        {
            var result = pipeline.Extract(data, fileName, new LimitBudget(), new ExtractSettings());
            var markdown = string.Concat(result.Parts.Select(p => p.Markdown));
            var kindOk = result.Parts.Count > 0 && result.Parts[0].Kind == kind;
            var tokenOk = token is null || markdown.Contains(token, StringComparison.Ordinal);
            var imageOk = kind != "image" || result.Images.Count > 0;
            var xlsxOk = name != "xlsx"
                || (markdown.Contains(FixtureFactory.XlsxHiddenToken, StringComparison.Ordinal)
                    && result.Parts[0].Warnings.Any(w => w.Code == WarningCodes.FormulasCachedValues)
                    && result.Parts[0].Warnings.Any(w => w.Code == WarningCodes.HiddenContent));
            var emlOk = name != "eml" || markdown.Contains(FixtureFactory.EmlAttachmentToken, StringComparison.Ordinal);
            var msgOk = name != "msg" || markdown.Contains(FixtureFactory.MsgSubject, StringComparison.Ordinal);
            var passed = kindOk && tokenOk && imageOk && xlsxOk && emlOk && msgOk;
            report.Checks.Add(new SelfTestCheck
            {
                Kind = name,
                Passed = passed,
                Detail = passed ? "ok" : "missing expected content",
            });
        }
        catch (Exception ex)
        {
            report.Checks.Add(new SelfTestCheck
            {
                Kind = name,
                Passed = false,
                Detail = ex is ExtractionException extraction ? extraction.ErrorCode : ex.GetType().Name,
            });
        }
    }
}
