using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using DocExtract.Cli;
using DocExtract.Contract;
using DocExtract.Extractors;
using DocExtract.Limits;
using DocExtract.Sandbox;
using DocExtract.SelfTest;
using DocExtract.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DocExtract.Tests;

public class ReviewFixTests
{
    [Fact]
    public void ChildArguments_KeepExtractVerb_ForApphostAndDotnetHost()
    {
        var invocation = new ExtractInvocation
        {
            InputPath = "in.bin",
            OutputDir = "out",
            FileName = "in.bin",
            Child = true,
        };
        var child = CliParser.ToArgs(invocation, child: true);
        Assert.Equal("extract", child[0]);

        var apphost = ChildLaunch.Arguments(@"C:\tools\DocExtract.exe", @"C:\tools\DocExtract.dll", child);
        Assert.Equal("extract", apphost[0]);
        Assert.Contains("--child", apphost);

        var hosted = ChildLaunch.Arguments("/usr/local/share/dotnet/dotnet", "/app/DocExtract.dll", child);
        Assert.Equal(new[] { "exec", "/app/DocExtract.dll", "extract" }, hosted.Take(3));
        Assert.Contains("--child", hosted);
    }

    [Fact]
    public void StderrTail_KeepsOnlyTheLast64Kilobytes()
    {
        var tail = new StderrTail();
        var prefix = Encoding.UTF8.GetBytes(new string('A', 80 * 1024));
        var ending = Encoding.UTF8.GetBytes("\nE_LIMIT_ZIP_RATIO\n");
        tail.Append(prefix);
        tail.Append(ending);
        var text = tail.Text;
        Assert.True(Encoding.UTF8.GetByteCount(text) <= StderrTail.MaxBytes);
        Assert.EndsWith("E_LIMIT_ZIP_RATIO", text.Trim());
        Assert.True(text.Length < prefix.Length);
    }

    [Fact]
    public void ReadEncryptionFlags_BadCentralOffsets_DoNotThrow()
    {
        Assert.Empty(ZipExtractor.ReadEncryptionFlags(Eocd(0xFFFFFFFFu)));
        Assert.Empty(ZipExtractor.ReadEncryptionFlags(Eocd(0x7FFFFFFFu)));
        Assert.Empty(ZipExtractor.ReadEncryptionFlags(Eocd(10_000)));
    }

    [Fact]
    public void ChildExitMapper_MapsQuotaMemoryAndUnknownCodes()
    {
        Assert.Equal((ExitCodes.Limits, ErrorCodes.LimitCpu), ChildExitMapper.Map(1816, ""));
        Assert.Equal((ExitCodes.Limits, ErrorCodes.LimitMemory), ChildExitMapper.Map(0xC0000044, "boom"));
        Assert.Equal((ExitCodes.Limits, ErrorCodes.LimitZipRatio), ChildExitMapper.Map(3, "noise\nE_LIMIT_ZIP_RATIO\n"));
        Assert.Equal((ExitCodes.Failure, ErrorCodes.Failed), ChildExitMapper.Map(3, "not a code"));
        Assert.Equal((ExitCodes.Failure, ErrorCodes.Failed), ChildExitMapper.Map(0xC0000005, "E_LIMIT_ZIP_RATIO"));
        Assert.Equal((ExitCodes.Success, (string?)null), ChildExitMapper.Map(0, "E_FAILED"));
    }

    [Fact]
    public void SmallHighlyCompressibleZip_IsNotAHardRatioFailure()
    {
        var payload = new byte[64 * 1024];
        var zip = TestSupport.ZipWith(("tiny.txt", payload, CompressionLevel.Optimal));
        var result = TestSupport.Extract(zip, "tiny.zip");
        Assert.Contains("tiny.txt", TestSupport.Markdown(result));
    }

    [Fact]
    public void NestedZipBomb_BecomesPartFailure()
    {
        var payload = new byte[2 * 1024 * 1024];
        var inner = TestSupport.ZipWith(("bomb.txt", payload, CompressionLevel.Optimal));
        var outer = TestSupport.ZipWith(("inner.zip", inner, CompressionLevel.NoCompression));
        var result = TestSupport.Extract(outer, "outer.zip");
        Assert.Equal("partial", result.Status);
        Assert.Contains(result.Parts, part => part.Warnings.Any(warning => warning.Code == WarningCodes.PartFailed));
    }

    [Fact]
    public void NestedPixelBomb_IsSkipped()
    {
        var png = TestSupport.CraftPng(100_000, 100_000);
        var zip = TestSupport.ZipWith(("bomb.png", png, CompressionLevel.NoCompression));
        var result = TestSupport.Extract(zip, "imgs.zip");
        Assert.Contains(result.Parts, part => part.Depth > 0 && part.Warnings.Any(warning => warning.Code == WarningCodes.ImageSkipped));
    }

    [Fact]
    public void EncryptedZipEntry_WarnsInsteadOfFailing()
    {
        var zip = TestSupport.RawStoredZip("secret.txt", Encoding.UTF8.GetBytes("SECRET"), encrypted: true);
        var result = TestSupport.Extract(zip, "locked.zip");
        Assert.Contains(result.Parts[0].Warnings, warning => warning.Code == WarningCodes.Encrypted);
        Assert.DoesNotContain("SECRET", TestSupport.Markdown(result));
    }

