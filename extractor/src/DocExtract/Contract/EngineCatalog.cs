namespace DocExtract.Contract;

public static class EngineCatalog
{
    public const string Version = "1.0.0";
    public const int SchemaVersion = 1;

    public const string PdfPig = "PdfPig 0.1.16";
    public const string Tabula = "Tabula 1.0.1";
    public const string PdfToImage = "PDFtoImage 5.4.0";
    public const string Tesseract = "Tesseract 5.2.0";
    public const string OpenXml = "DocumentFormat.OpenXml 3.5.1";
    public const string MimeKit = "MimeKit 4.18.1";
    public const string MsgReader = "MsgReader 6.1.2";
    public const string SkiaSharp = "SkiaSharp 4.152.1";

    public static IReadOnlyDictionary<string, string> Engines { get; } = new Dictionary<string, string>
    {
        ["pdf"] = PdfPig,
        ["tables"] = Tabula,
        ["pdfRaster"] = PdfToImage,
        ["ocr"] = Tesseract,
        ["office"] = OpenXml,
        ["eml"] = MimeKit,
        ["msg"] = MsgReader,
        ["images"] = SkiaSharp,
        ["zip"] = "System.IO.Compression",
        ["text"] = "DocExtract",
    };
}
