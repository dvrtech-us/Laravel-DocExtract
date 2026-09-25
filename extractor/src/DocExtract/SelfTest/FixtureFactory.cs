using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using MimeKit;
using OpenMcdf;
using MimeKit.Text;
using SkiaSharp;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DocExtract.SelfTest;

public static class FixtureFactory
{
    public const string TextToken = "SELFTEST-TEXT-ALPHA";
    public const string PdfToken = "SELFTEST-PDF-BETA";
    public const string DocxToken = "SELFTEST-DOCX-GAMMA";
    public const string XlsxToken = "SELFTEST-XLSX-DELTA";
    public const string XlsxHiddenToken = "SELFTEST-XLSX-HIDDEN";
    public const string PptxToken = "SELFTEST-PPTX-EPSILON";
    public const string EmlToken = "SELFTEST-EML-ZETA";
    public const string EmlAttachmentToken = "SELFTEST-EML-ATT";
    public const string ZipToken = "SELFTEST-ZIP-ETA";
    public const string MsgToken = "SELFTEST-MSG-THETA";
    public const string MsgSubject = "SELFTEST-MSG-SUBJECT";
    public const string XlsxSharedToken = "SELFTEST-XLSX-SHARED";

    public static byte[] Text() => Encoding.UTF8.GetBytes(TextToken + "\nline two\n");

    public static byte[] Png()
    {
        using var bitmap = new SKBitmap(48, 32, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        canvas.DrawRect(new SKRect(6, 6, 42, 26), paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static byte[] Pdf() => PdfWithPages(PdfToken);

    public static byte[] PdfWithPages(params string[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var line in lines)
        {
            var page = builder.AddPage(PageSize.A4);
            page.AddText(line, 12, new PdfPoint(72, 720), font);
        }

        return builder.Build();
    }

    public static byte[] Docx()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body(
                new W.Paragraph(new W.Run(new W.Text(DocxToken))),
                new W.Table(
                    new W.TableRow(
                        new W.TableCell(new W.Paragraph(new W.Run(new W.Text("Alpha")))),
                        new W.TableCell(new W.Paragraph(new W.Run(new W.Text("Beta"))))),
                    new W.TableRow(
                        new W.TableCell(new W.Paragraph(new W.Run(new W.Text("One")))),
                        new W.TableCell(new W.Paragraph(new W.Run(new W.Text("Two"))))))));
            main.Document.Save();
        }

        return stream.ToArray();
    }

    public static byte[] Xlsx()
    {
        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = doc.AddWorkbookPart();
            workbook.Workbook = new S.Workbook();
            var sheets = workbook.Workbook.AppendChild(new S.Sheets());

            var shared = workbook.AddNewPart<SharedStringTablePart>();
            shared.SharedStringTable = new S.SharedStringTable(
                new S.SharedStringItem(new S.Text(XlsxSharedToken)));
            var visible = workbook.AddNewPart<WorksheetPart>();
            visible.Worksheet = new S.Worksheet(new S.SheetData(
                new S.Row(
                    InlineCell("A1", XlsxToken),
                    new S.Cell
                    {
                        CellReference = "B1",
                        CellFormula = new S.CellFormula("1+1"),
                        CellValue = new S.CellValue("2"),
                        DataType = S.CellValues.Number,
                    },
                    new S.Cell
                    {
                        CellReference = "C1",
                        DataType = S.CellValues.SharedString,
                        CellValue = new S.CellValue("0"),
                    })
                { RowIndex = 1 }));
            visible.Worksheet.Save();
            sheets.Append(new S.Sheet
            {
                Id = workbook.GetIdOfPart(visible),
                SheetId = 1,
                Name = "Visible",
            });

            var hidden = workbook.AddNewPart<WorksheetPart>();
            hidden.Worksheet = new S.Worksheet(new S.SheetData(
                new S.Row(InlineCell("A1", XlsxHiddenToken)) { RowIndex = 1 }));
            hidden.Worksheet.Save();
            sheets.Append(new S.Sheet
            {
                Id = workbook.GetIdOfPart(hidden),
                SheetId = 2,
                Name = "Hidden",
                State = S.SheetStateValues.Hidden,
            });
            workbook.Workbook.Save();
        }

