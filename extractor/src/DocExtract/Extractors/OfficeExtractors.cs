using System.Diagnostics;
using System.Text;
using DocExtract.Contract;
using DocExtract.Extraction;
using DocExtract.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DocExtract.Extractors;

public static class OfficeLimits
{
    public const int MaxCharactersInPart = 10_000_000;
    public const int MaxRowsPerSheet = 10_000;
    public const int MaxColumnsPerSheet = 256;

    public static OpenSettings Settings() => new() { MaxCharactersInPart = MaxCharactersInPart };
}

public sealed class DocxExtractor : IExtractor
{
    public string Kind => "docx";

    public void Extract(ExtractionInput input, ExtractionContext context)
    {
        var watch = Stopwatch.StartNew();
        var part = context.AddPart(input.LogicalPath, input.Depth, "docx", input.Mime, input.Data.LongLength, "openxml");
        try
        {
            using var stream = new MemoryStream(input.Data, writable: false);
            using var doc = WordprocessingDocument.Open(stream, false, OfficeLimits.Settings());
            var sb = new StringBuilder();
            sb.Append(MarkdownText.Heading(input.LogicalPath));
            var main = doc.MainDocumentPart;
            if (main?.Document?.Body is not null)
            {
                AppendContainer(sb, main.Document.Body, context);
            }

            if (main is not null)
            {
                foreach (var header in main.HeaderParts)
                {
                    AppendContainer(sb, header.Header, context);
                }

                foreach (var footer in main.FooterParts)
                {
                    AppendContainer(sb, footer.Footer, context);
                }

                if (main.FootnotesPart?.Footnotes is { } footnotes)
                {
                    foreach (var note in footnotes.Elements<W.Footnote>())
                    {
                        var noteType = note.Type?.Value;
                        if (noteType == W.FootnoteEndnoteValues.Separator
                            || noteType == W.FootnoteEndnoteValues.ContinuationSeparator
                            || noteType == W.FootnoteEndnoteValues.ContinuationNotice)
                        {
                            continue;
                        }

                        AppendContainer(sb, note, context);
                    }
                }
            }

            part.Markdown = context.Take(sb.ToString(), part);
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

    private static void AppendContainer(StringBuilder sb, OpenXmlElement? container, ExtractionContext context)
    {
        if (container is null)
        {
            return;
        }

        foreach (var element in container.Elements())
        {
            context.Cancellation.ThrowIfCancellationRequested();
            AppendElement(sb, element, context);
        }
    }

    private static void AppendElement(StringBuilder sb, OpenXmlElement element, ExtractionContext context)
    {
        switch (element)
        {
            case W.Paragraph paragraph:
                AppendParagraph(sb, paragraph);
                break;
            case W.Table table:
                AppendTable(sb, table);
                break;
            case W.SdtBlock sdt:
                AppendContainer(sb, sdt.SdtContentBlock, context);
                break;
            case W.SdtCell sdtCell:
                AppendContainer(sb, sdtCell.SdtContentCell, context);
                break;
            case W.SdtRow sdtRow:
                AppendContainer(sb, sdtRow.SdtContentRow, context);
                break;
        }
    }

    private static void AppendParagraph(StringBuilder sb, W.Paragraph paragraph)
    {
        var text = string.Concat(paragraph.Descendants<W.Text>().Select(t => t.Text)).Trim();
        if (text.Length == 0)
        {
            return;
        }

        var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
        if (style.Equals("Title", StringComparison.OrdinalIgnoreCase)
            || style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
        {
            var level = 3;
            if (style.Length > "Heading".Length && int.TryParse(style["Heading".Length..], out var parsed))
            {
                level = Math.Clamp(parsed + 2, 3, 6);
            }

            sb.Append(new string('#', level)).Append(' ').Append(text).Append("\n\n");
            return;
        }

        sb.Append(text).Append("\n\n");
    }

    private static void AppendTable(StringBuilder sb, W.Table table)
    {
        var rows = new List<List<string>>();
        foreach (var row in table.Elements<W.TableRow>())
        {
            var cells = new List<string>();
            foreach (var cell in row.Elements<W.TableCell>())
            {
                cells.Add(string.Concat(cell.Descendants<W.Text>().Select(t => t.Text)));
            }

            rows.Add(cells);
        }

        if (rows.Count > 0)
        {
            sb.Append(MarkdownText.Table(rows)).Append('\n');
        }
    }
}

public sealed class XlsxExtractor : IExtractor
{
    public string Kind => "xlsx";

    public void Extract(ExtractionInput input, ExtractionContext context)
    {
        var watch = Stopwatch.StartNew();
        var part = context.AddPart(input.LogicalPath, input.Depth, "xlsx", input.Mime, input.Data.LongLength, "openxml");
        try
        {
            using var stream = new MemoryStream(input.Data, writable: false);
            using var doc = SpreadsheetDocument.Open(stream, false, OfficeLimits.Settings());
            var workbookPart = doc.WorkbookPart ?? throw ExtractionException.Corrupt();
            var workbook = workbookPart.Workbook ?? throw ExtractionException.Corrupt();
            var sheets = workbook.Sheets?.Elements<S.Sheet>().ToList() ?? [];
            part.Pages = sheets.Count;
            var shared = MaterializeSharedStrings(workbookPart.SharedStringTablePart?.SharedStringTable);
            var sb = new StringBuilder();
            sb.Append(MarkdownText.Heading(input.LogicalPath));
            var sawFormula = false;
            foreach (var sheet in sheets)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                var state = sheet.State?.Value;
                var hidden = state == S.SheetStateValues.Hidden || state == S.SheetStateValues.VeryHidden;
                var name = sheet.Name?.Value ?? "Sheet";
                if (hidden)
                {
                    part.Warn(WarningCodes.HiddenContent, name);
                }

                sb.Append("### Sheet: ").Append(name).Append("\n\n");
                if (hidden)
                {
                    sb.Append("*(hidden)*\n\n");
                }

                if (sheet.Id?.Value is null)
                {
                    continue;
                }

                var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id.Value);
                var worksheet = worksheetPart.Worksheet ?? throw ExtractionException.Corrupt();
                var matrix = new List<List<string>>();
                var rowNumber = 0;
                foreach (var row in worksheet.Descendants<S.Row>())
                {
                    if (rowNumber >= OfficeLimits.MaxRowsPerSheet)
                    {
                        part.Warn(WarningCodes.Truncated, "sheet row cap");
                        break;
                    }

                    rowNumber++;
                    var map = new SortedDictionary<int, string>();
                    foreach (var cell in row.Elements<S.Cell>())
                    {
                        if (cell.CellFormula is not null)
                        {
                            sawFormula = true;
                        }

                        var column = ColumnIndex(cell.CellReference?.Value);
                        if (column < 0)
                        {
                            column = map.Count == 0 ? 0 : map.Keys.Max() + 1;
                        }

                        if (column >= OfficeLimits.MaxColumnsPerSheet)
                        {
                            part.Warn(WarningCodes.Truncated, "sheet column cap");
                            continue;
                        }

                        map[column] = CellText(cell, shared);
                    }

                    if (map.Count == 0)
                    {
                        continue;
                    }

                    var width = Math.Min(OfficeLimits.MaxColumnsPerSheet, map.Keys.Max() + 1);
                    var cells = new List<string>(width);
                    for (var c = 0; c < width; c++)
                    {
                        cells.Add(map.TryGetValue(c, out var value) ? value : "");
                    }

                    matrix.Add(cells);
                }

                if (matrix.Count > 0)
                {
                    sb.Append(MarkdownText.Table(matrix)).Append('\n');
                }
            }

            if (sawFormula)
            {
                part.Warn(WarningCodes.FormulasCachedValues, "values are cached, not recalculated");
            }

            part.Markdown = context.Take(sb.ToString(), part);
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

    internal static int ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return -1;
        }

        var column = 0;
        var seen = false;
        foreach (var ch in reference)
        {
            if (!char.IsLetter(ch))
            {
                break;
            }

            seen = true;
            column = (column * 26) + (char.ToUpperInvariant(ch) - 'A' + 1);
        }

        return seen ? column - 1 : -1;
    }

    internal static List<string> MaterializeSharedStrings(S.SharedStringTable? table)
    {
        var values = new List<string>();
        if (table is null)
        {
            return values;
        }

        foreach (var item in table.Elements<S.SharedStringItem>())
        {
            values.Add(item.InnerText ?? "");
        }

        return values;
    }

    private static string CellText(S.Cell cell, IReadOnlyList<string> shared)
    {
        if (cell.DataType?.Value == S.CellValues.InlineString)
        {
            return cell.InlineString?.InnerText ?? "";
        }

        if (cell.DataType?.Value == S.CellValues.SharedString
            && int.TryParse(cell.CellValue?.Text, out var index)
            && index >= 0
            && index < shared.Count)
        {
            return shared[index];
        }

        if (!string.IsNullOrEmpty(cell.CellValue?.Text))
        {
            return cell.CellValue.Text;
        }

        if (cell.CellFormula is not null)
        {
            return "=" + cell.CellFormula.Text;
        }

        return "";
    }
}

public sealed class PptxExtractor : IExtractor
{
    public string Kind => "pptx";

