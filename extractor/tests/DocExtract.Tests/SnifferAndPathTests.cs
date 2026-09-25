using DocExtract.Contract;
using DocExtract.Limits;
using DocExtract.Paths;
using DocExtract.SelfTest;
using DocExtract.Sniff;

namespace DocExtract.Tests;

public class SnifferTests
{
    [Fact]
    public void DetectsPdfPngJpegAndHtml()
    {
        Assert.Equal("pdf", TypeSniffer.Detect(FixtureFactory.Pdf(), "x.bin").KindName);
        Assert.Equal("image/png", TypeSniffer.Detect(FixtureFactory.Png(), "x.bin").Mime);
        Assert.Equal("image", TypeSniffer.Detect([0xFF, 0xD8, 0xFF, 0x00], "x.bin").KindName);
        Assert.Equal("text/html", TypeSniffer.Detect(FixtureFactory.Html(), "page.bin").Mime);
        Assert.Equal("text", TypeSniffer.Detect(FixtureFactory.Text(), "note.txt").KindName);
    }

    [Fact]
    public void DistinguishesOfficeFromZipUsingContentTypes()
    {
        Assert.Equal("docx", TypeSniffer.Detect(FixtureFactory.Docx(), "file.zip").KindName);
        Assert.Equal("xlsx", TypeSniffer.Detect(FixtureFactory.Xlsx(), "file.zip").KindName);
        Assert.Equal("pptx", TypeSniffer.Detect(FixtureFactory.Pptx(), "file.zip").KindName);
        Assert.Equal("zip", TypeSniffer.Detect(FixtureFactory.Zip(), "archive.docx").KindName);
    }

    [Fact]
    public void DetectsEmailAndMsgByMagicNotOnlyExtension()
    {
        Assert.Equal("eml", TypeSniffer.Detect(FixtureFactory.Eml(), "mail.bin").KindName);
        var msg = new byte[64];
        msg[0] = 0xD0; msg[1] = 0xCF; msg[2] = 0x11; msg[3] = 0xE0;
        msg[4] = 0xA1; msg[5] = 0xB1; msg[6] = 0x1A; msg[7] = 0xE1;
        System.Text.Encoding.ASCII.GetBytes("__substg1.0").CopyTo(msg, 16);
        Assert.Equal("msg", TypeSniffer.Detect(msg, "note.bin").KindName);

        var ole = msg.ToArray();
        ole.AsSpan(16).Clear();
        Assert.Equal("unsupported", TypeSniffer.Detect(ole, "legacy.bin").KindName);
        Assert.Equal("msg", TypeSniffer.Detect(ole, "legacy.msg").KindName);
    }

    [Fact]
    public void ReadsCraftedPngDimensionsBeforeDecode()
    {
        var png = TestSupport.CraftPng(100_000, 80_000);
        Assert.True(ImageHeader.TryPng(png, out var width, out var height));
        Assert.Equal(100_000, width);
        Assert.Equal(80_000, height);
        Assert.True((long)width * height > LimitBudget.DefaultMaxRasterPixels);
    }
}

public class SafePathTests
{
    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("..\\secret.txt")]
    [InlineData("folder/../../outside.txt")]
    [InlineData("file.txt:stream")]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("COM1.txt")]
    [InlineData("LPT9")]
    [InlineData("folder/AUX/file.txt")]
    [InlineData("PRN.txt")]
    [InlineData("NUL")]
    public void RejectsUnsafeRelativePaths(string relative)
    {
        var ex = Assert.Throws<ExtractionException>(() => SafePath.Combine(Path.GetTempPath(), relative));
        Assert.Equal(ErrorCodes.UnsafePath, ex.ErrorCode);
    }

    [Fact]
    public void WritesOnlyUnderTheOutputRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "docextract-path-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            var full = SafePath.Combine(root, "parts/0.md");
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "ok");
            var rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            Assert.StartsWith(rootFull, Path.GetFullPath(full), StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(root, "parts", "0.md")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