        return stream.ToArray();
    }

    public static byte[] Pptx()
    {
        using var stream = new MemoryStream();
        using (var ppt = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var presentationPart = ppt.AddPresentationPart();
            var slidePart = presentationPart.AddNewPart<SlidePart>();
            slidePart.Slide = new P.Slide(
                new P.CommonSlideData(
                    new P.ShapeTree(
                        new P.NonVisualGroupShapeProperties(
                            new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                            new P.NonVisualGroupShapeDrawingProperties(),
                            new P.ApplicationNonVisualDrawingProperties()),
                        new P.GroupShapeProperties(new A.TransformGroup()),
                        new P.Shape(
                            new P.NonVisualShapeProperties(
                                new P.NonVisualDrawingProperties { Id = 2U, Name = "Title" },
                                new P.NonVisualShapeDrawingProperties(new A.ShapeProperties()),
                                new P.ApplicationNonVisualDrawingProperties()),
                            new P.ShapeProperties(),
                            new P.TextBody(
                                new A.BodyProperties(),
                                new A.ListStyle(),
                                new A.Paragraph(new A.Run(new A.Text(PptxToken))))))),
                new P.ColorMapOverride(new A.MasterColorMapping()));
            slidePart.Slide.Save();
            presentationPart.Presentation = new P.Presentation(
                new P.SlideIdList(
                    new P.SlideId { Id = 256U, RelationshipId = presentationPart.GetIdOfPart(slidePart) }));
            presentationPart.Presentation.Save();
        }

        return stream.ToArray();
    }

    public static byte[] Eml()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Self Test", "selftest@example.com"));
        message.To.Add(new MailboxAddress("Reader", "reader@example.com"));
        message.Subject = "SELFTEST-EML-SUBJECT";
        var body = new TextPart(TextFormat.Plain) { Text = EmlToken };
        var attachment = new MimePart("text", "plain")
        {
            Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes(EmlAttachmentToken))),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            ContentTransferEncoding = ContentEncoding.Base64,
            FileName = "note.txt",
        };
        message.Body = new Multipart("mixed") { body, attachment };
        using var stream = new MemoryStream();
        message.WriteTo(stream);
        return stream.ToArray();
    }

    public static byte[] Zip()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("inner.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(ZipToken);
        }

        return stream.ToArray();
    }

    public static byte[] Html() => Encoding.UTF8.GetBytes("<html><body><p>Hello <b>HTML</b></p><script>ignore()</script></body></html>");

    public static byte[] ProsePdf()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        page.AddText("Alpha beta gamma delta epsilon zeta eta theta", 12, new PdfPoint(72, 720), font);
        page.AddText("The quick brown fox jumps over the lazy dog", 12, new PdfPoint(72, 700), font);
        page.AddText("Pack my box with five dozen liquor jugs now", 12, new PdfPoint(72, 680), font);
        page.AddText("How vexingly quick daft zebras jump today", 12, new PdfPoint(72, 660), font);
        return builder.Build();
    }

    public static byte[] Msg()
    {
        using var stream = new MemoryStream();
        using (var root = RootStorage.Create(stream, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
        {
            WriteStream(root, "__substg1.0_001A001F", Utf16("IPM.Note"));
            WriteStream(root, "__substg1.0_0037001F", Utf16(MsgSubject));
            WriteStream(root, "__substg1.0_1000001F", Utf16(MsgToken));
            WriteStream(root, "__nameid_version1.0", new byte[16]);
            WriteStream(root, "__properties_version1.0", PropertyStream(
                (0x001A001F, Utf16("IPM.Note").Length),
                (0x0037001F, Utf16(MsgSubject).Length),
                (0x1000001F, Utf16(MsgToken).Length)));
        }

        return stream.ToArray();
    }

    private static byte[] Utf16(string value) => Encoding.Unicode.GetBytes(value + "\0");

    private static byte[] PropertyStream(params (uint Tag, int Size)[] properties)
    {
        var bytes = new byte[32 + (properties.Length * 16)];
        var offset = 32;
        foreach (var property in properties)
        {
            BitConverter.GetBytes(property.Tag).CopyTo(bytes, offset);
            BitConverter.GetBytes(property.Size).CopyTo(bytes, offset + 8);
            offset += 16;
        }

        return bytes;
    }

    private static void WriteStream(Storage storage, string name, byte[] data)
    {
        using var entry = storage.CreateStream(name);
        entry.Write(data, 0, data.Length);
    }

    private static S.Cell InlineCell(string reference, string text) => new()
    {
        CellReference = reference,
        DataType = S.CellValues.InlineString,
        InlineString = new S.InlineString(new S.Text(text)),
    };
}
