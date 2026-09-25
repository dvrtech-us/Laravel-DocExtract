using System.IO.Compression;
using System.Text;
using DocExtract.Contract;
using DocExtract.Extraction;
using DocExtract.Limits;
using DocExtract.Output;
using DocExtract.SelfTest;

namespace DocExtract.Tests;

public class ExtractionSnapshotTests
{
    [Fact]
    public void Text_StartsWithHeadingAndBody()
    {
        var result = TestSupport.Extract(FixtureFactory.Text(), "note.txt");
        Assert.Equal("ok", result.Status);
        Assert.Equal("text", result.Parts[0].Kind);
        Assert.Equal("text/plain", result.Parts[0].Mime);
        Assert.StartsWith("## note.txt\n", result.Parts[0].Markdown);
        Assert.Contains(FixtureFactory.TextToken, result.Parts[0].Markdown);
        Assert.Equal(0, result.Parts[0].CharStart);
        Assert.Equal(result.Parts[0].Markdown.Length, result.Parts[0].CharEnd);
        Assert.Equal(result.TotalChars, result.Parts[0].CharEnd);
    }

    [Fact]
    public void Html_BecomesPlainText()
    {
        var markdown = TestSupport.Markdown(TestSupport.Extract(FixtureFactory.Html(), "page.html"));
        Assert.Contains("Hello HTML", markdown);
        Assert.DoesNotContain("ignore()", markdown);
        Assert.DoesNotContain("<b>", markdown);
    }

    [Fact]
    public void Png_ReturnsNormalizedImageAndOcrText()
    {
        var ocr = new ScriptedOcr();
        var result = TestSupport.Extract(FixtureFactory.Png(), "pixel.png", ocr: ocr);
        Assert.Equal("image", result.Parts[0].Kind);
        Assert.Equal("skiasharp+tesseract", result.Parts[0].Engine);
        Assert.Contains("img-0", result.Parts[0].ImageIds);
        Assert.Contains("### OCR text (img-0)", result.Parts[0].Markdown);
        Assert.Contains("FAKE-OCR", result.Parts[0].Markdown);
        Assert.Contains(result.Parts[0].Warnings, w => w.Code == WarningCodes.OcrUsed);
        Assert.Equal(1, ocr.Calls);
        Assert.Equal(8, result.Images[0].OcrChars);
        var written = TestSupport.WriteResult(result);
        Assert.Equal("images/img-0.png", written.Images[0].File);
        Assert.Equal("parts/0.md", written.Parts[0].MarkdownFile);
    }

    [Fact]
    public void Png_MissingOcrDoesNotFail()
    {
        var result = TestSupport.Extract(FixtureFactory.Png(), "pixel.png");
        Assert.Equal("partial", result.Status);
        Assert.Contains(result.Parts[0].Warnings, w => w.Code == WarningCodes.OcrUnavailable);
        Assert.NotEmpty(result.Images);
        Assert.DoesNotContain("### OCR text", result.Parts[0].Markdown);
    }

    [Fact]
    public void Pdf_ExtractsTextLayer()
    {
        var result = TestSupport.Extract(FixtureFactory.Pdf(), "report.pdf");
        Assert.Contains(FixtureFactory.PdfToken, result.Parts[0].Markdown);
        Assert.StartsWith("## report.pdf\n", result.Parts[0].Markdown);
        Assert.Equal("pdf", result.Parts[0].Kind);
        Assert.Equal(1, result.Parts[0].Pages);
        Assert.Contains("pdfpig", result.Parts[0].Engine);
        Assert.Equal("application/pdf", result.Parts[0].Mime);
    }

    [Fact]
    public void Docx_ExtractsParagraphAndTable()
    {
        var markdown = TestSupport.Markdown(TestSupport.Extract(FixtureFactory.Docx(), "doc.docx"));
        Assert.Contains(FixtureFactory.DocxToken, markdown);
        Assert.Contains("Alpha", markdown);
        Assert.Contains("| Alpha | Beta |", markdown);
    }

    [Fact]
    public void Xlsx_UsesCachedValuesAndMarksHiddenSheets()
    {
        var result = TestSupport.Extract(FixtureFactory.Xlsx(), "book.xlsx");
        var markdown = TestSupport.Markdown(result);
        Assert.Contains(FixtureFactory.XlsxToken, markdown);
        Assert.Contains(FixtureFactory.XlsxSharedToken, markdown);
        Assert.Contains(FixtureFactory.XlsxHiddenToken, markdown);
        Assert.Contains("*(hidden)*", markdown);
        Assert.Contains("2", markdown);
        Assert.Equal(2, result.Parts[0].Pages);
        Assert.Contains(result.Parts[0].Warnings, w => w.Code == WarningCodes.FormulasCachedValues);
        Assert.Contains(result.Parts[0].Warnings, w => w.Code == WarningCodes.HiddenContent);
        Assert.Equal("openxml", result.Parts[0].Engine);
    }