    public void Extract(ExtractionInput input, ExtractionContext context)
    {
        var watch = Stopwatch.StartNew();
        var part = context.AddPart(input.LogicalPath, input.Depth, "pptx", input.Mime, input.Data.LongLength, "openxml");
        try
        {
            using var stream = new MemoryStream(input.Data, writable: false);
            using var ppt = PresentationDocument.Open(stream, false, OfficeLimits.Settings());
            var presentationPart = ppt.PresentationPart ?? throw ExtractionException.Corrupt();
            var presentation = presentationPart.Presentation ?? throw ExtractionException.Corrupt();
            var slideIds = presentation.SlideIdList?.Elements<DocumentFormat.OpenXml.Presentation.SlideId>().ToList() ?? [];
            part.Pages = slideIds.Count;
            var sb = new StringBuilder();
            sb.Append(MarkdownText.Heading(input.LogicalPath));
            var number = 0;
            foreach (var slideId in slideIds)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                number++;
                if (slideId.RelationshipId?.Value is null)
                {
                    continue;
                }

                var slidePart = (SlidePart)presentationPart.GetPartById(slideId.RelationshipId.Value);
                var slide = slidePart.Slide ?? throw ExtractionException.Corrupt();
                var hidden = slide.Show is not null && slide.Show.HasValue && !slide.Show.Value;
                if (hidden)
                {
                    part.Warn(WarningCodes.HiddenContent, "slide " + number);
                }

                sb.Append("### Slide ").Append(number).Append("\n\n");
                if (hidden)
                {
                    sb.Append("*(hidden)*\n\n");
                }

                foreach (var paragraph in slide.Descendants<A.Paragraph>())
                {
                    var text = string.Concat(paragraph.Descendants<A.Text>().Select(t => t.Text)).Trim();
                    if (text.Length > 0)
                    {
                        sb.Append(text).Append("\n\n");
                    }
                }

                if (slidePart.NotesSlidePart?.NotesSlide is not null)
                {
                    var notes = string.Concat(slidePart.NotesSlidePart.NotesSlide.Descendants<A.Text>().Select(t => t.Text)).Trim();
                    if (notes.Length > 0)
                    {
                        sb.Append("*(notes)* ").Append(notes).Append("\n\n");
                    }
                }
            }

            part.Markdown = context.Take(sb.ToString(), part);
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
}
