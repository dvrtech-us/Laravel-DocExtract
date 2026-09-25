using System.Buffers.Binary;

namespace DocExtract.Sniff;

public static class ImageHeader
{
    public static bool TryGetSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        if (TryPng(data, out width, out height)
            || TryJpeg(data, out width, out height)
            || TryGif(data, out width, out height)
            || TryBmp(data, out width, out height)
            || TryWebp(data, out width, out height)
            || TryTiff(data, out width, out height))
        {
            return width > 0 && height > 0;
        }

        width = 0;
        height = 0;
        return false;
    }

    public static bool TryPng(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 24)
        {
            return false;
        }

        ReadOnlySpan<byte> sig = [137, 80, 78, 71, 13, 10, 26, 10];
        if (!data[..8].SequenceEqual(sig))
        {
            return false;
        }

        if (data[12] != (byte)'I' || data[13] != (byte)'H' || data[14] != (byte)'D' || data[15] != (byte)'R')
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(data[16..]);
        height = BinaryPrimitives.ReadInt32BigEndian(data[20..]);
        return width > 0 && height > 0;
    }

    public static bool TryJpeg(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
        {
            return false;
        }

        var i = 2;
        while (i + 8 < data.Length)
        {
            if (data[i] != 0xFF)
            {
                i++;
                continue;
            }

            while (i < data.Length && data[i] == 0xFF)
            {
                i++;
            }

            if (i >= data.Length)
            {
                break;
            }

            var marker = data[i++];
            if (marker is 0xD8 or 0xD9 or 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                continue;
            }

            if (i + 1 >= data.Length)
            {
                break;
            }

            var len = BinaryPrimitives.ReadUInt16BigEndian(data[i..]);
            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
            {
                if (i + 7 >= data.Length)
                {
                    return false;
                }

                height = BinaryPrimitives.ReadUInt16BigEndian(data[(i + 3)..]);
                width = BinaryPrimitives.ReadUInt16BigEndian(data[(i + 5)..]);
                return width > 0 && height > 0;
            }

            if (len < 2)
            {
                return false;
            }

            i += len;
        }

        return false;
    }

    public static bool TryGif(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 10 || data[0] != (byte)'G' || data[1] != (byte)'I' || data[2] != (byte)'F')
        {
            return false;
        }

        width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        return width > 0 && height > 0;
    }

    public static bool TryBmp(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 26 || data[0] != (byte)'B' || data[1] != (byte)'M')
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32LittleEndian(data[18..]);
        height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data[22..]));
        return width > 0 && height > 0;
    }

    public static bool TryWebp(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 16
            || data[0] != (byte)'R' || data[1] != (byte)'I' || data[2] != (byte)'F' || data[3] != (byte)'F'
            || data[8] != (byte)'W' || data[9] != (byte)'E' || data[10] != (byte)'B' || data[11] != (byte)'P')
        {
            return false;
        }

        if (data.Length >= 30 && data[12] == (byte)'V' && data[13] == (byte)'P' && data[14] == (byte)'8' && data[15] == (byte)'X')
        {
            width = 1 + data[24] + (data[25] << 8) + (data[26] << 16);
            height = 1 + data[27] + (data[28] << 8) + (data[29] << 16);
            return width > 0 && height > 0;
        }

        if (data.Length >= 30 && data[12] == (byte)'V' && data[13] == (byte)'P' && data[14] == (byte)'8' && data[15] == (byte)' ')
        {
            var start = data.IndexOf(new byte[] { 0x9D, 0x01, 0x2A });
            if (start >= 0 && start + 7 < data.Length)
            {
                width = data[start + 3] | ((data[start + 4] & 0x3F) << 8);
                height = data[start + 5] | ((data[start + 6] & 0x3F) << 8);
                return width > 0 && height > 0;
            }
        }

        return false;
    }

    public static bool TryTiff(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 8)
        {
            return false;
        }

        bool le;
        if (data[0] == (byte)'I' && data[1] == (byte)'I' && data[2] == 42 && data[3] == 0)
        {
            le = true;
        }
        else if (data[0] == (byte)'M' && data[1] == (byte)'M' && data[2] == 0 && data[3] == 42)
        {
            le = false;
        }
        else
        {
            return false;
        }

        static uint Offset(ReadOnlySpan<byte> bytes, int at, bool little)
            => little
                ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[at..])
                : BinaryPrimitives.ReadUInt32BigEndian(bytes[at..]);
        static ushort Short(ReadOnlySpan<byte> bytes, int at, bool little)
            => little
                ? BinaryPrimitives.ReadUInt16LittleEndian(bytes[at..])
                : BinaryPrimitives.ReadUInt16BigEndian(bytes[at..]);

        var ifd = (int)Offset(data, 4, le);
        if (ifd < 0 || ifd + 2 > data.Length)
        {
            return false;
        }

        var count = Short(data, ifd, le);
        var cursor = ifd + 2;
        for (var n = 0; n < count && cursor + 12 <= data.Length; n++, cursor += 12)
        {
            var tag = Short(data, cursor, le);
            var type = Short(data, cursor + 2, le);
            int value;
            if (type == 3)
            {
                value = Short(data, cursor + 8, le);
            }
            else if (type == 4)
            {
                value = (int)Offset(data, cursor + 8, le);
            }
            else
            {
                continue;
            }

            if (tag == 256)
            {
                width = value;
            }
            else if (tag == 257)
            {
                height = value;
            }
        }

        return width > 0 && height > 0;
    }
}