    [Fact]
    public void Pptx_ExtractsSlideText()
    {
        var result = TestSupport.Extract(FixtureFactory.Pptx(), "deck.pptx");
        Assert.Contains(FixtureFactory.PptxToken, result.Parts[0].Markdown);
        Assert.Equal(1, result.Parts[0].Pages);
        Assert.Contains("### Slide 1", result.Parts[0].Markdown);
    }

    [Fact]
    public void Eml_ListsHeadersAndExtractsAttachment()
    {
        var result = TestSupport.Extract(FixtureFactory.Eml(), "mail.eml");
        var markdown = TestSupport.Markdown(result);
        Assert.Contains("SELFTEST-EML-SUBJECT", markdown);
        Assert.Contains(FixtureFactory.EmlToken, markdown);
        Assert.Contains(FixtureFactory.EmlAttachmentToken, markdown);
        Assert.Equal("eml", result.Parts[0].Kind);
        Assert.Contains(result.Parts, p => p.Kind == "text" && p.Path == "mail.eml/note.txt");
        Assert.True(result.Parts[1].Depth == 1);
    }

    [Fact]
    public void Zip_ExtractsChildInDepthFirstOrder()
    {
        var result = TestSupport.Extract(FixtureFactory.Zip(), "bundle.zip");
        Assert.Equal("zip", result.Parts[0].Kind);
        Assert.Contains("inner.txt", result.Parts[0].Markdown);
        Assert.Equal("bundle.zip/inner.txt", result.Parts[1].Path);
        Assert.Contains(FixtureFactory.ZipToken, result.Parts[1].Markdown);
        Assert.True(result.Parts[1].CharStart >= result.Parts[0].CharEnd - 0);
        Assert.Equal(result.Parts[0].CharEnd, result.Parts[1].CharStart);
    }

    [Fact]
    public void CorpusHelloTxt_RoundTrips()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "corpus", "hello.txt");
        if (!File.Exists(path))
        {
            path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "corpus", "hello.txt"));
        }

        var result = TestSupport.Extract(File.ReadAllBytes(path), "hello.txt");
        Assert.Contains("synthetic corpus", result.Parts[0].Markdown);
    }
}

public class LimitTests
{
    [Theory]
    [InlineData(1000, 10, 100, false)]
    [InlineData(1001, 10, 100, true)]
    [InlineData(50, 0, 100, true)]
    [InlineData(0, 0, 100, false)]
    public void ZipRatio_Boundary(long uncompressed, long compressed, double max, bool exceeds)
    {
        Assert.Equal(exceeds, LimitBudget.ExceedsZipRatio(uncompressed, compressed, max));
    }

    [Fact]
    public void ZipBomb_HighlyCompressibleEntry_StopsBeforeDecompression()
    {
        var payload = new byte[2 * 1024 * 1024];
        var zip = TestSupport.ZipWith(("bomb.txt", payload, CompressionLevel.Optimal));
        var budget = new LimitBudget();
        var ex = Assert.Throws<ExtractionException>(() => TestSupport.Extract(zip, "bomb.zip", budget));
        Assert.Equal(ExitCodes.Limits, ex.ExitCode);
        Assert.Equal(ErrorCodes.LimitZipRatio, ex.ErrorCode);
        Assert.Equal(0, budget.DecompressedUsed);
    }

    [Fact]
    public void DecompressedBudget_RejectsBeforeReadingOversizedStoredEntry()
    {
        var payload = new byte[4000];
        Random.Shared.NextBytes(payload);
        var zip = TestSupport.ZipWith(("data.bin", payload, CompressionLevel.NoCompression));
        var budget = new LimitBudget { MaxDecompressedBytes = 100 };
        var ex = Assert.Throws<ExtractionException>(() => TestSupport.Extract(zip, "data.zip", budget));
        Assert.Equal(ErrorCodes.LimitDecompressed, ex.ErrorCode);
        Assert.Equal(0, budget.DecompressedUsed);
    }

    [Fact]
    public void StoredEntry_ChargesDecompressedBytes()
    {
        var zip = TestSupport.ZipWith(("inner.txt", Encoding.UTF8.GetBytes("hello"), CompressionLevel.NoCompression));
        var budget = new LimitBudget();
        var result = TestSupport.Extract(zip, "ok.zip", budget);
        Assert.Contains("hello", TestSupport.Markdown(result));
        Assert.Equal(5, budget.DecompressedUsed);
    }

