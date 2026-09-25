using System.IO.Compression;
using System.Text;

namespace DocExtract.Sniff;

public static class TypeSniffer
{
    public static Detection Detect(ReadOnlySpan<byte> data, string? fileName)
    {
        var ext = Extension(fileName);
        if (data.Length >= 5 && data[0] == (byte)'%' && data[1] == (byte)'P' && data[2] == (byte)'D' && data[3] == (byte)'F')
        {
            return new Detection(DocumentKind.Pdf, "application/pdf");
        }

        if (IsPng(data))
        {
            return new Detection(DocumentKind.Image, "image/png");
        }

        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return new Detection(DocumentKind.Image, "image/jpeg");
        }

        if (data.Length >= 6 && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F')
        {
            return new Detection(DocumentKind.Image, "image/gif");
        }

        if (data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M')
        {
            return new Detection(DocumentKind.Image, "image/bmp");
        }

        if (IsWebp(data))
        {
            return new Detection(DocumentKind.Image, "image/webp");
        }

        if (IsTiff(data))
        {
            return new Detection(DocumentKind.Image, "image/tiff");
        }

        if (IsOle(data))
        {
            if (ContainsAscii(data, "EncryptedPackage"))
            {
                return new Detection(DocumentKind.Unsupported, "application/vnd.ms-office.encrypted");
            }

            if (ContainsAscii(data, "__substg1.0") || ContainsAscii(data, "__nameid_version1.0") || ext is ".msg")
            {
                return new Detection(DocumentKind.Msg, "application/vnd.ms-outlook");
            }

            return new Detection(DocumentKind.Unsupported, "application/x-cfb");
        }

        if (IsZip(data))
        {
            return DetectZip(data);
        }

        if (LooksLikeUtf16(data) || HasBom(data) || LooksLikeText(data))
        {
            var head = DecodeHead(data);
            if (LooksLikeHtml(head))
            {
                return new Detection(DocumentKind.Text, "text/html");
            }

            if (LooksLikeEmail(head, ext))
            {
                return new Detection(DocumentKind.Eml, "message/rfc822");
            }

            if (head.TrimStart().StartsWith("{\\rtf", StringComparison.Ordinal))
            {
                return new Detection(DocumentKind.Text, "application/rtf");
            }

            return new Detection(DocumentKind.Text, "text/plain");
        }

        if (ext is ".txt" or ".csv" or ".log" or ".json" or ".xml" or ".md")
        {
            return new Detection(DocumentKind.Text, "text/plain");
        }

        if (ext is ".htm" or ".html")
        {
            return new Detection(DocumentKind.Text, "text/html");
        }

        if (ext is ".eml")
        {
            return new Detection(DocumentKind.Eml, "message/rfc822");
        }

        return new Detection(DocumentKind.Unsupported, "application/octet-stream");
    }

    private static Detection DetectZip(ReadOnlySpan<byte> data)
    {
        try
        {
            using var ms = new MemoryStream(data.ToArray(), writable: false);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
            var docx = zip.GetEntry("word/document.xml") is not null;
            var xlsx = zip.GetEntry("xl/workbook.xml") is not null;
            var pptx = zip.GetEntry("ppt/presentation.xml") is not null;
            var contentTypes = zip.GetEntry("[Content_Types].xml") is not null;
            if (contentTypes || docx || xlsx || pptx)
            {
                if (docx)
                {
                    return new Detection(DocumentKind.Docx, "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
                }

                if (xlsx)
                {
                    return new Detection(DocumentKind.Xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
                }

                if (pptx)
                {
                    return new Detection(DocumentKind.Pptx, "application/vnd.openxmlformats-officedocument.presentationml.presentation");
                }
            }
        }
        catch (InvalidDataException)
        {
            return new Detection(DocumentKind.Zip, "application/zip");
        }

        return new Detection(DocumentKind.Zip, "application/zip");
    }

    private static string Extension(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "";
        }

        var leaf = fileName.Replace('\\', '/');
        var slash = leaf.LastIndexOf('/');
        if (slash >= 0)
        {
            leaf = leaf[(slash + 1)..];
        }

        return Path.GetExtension(leaf).ToLowerInvariant();
    }

    private static bool IsPng(ReadOnlySpan<byte> data) =>
        data.Length >= 8 && data[0] == 137 && data[1] == 80 && data[2] == 78 && data[3] == 71;

    private static bool IsWebp(ReadOnlySpan<byte> data) =>
        data.Length >= 12
        && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
        && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P';

    private static bool IsTiff(ReadOnlySpan<byte> data) =>
        data.Length >= 4
        && ((data[0] == (byte)'I' && data[1] == (byte)'I' && data[2] == 42 && data[3] == 0)
            || (data[0] == (byte)'M' && data[1] == (byte)'M' && data[2] == 0 && data[3] == 42));

    private static bool IsOle(ReadOnlySpan<byte> data) =>
        data.Length >= 8
        && data[0] == 0xD0 && data[1] == 0xCF && data[2] == 0x11 && data[3] == 0xE0
        && data[4] == 0xA1 && data[5] == 0xB1 && data[6] == 0x1A && data[7] == 0xE1;

    private static bool IsZip(ReadOnlySpan<byte> data) =>
        data.Length >= 4
        && data[0] == (byte)'P' && data[1] == (byte)'K'
        && ((data[2] == 3 && data[3] == 4) || (data[2] == 5 && data[3] == 6) || (data[2] == 7 && data[3] == 8));

    private static bool HasBom(ReadOnlySpan<byte> data) =>
        (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        || (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
        || (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF);

    private static bool LooksLikeUtf16(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
        {
            return false;
        }

        var n = Math.Min(data.Length, 200);
        var zeros = 0;
        for (var i = 1; i < n; i += 2)
        {
            if (data[i] == 0)
            {
                zeros++;
            }
        }

        return zeros > n / 8;
    }

    private static bool LooksLikeText(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return false;
        }

        var n = Math.Min(data.Length, 4096);
        var control = 0;
        for (var i = 0; i < n; i++)
        {
            var b = data[i];
            if (b == 0)
            {
                return false;
            }

            if (b < 9 || (b > 13 && b < 32))
            {
                control++;
            }
        }

        return control * 20 < n;
    }

    private static string DecodeHead(ReadOnlySpan<byte> data)
    {
        var n = Math.Min(data.Length, 8192);
        return Text.TextDecoder.Decode(data[..n].ToArray());
    }

    private static bool LooksLikeHtml(string head)
    {
        var t = head.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return t.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("<head", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("<body", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeEmail(string head, string ext)
    {
        if (ext is ".eml" && (HasHeader(head, "From:") || HasHeader(head, "MIME-Version:") || HasHeader(head, "Received:") || HasHeader(head, "Subject:")))
        {
            return true;
        }

        var score = 0;
        if (HasHeader(head, "From:")) score++;
        if (HasHeader(head, "MIME-Version:")) score++;
        if (HasHeader(head, "Received:")) score++;
        if (HasHeader(head, "Content-Type:")) score++;
        if (HasHeader(head, "Subject:")) score++;
        if (HasHeader(head, "To:")) score++;
        return score >= 2 && (HasHeader(head, "From:") || HasHeader(head, "MIME-Version:") || HasHeader(head, "Received:"));
    }

    private static bool HasHeader(string text, string name)
    {
        var lines = text.Split('\n');
        var limit = Math.Min(lines.Length, 40);
        for (var i = 0; i < limit; i++)
        {
            if (lines[i].TrimEnd('\r').StartsWith(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAscii(ReadOnlySpan<byte> data, string needle)
    {
        var pattern = Encoding.ASCII.GetBytes(needle);
        if (pattern.Length == 0 || data.Length < pattern.Length)
        {
            return false;
        }

        var limit = data.Length - pattern.Length;
        for (var i = 0; i <= limit; i++)
        {
            if (data.Slice(i, pattern.Length).SequenceEqual(pattern))
            {
                return true;
            }
        }

        return false;
    }
}
