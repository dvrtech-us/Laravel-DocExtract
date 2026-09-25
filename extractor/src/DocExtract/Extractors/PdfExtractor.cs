using System.Diagnostics;
using System.Text;
using DocExtract.Contract;
using DocExtract.Extraction;
using DocExtract.Text;
using PDFtoImage;
using SkiaSharp;
using Tabula;
using Tabula.Extractors;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;

namespace DocExtract.Extractors;

public sealed class PdfExtractor : IExtractor
{
    public string Kind => "pdf";

    public void Extract(ExtractionInput input, ExtractionContext context)
    {
        var watch = Stopwatch.StartNew();
        var part = context.AddPart(input.LogicalPath, input.Depth, "pdf", input.Mime, input.Data.LongLength, "pdfpig");
        var usedTabula = false;
        try
        {
            using var stream = new MemoryStream(input.Data, writable: false);
            using var document = PdfDocument.Open(stream);
            part.Pages = document.NumberOfPages;
            var sb = new StringBuilder();
            sb.Append(MarkdownText.Heading(input.LogicalPath));
            var skippedPages = 0;
            var anyText = false;
            for (var number = 1; number <= document.NumberOfPages; number++)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                if (!context.Budget.TryUsePdfPage())
                {
                    skippedPages += document.NumberOfPages - number + 1;
                    break;
                }

                Page page;
                try
                {
                    page = document.GetPage(number);
                }
                catch (Exception ex) when (ExtractionException.IsCorruptError(ex) || ExtractionException.IsEncryptedError(ex))
                {
                    part.Warn(WarningCodes.PartFailed, "page " + number);
                    continue;
                }

                var text = LayoutText(page);
                var thin = string.IsNullOrWhiteSpace(text) || text.Trim().Length < 20;
                if (!thin)
                {
                    anyText = true;
                }

                if (thin)
                {
                    part.Warn(WarningCodes.NoTextLayer, "page " + number);
                    sb.Append("### Page ").Append(number).Append("\n\n");
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        sb.Append(text.Trim()).Append("\n\n");
                    }

                    AppendScannedPage(input, context, part, stream, page, number, sb);
                }
                else
                {
                    sb.Append("### Page ").Append(number).Append("\n\n");
                    sb.Append(text.Trim()).Append("\n\n");
                }

                if (context.Settings.Tables == TableChoice.Auto && !thin)
                {
                    if (AppendTables(document, number, sb, part))
                    {
                        usedTabula = true;
                    }
                }
            }

            if (skippedPages > 0)
            {
                part.Warn(WarningCodes.Truncated, "pdf page cap");
            }

            if (document.IsEncrypted && !anyText)
            {
                part.Warn(WarningCodes.Encrypted, "no text");
            }