    [Fact]
    public void DepthLimit_SkipsGrandchildren()
    {
        var inner = TestSupport.ZipWith(("b.txt", Encoding.UTF8.GetBytes("DEEP-B"), CompressionLevel.NoCompression));
        var outer = TestSupport.ZipWith(
            ("a.txt", Encoding.UTF8.GetBytes("DEEP-A"), CompressionLevel.NoCompression),
            ("nested.zip", inner, CompressionLevel.NoCompression));
        var budget = new LimitBudget { MaxDepth = 1 };
        var result = TestSupport.Extract(outer, "outer.zip", budget);
        var markdown = TestSupport.Markdown(result);
        Assert.Contains("DEEP-A", markdown);
        Assert.DoesNotContain("DEEP-B", markdown);
        Assert.Contains(result.Parts, p => p.Warnings.Any(w => w.Code == WarningCodes.DepthLimit));
        Assert.Equal("partial", result.Status);
    }

    [Fact]
    public void PartCount_StopsWithChildLimit()
    {
        var zip = TestSupport.ZipWith(
            ("a.txt", Encoding.UTF8.GetBytes("A"), CompressionLevel.NoCompression),
            ("b.txt", Encoding.UTF8.GetBytes("B"), CompressionLevel.NoCompression),
            ("c.txt", Encoding.UTF8.GetBytes("C"), CompressionLevel.NoCompression));
        var budget = new LimitBudget { MaxParts = 2 };
        var result = TestSupport.Extract(zip, "many.zip", budget);
        Assert.Equal(2, result.Parts.Count);
        Assert.Contains(result.Parts[0].Warnings, w => w.Code == WarningCodes.ChildLimit);
        Assert.Contains("A", TestSupport.Markdown(result));
        Assert.DoesNotContain("\nC\n", "\n" + TestSupport.Markdown(result));
    }

