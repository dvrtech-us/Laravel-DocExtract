using System.Text;

namespace DocExtract.Text;

public static class TextDecoder
{
    static TextDecoder()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static string Decode(byte[] data)
    {
        if (data.Length == 0)
        {
            return "";
        }

        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(data, 3, data.Length - 3);
        }

        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(data, 2, data.Length - 2);
        }

        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);
        }

        if (LooksLikeUtf16(data, out var littleEndian))
        {
            var length = data.Length - (data.Length % 2);
            var encoding = littleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode;
            return encoding.GetString(data, 0, length);
        }

        if (IsUtf8(data))
        {
            return Encoding.UTF8.GetString(data);
        }

        return Encoding.GetEncoding(1252).GetString(data);
    }

    public static string DecodeLimited(byte[] data, int maxChars)
    {
        if (maxChars <= 0 || data.Length == 0)
        {
            return "";
        }

        var byteCap = Math.Min(data.Length, Math.Max(maxChars * 4, 8));
        if (byteCap < data.Length)
        {
            var prefix = new byte[byteCap];
            Buffer.BlockCopy(data, 0, prefix, 0, byteCap);
            return Limits.Utf16Units.Truncate(Decode(prefix), maxChars);
        }

        return Limits.Utf16Units.Truncate(Decode(data), maxChars);
    }

    internal static bool LooksLikeUtf16(byte[] data, out bool littleEndian)
    {
        littleEndian = true;
        if (data.Length < 8)
        {
            return false;
        }

        var n = Math.Min(data.Length, 256) & ~1;
        var pairs = n / 2;
        var evenZeros = 0;
        var oddZeros = 0;
        for (var i = 0; i < n; i += 2)
        {
            if (data[i] == 0)
            {
                evenZeros++;
            }

            if (data[i + 1] == 0)
            {
                oddZeros++;
            }
        }

        if (oddZeros > pairs * 0.6 && evenZeros < pairs * 0.25)
        {
            littleEndian = true;
            return true;
        }

        if (evenZeros > pairs * 0.6 && oddZeros < pairs * 0.25)
        {
            littleEndian = false;
            return true;
        }

        return false;
    }

    private static bool IsUtf8(byte[] data)
    {
        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            utf8.GetString(data);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