    [Fact]
    public void EncryptedOfficePackage_IsEncryptedNotUnsupported()
    {
        var bytes = new byte[64];
        bytes[0] = 0xD0; bytes[1] = 0xCF; bytes[2] = 0x11; bytes[3] = 0xE0;
        bytes[4] = 0xA1; bytes[5] = 0xB1; bytes[6] = 0x1A; bytes[7] = 0xE1;
        Encoding.ASCII.GetBytes("EncryptedPackage").CopyTo(bytes, 16);
        var ex = Assert.Throws<ExtractionException>(() => TestSupport.Extract(bytes, "locked.docx"));
        Assert.Equal(ErrorCodes.Encrypted, ex.ErrorCode);
        Assert.Equal(ExitCodes.CorruptOrEncrypted, ex.ExitCode);
    }

    [Fact]
    public void UnclosedScriptTags_DoNotBacktrack()
    {
        var html = string.Concat(Enumerable.Repeat("<script", 20_000));
        var watch = Stopwatch.StartNew();
        var text = HtmlToText.Convert(html + "<p>kept</p>");
        watch.Stop();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), watch.Elapsed.ToString());
        Assert.Contains("kept", text);
    }

    [Fact]
    public void Utf16WithoutBom_DecodesBothEndiannesses()
    {
        Assert.Equal("Hello", TextDecoder.Decode(Encoding.Unicode.GetBytes("Hello")));
        Assert.Equal("Hello", TextDecoder.Decode(Encoding.BigEndianUnicode.GetBytes("Hello")));
    }

    [Fact]
    public void ProsePdf_DoesNotEmitATable()
    {
        var result = TestSupport.Extract(FixtureFactory.ProsePdf(), "prose.pdf");
        var markdown = TestSupport.Markdown(result);
        Assert.Contains("quick brown fox", markdown);
        Assert.DoesNotContain("### Tables", markdown);
    }

    [Fact]
    public void Docx_IncludesContentControlsHeadersAndFootnotes()
    {
        var result = TestSupport.Extract(RichDocx(), "rich.docx");
        var markdown = TestSupport.Markdown(result);
        Assert.Contains("SDT-TOKEN", markdown);
        Assert.Contains("HEADER-TOKEN", markdown);
        Assert.Contains("FOOTNOTE-TOKEN", markdown);
    }

    [Fact]
    public void MsgFixture_ExtractsSubjectAndBody()
    {
        var generated = TestSupport.Extract(FixtureFactory.Msg(), "note.msg");
        Assert.Equal("msg", generated.Parts[0].Kind);
        Assert.Contains(FixtureFactory.MsgToken, generated.Parts[0].Markdown);
        Assert.Contains(FixtureFactory.MsgSubject, generated.Parts[0].Markdown);

        var corpus = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "corpus", "synthetic.msg"));
        if (!File.Exists(corpus))
        {
            corpus = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "corpus", "synthetic.msg"));
        }

        var fromDisk = TestSupport.Extract(File.ReadAllBytes(corpus), "synthetic.msg");
        Assert.Contains(FixtureFactory.MsgToken, fromDisk.Parts[0].Markdown);
    }

    [Fact]
    public void SelfTestRequireOcr_FailsWhenEngineIsMissing()
    {
        var (code, stdout, stderr) = TestSupport.RunCli("self-test", "--require-ocr");
        if (stdout.Contains("\"ocrAvailable\": false", StringComparison.Ordinal))
        {
            Assert.Equal(1, code);
            Assert.Contains("E_SELF_TEST", stderr);
            Assert.Contains("\"kind\": \"ocr\"", stdout);
        }
        else
        {
            Assert.Equal(0, code);
        }
    }

    [Fact]
    public void TestHooks_AreRejectedWithoutTheEnvironmentVariable()
    {
        var previous = Environment.GetEnvironmentVariable(ChildLaunch.TestHooksVariable);
        Environment.SetEnvironmentVariable(ChildLaunch.TestHooksVariable, null);
        try
        {
            var ex = Assert.Throws<ExtractionException>(() => CliParser.Parse(
            [
                "extract", "--input", "a.txt", "--output-dir", "out", "--child-test-hang",
            ]));
            Assert.Equal(ErrorCodes.Usage, ex.ErrorCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ChildLaunch.TestHooksVariable, previous);
        }
    }

    [Fact]
    public void Windows_Apphost_ExtractsThroughTheJobObject()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var exe = FindApphost();
        var dir = Path.Combine(Path.GetTempPath(), "docextract-job-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var input = Path.Combine(dir, "note.txt");
            File.WriteAllText(input, "job-object-ok");
            var output = Path.Combine(dir, "out");
            var (code, _, stderr) = RunExe(exe, "extract", "--input", input, "--output-dir", output, "--file-name", "note.txt", "--timeout-seconds", "30");
            Assert.Equal(0, code);
            Assert.Equal("", stderr.Trim());
            Assert.Contains("job-object-ok", File.ReadAllText(Path.Combine(output, "parts", "0.md")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Windows_JobObject_KillsAHungChildOnTimeout()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var exe = FindApphost();
        var dir = Path.Combine(Path.GetTempPath(), "docextract-hang-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var input = Path.Combine(dir, "note.txt");
            File.WriteAllText(input, "unused");
            var (code, _, stderr) = RunExe(
                exe,
                new Dictionary<string, string> { [ChildLaunch.TestHooksVariable] = "1" },
                "extract", "--input", input, "--output-dir", Path.Combine(dir, "out"),
                "--child-test-hang", "--timeout-seconds", "2", "--memory-mb", "256");
            Assert.Equal(ExitCodes.Timeout, code);
            Assert.Equal(ErrorCodes.Timeout, stderr.Trim());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Windows_JobObject_DrainsAStderrFloodBeforeTimeout()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var exe = FindApphost();
        var dir = Path.Combine(Path.GetTempPath(), "docextract-flood-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var input = Path.Combine(dir, "note.txt");
            File.WriteAllText(input, "unused");
            var started = DateTime.UtcNow;
            var (code, _, stderr) = RunExe(
                exe,
                new Dictionary<string, string> { [ChildLaunch.TestHooksVariable] = "1" },
                "extract", "--input", input, "--output-dir", Path.Combine(dir, "out"),
                "--child-test-flood-stderr", "--timeout-seconds", "8", "--memory-mb", "256");
            Assert.Equal(ExitCodes.Limits, code);
            Assert.Equal(ErrorCodes.LimitZipRatio, stderr.Trim());
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(8));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Windows_JobObject_MemoryLimit_ExitsWithMemoryCode()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var exe = FindApphost();
        var dir = Path.Combine(Path.GetTempPath(), "docextract-mem-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var input = Path.Combine(dir, "note.txt");
            File.WriteAllText(input, "unused");
            var (code, _, stderr) = RunExe(
                exe,
                new Dictionary<string, string> { [ChildLaunch.TestHooksVariable] = "1" },
                "extract", "--input", input, "--output-dir", Path.Combine(dir, "out"),
                "--child-test-allocate", "--memory-mb", "64", "--timeout-seconds", "30");
            Assert.Equal(ExitCodes.Limits, code);
            Assert.Equal(ErrorCodes.LimitMemory, stderr.Trim());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static byte[] Eocd(uint centralOffset)
    {
        var data = new byte[22];
        data[0] = 0x50;
        data[1] = 0x4B;
        data[2] = 0x05;
        data[3] = 0x06;
        BitConverter.GetBytes((ushort)1).CopyTo(data, 10);
        BitConverter.GetBytes(centralOffset).CopyTo(data, 16);
        return data;
    }

    private static string FindApphost()
    {
        var config = typeof(ReviewFixTests).Assembly.Location.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "DocExtract.exe"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "DocExtract", "bin", config, "net10.0", "DocExtract.exe")),
        };
        return candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("DocExtract.exe was not built.");
    }

    private static (int ExitCode, string Stdout, string Stderr) RunExe(string exe, params string[] args) =>
        RunExe(exe, null, args);

    private static (int ExitCode, string Stdout, string Stderr) RunExe(string exe, Dictionary<string, string>? environment, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                psi.Environment[pair.Key] = pair.Value;
            }
        }

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(60_000);
        return (process.ExitCode, stdout, stderr);
    }

    private static byte[] RichDocx()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            var headerPart = main.AddNewPart<HeaderPart>();
            headerPart.Header = new W.Header(new W.Paragraph(new W.Run(new W.Text("HEADER-TOKEN"))));
            headerPart.Header.Save();
            var footerPart = main.AddNewPart<FooterPart>();
            footerPart.Footer = new W.Footer(new W.Paragraph(new W.Run(new W.Text("FOOTER-TOKEN"))));
            footerPart.Footer.Save();
            var footnotes = main.AddNewPart<FootnotesPart>();
            footnotes.Footnotes = new W.Footnotes(
                new W.Footnote(new W.Paragraph(new W.Run(new W.Text("FOOTNOTE-TOKEN")))) { Type = W.FootnoteEndnoteValues.Normal, Id = 1 });
            footnotes.Footnotes.Save();

            var headerRef = new W.HeaderReference { Id = main.GetIdOfPart(headerPart), Type = W.HeaderFooterValues.Default };
            var footerRef = new W.FooterReference { Id = main.GetIdOfPart(footerPart), Type = W.HeaderFooterValues.Default };
            main.Document = new W.Document(new W.Body(
                new W.Paragraph(new W.ParagraphProperties(headerRef, footerRef), new W.Run(new W.Text("BODY"))),
                new W.SdtBlock(
                    new W.SdtProperties(new W.SdtAlias { Val = "Control" }),
                    new W.SdtContentBlock(new W.Paragraph(new W.Run(new W.Text("SDT-TOKEN")))))));
            main.Document.Save();
        }

        return stream.ToArray();
    }
}