    [Fact]
    public void PixelBomb_RejectsCraftedPngHeader()
    {
        var png = TestSupport.CraftPng(100_000, 100_000);
        var started = DateTime.UtcNow;
        var ex = Assert.Throws<ExtractionException>(() => TestSupport.Extract(png, "bomb.png"));
        Assert.Equal(ErrorCodes.LimitPixels, ex.ErrorCode);
        Assert.Equal(ExitCodes.Limits, ex.ExitCode);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void OutputTruncation_DoesNotSplitSurrogatePair()
    {
        var heading = "## trunc.txt\n\n";
        var prefix = "HELLO ";
        var body = prefix + "\U0001F600" + "TAIL";
        var cut = heading.Length + prefix.Length + 1;
        var budget = new LimitBudget { MaxOutputChars = cut };
        var result = TestSupport.Extract(Encoding.UTF8.GetBytes(body), "trunc.txt", budget);
        var markdown = result.Parts[0].Markdown;
        Assert.True(result.Truncated);
        Assert.Contains(result.Parts[0].Warnings, w => w.Code == WarningCodes.Truncated);
        Assert.Contains("HELLO", markdown);
        Assert.DoesNotContain("TAIL", markdown);
        Assert.True(markdown.Length <= cut);
        Assert.True(markdown.Length == 0 || !char.IsHighSurrogate(markdown[^1]));
        Assert.Equal("partial", result.Status);
    }

    [Fact]
    public void PdfPageCap_StopsAfterBudget()
    {
        var pdf = FixtureFactory.PdfWithPages("PAGE-ONE", "PAGE-TWO");
        var budget = new LimitBudget { MaxPdfPages = 1 };
        var result = TestSupport.Extract(pdf, "pages.pdf", budget);
        var markdown = TestSupport.Markdown(result);
        Assert.Contains("PAGE-ONE", markdown);
        Assert.DoesNotContain("PAGE-TWO", markdown);
        Assert.Contains(result.Parts[0].Warnings, w => w.Code == WarningCodes.Truncated && w.Detail.Contains("pdf page cap"));
    }

    [Fact]
    public void ZipSlip_DoesNotEscapeOutputDirectory()
    {
        var zip = TestSupport.RawStoredZip("../escape.txt", Encoding.UTF8.GetBytes("SECRET-SLIP"));
        var root = Path.Combine(Path.GetTempPath(), "docextract-slip-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            var result = TestSupport.Extract(zip, "slip.zip");
            Assert.Contains(result.Parts[0].Warnings, w => w.Code == WarningCodes.PartFailed);
            Assert.DoesNotContain("SECRET-SLIP", TestSupport.Markdown(result));
            ResultWriter.Write(root, result);
            Assert.False(File.Exists(Path.Combine(Directory.GetParent(root)!.FullName, "escape.txt")));
            Assert.DoesNotContain("escape", string.Join('\n', Directory.GetFiles(root, "*", SearchOption.AllDirectories)), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Utf16Truncate_BacksUpOffHighSurrogate()
    {
        var text = "A\U0001F600B";
        Assert.Equal("A", Utf16Units.Truncate(text, 2));
        Assert.Equal(text, Utf16Units.Truncate(text, 4));
    }
}

public class FuzzTests
{
    [Fact]
    public void EmptyInput_IsCorrupt()
    {
        var ex = Assert.Throws<ExtractionException>(() => TestSupport.Extract([], "empty.bin"));
        Assert.Equal(ExitCodes.CorruptOrEncrypted, ex.ExitCode);
        Assert.Equal(ErrorCodes.Corrupt, ex.ErrorCode);
    }

    [Fact]
    public void RandomBytes_AreUnsupported()
    {
        var ex = Assert.Throws<ExtractionException>(() => TestSupport.Extract([1, 2, 3, 4, 5, 6, 7, 8, 9], "noise.bin"));
        Assert.Equal(ExitCodes.Unsupported, ex.ExitCode);
        Assert.Equal(ErrorCodes.Unsupported, ex.ErrorCode);
    }

    [Fact]
    public void TruncatedPdf_IsCorrupt()
    {
        var corpus = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "corpus", "truncated.pdf"));
        var bytes = File.Exists(corpus) ? File.ReadAllBytes(corpus) : Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF\n");
        var ex = Assert.Throws<ExtractionException>(() => TestSupport.Extract(bytes, "truncated.pdf"));
        Assert.Equal(ExitCodes.CorruptOrEncrypted, ex.ExitCode);
        Assert.Equal(ErrorCodes.Corrupt, ex.ErrorCode);
    }

    [Fact]
    public void OversizedInput_HitsInputCap()
    {
        var budget = new LimitBudget { MaxInputBytes = 32 };
        var ex = Assert.Throws<ExtractionException>(() => TestSupport.Extract(new byte[64], "big.txt", budget));
        Assert.Equal(ErrorCodes.LimitInputSize, ex.ErrorCode);
    }
}

public class CliTests
{
    [Fact]
    public void Version_PrintsEngineJson()
    {
        var (code, stdout, stderr) = TestSupport.RunCli("version");
        Assert.Equal(0, code);
        Assert.Equal("", stderr.Trim());
        Assert.Contains("\"version\": \"1.0.0\"", stdout);
        Assert.Contains("PdfPig 0.1.16", stdout);
        Assert.Contains("Tabula 1.0.1", stdout);
        Assert.Contains("Tesseract 5.2.0", stdout);
    }

    [Fact]
    public void SelfTest_ExitsZero()
    {
        var (code, stdout, stderr) = TestSupport.RunCli("self-test");
        Assert.Equal(0, code);
        Assert.Equal("", stderr.Trim());
        Assert.Contains("\"passed\": true", stdout);
    }

    [Fact]
    public void Extract_WritesResultAndKeepsStderrToCodes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "docextract-cli-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var input = Path.Combine(dir, "note.txt");
            File.WriteAllText(input, "hello contract");
            var output = Path.Combine(dir, "out");
            var (code, stdout, stderr) = TestSupport.RunCli("extract", "--input", input, "--output-dir", output, "--file-name", "note.txt");
            Assert.Equal(0, code);
            Assert.Equal("", stderr.Trim());
            if (!OperatingSystem.IsWindows())
            {
                Assert.Contains("W_SANDBOX_UNAVAILABLE", stdout);
            }

            var json = File.ReadAllText(Path.Combine(output, "result.json"));
            Assert.Contains("\"schemaVersion\": 1", json);
            Assert.Contains("hello contract", File.ReadAllText(Path.Combine(output, "parts", "0.md")));
            Assert.DoesNotContain(input, stderr);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Usage_IsAnErrorCode()
    {
        var (code, _, stderr) = TestSupport.RunCli();
        Assert.Equal(1, code);
        Assert.Equal("E_USAGE", stderr.Trim());
    }
}

public class SandboxLimitTests
{
    [Fact]
    public void JobLimits_SetKillMemoryCpuAndSingleProcess()
    {
        var limits = Sandbox.JobLimits.Build(1024, 90);
        Assert.Equal(1u, limits.ActiveProcessLimit);
        Assert.Equal(1024UL * 1024UL * 1024UL, limits.ProcessMemoryBytes);
        Assert.Equal(90L * 10_000_000L, limits.JobCpu100Ns);
        Assert.Equal(
            Sandbox.JobLimits.KillOnJobClose | Sandbox.JobLimits.ProcessMemory | Sandbox.JobLimits.JobTime | Sandbox.JobLimits.ActiveProcess,
            limits.Flags);
        Assert.Equal(OperatingSystem.IsWindows(), Sandbox.WindowsJobSandbox.IsSupported);
    }
}