            part.Markdown = context.Take(sb.ToString(), part);
            part.Engine = EngineName(part, usedTabula);
        }
        catch (Exception ex)
        {
            TextExtractor.HandleOrRethrow(input, part, context, ex);
        }
        finally
        {
            part.DurationMs = watch.ElapsedMilliseconds;
        }
    }

    private static string EngineName(PartDraft part, bool usedTabula)
    {
        var pieces = new List<string> { "pdfpig" };
        if (usedTabula)
        {
            pieces.Add("tabula");
        }

        if (part.Warnings.Any(w => w.Code == WarningCodes.OcrUsed))
        {
            pieces.Add("tesseract");
        }

        return string.Join('+', pieces);
    }

    private static string LayoutText(Page page)
    {
        try
        {
            var words = page.GetWords().ToList();
            if (words.Count == 0)
            {
                return page.Text ?? "";
            }

            var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
            var ordered = UnsupervisedReadingOrderDetector.Instance.Get(blocks.ToList()).ToList();
            var sb = new StringBuilder();
            foreach (var block in ordered)
            {
                var text = (block.Text ?? "").Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                if (IsHeading(block, text))
                {
                    sb.Append("### ").Append(text.Replace('\n', ' ')).Append("\n\n");
                }
                else
                {
                    sb.Append(text).Append("\n\n");
                }
            }

            var rendered = sb.ToString().Trim();
            return rendered.Length == 0 ? page.Text ?? "" : rendered;
        }
        catch (Exception)
        {
            return page.Text ?? "";
        }
    }

    private static bool IsHeading(UglyToad.PdfPig.DocumentLayoutAnalysis.TextBlock block, string text)
    {
        if (text.Contains('\n') || text.Length is < 1 or > 80)
        {
            return false;
        }

        var letter = block.TextLines.FirstOrDefault()?.Words.FirstOrDefault()?.Letters.FirstOrDefault();
        return letter is not null && letter.FontSize >= 16;
    }

    private static void AppendScannedPage(ExtractionInput input, ExtractionContext context, PartDraft part, MemoryStream stream, Page page, int number, StringBuilder sb)
    {
        if (context.Settings.Ocr != OcrChoice.Auto)
        {
            return;
        }

        if (!context.Ocr.IsAvailable)
        {
            part.Warn(WarningCodes.OcrUnavailable, context.Ocr.UnavailableReason ?? "tessdata/eng.traineddata missing");
            return;
        }

        if (!context.Budget.TryUseOcrPage())
        {
            part.Warn(WarningCodes.Truncated, "ocr page cap");
            return;
        }

        var dpi = SafeDpi(page.Width, page.Height, context.Budget.MaxRasterPixels);
        if (dpi <= 0)
        {
            throw ExtractionException.Limit(ErrorCodes.LimitPixels);
        }

        stream.Position = 0;
        using var bitmap = Conversion.ToImage(stream, page: number - 1, leaveOpen: true, options: new RenderOptions(Dpi: dpi));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
        var png = encoded?.ToArray() ?? [];
        if (png.Length == 0)
        {
            part.Warn(WarningCodes.ImageSkipped, "page " + number);
            return;
        }

        var ocr = context.TryAttachImage(part, png);
        ImageExtractor.AppendOcr(sb, part, ocr);
    }

    internal static int SafeDpi(double widthPt, double heightPt, long maxPixels)
    {
        var widthIn = Math.Max(widthPt, 1) / 72.0;
        var heightIn = Math.Max(heightPt, 1) / 72.0;
        var area = widthIn * heightIn;
        if (area <= 0)
        {
            return 72;
        }

        var maxDpi = Math.Sqrt(maxPixels / area);
        var dpi = (int)Math.Floor(Math.Min(300, maxDpi));
        return dpi < 36 ? 0 : dpi;
    }

    private static bool AppendTables(PdfDocument document, int pageNumber, StringBuilder sb, PartDraft part)
    {
        try
        {
            var area = ObjectExtractor.Extract(document, pageNumber);
            var tables = new SpreadsheetExtractionAlgorithm().Extract(area).Where(IsLatticeTable).ToList();
            var low = tables.Any(IsSparse);
            if (tables.Count == 0)
            {
                tables = new BasicExtractionAlgorithm().Extract(area).Where(IsStreamTable).ToList();
                low = tables.Count > 0;
            }

            if (tables.Count == 0)
            {
                return false;
            }

            if (low)
            {
                part.Warn(WarningCodes.TableLowConfidence, "page " + pageNumber);
            }

            sb.Append("### Tables (page ").Append(pageNumber).Append(")\n\n");
            foreach (var table in tables)
            {
                var rows = new List<List<string>>();
                foreach (var row in table.Rows)
                {
                    rows.Add(row.Select(cell => cell.GetText().Replace('\r', ' ').Trim()).ToList());
                }

                sb.Append(MarkdownText.Table(rows)).Append('\n');
            }

            return true;
        }
        catch (Exception)
        {
            part.Warn(WarningCodes.TableLowConfidence, "page " + pageNumber + " extraction failed");
            return false;
        }
    }

    private static bool IsLatticeTable(Table table) =>
        table.RowCount >= 2
        && table.ColumnCount >= 2
        && table.Rows.Any(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell.GetText())));

    private static bool IsStreamTable(Table table) =>
        table.RowCount >= 3
        && table.ColumnCount >= 3
        && !IsSparse(table)
        && table.Rows.Any(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell.GetText())));

    private static bool IsSparse(Table table)
    {
        var cells = table.RowCount * table.ColumnCount;
        if (cells <= 0)
        {
            return true;
        }

        var empty = 0;
        foreach (var row in table.Rows)
        {
            foreach (var cell in row)
            {
                if (string.IsNullOrWhiteSpace(cell.GetText()))
                {
                    empty++;
                }
            }
        }

        return empty * 2 > cells;
    }
}
