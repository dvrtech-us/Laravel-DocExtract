using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using DocExtract.Contract;
using DocExtract.Extraction;
using DocExtract.Paths;
using DocExtract.Text;

namespace DocExtract.Extractors;

public sealed class ZipExtractor : IExtractor
{
    public string Kind => "zip";

    public void Extract(ExtractionInput input, ExtractionContext context)
    {
        var watch = Stopwatch.StartNew();
        var part = context.AddPart(input.LogicalPath, input.Depth, "zip", input.Mime, input.Data.LongLength, "zip");
        try
        {
            using var stream = new MemoryStream(input.Data, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var encryption = ReadEncryptionFlags(input.Data);
            var listing = new StringBuilder();
            listing.Append(MarkdownText.Heading(input.LogicalPath));
            listing.Append("Entries:\n\n");
            var listed = 0;
            foreach (var entry in archive.Entries)
            {
                if (listed >= context.Budget.MaxParts)
                {
                    part.Warn(WarningCodes.ChildLimit, "listing cap");
                    break;
                }

                listed++;
                listing.Append("- `").Append(entry.FullName.Replace('\r', ' ').Replace('\n', ' ')).Append("` (");
                listing.Append(entry.Length).Append(" bytes)\n");
            }

            listing.Append('\n');
            part.Markdown = context.Take(listing.ToString(), part);

            if (input.Depth >= context.Budget.MaxDepth)
            {
                part.Warn(WarningCodes.DepthLimit, "depth " + input.Depth);
                return;
            }

            foreach (var entry in archive.Entries)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                if (IsDirectory(entry))
                {
                    continue;
                }

                if (!context.CanAddPart())
                {
                    part.Warn(WarningCodes.ChildLimit, entry.FullName);
                    break;
                }

                if (IsUnsafeEntry(entry.FullName))
                {
                    part.Warn(WarningCodes.PartFailed, "unsafe entry name");
                    continue;
                }

                if (encryption.TryGetValue(entry.FullName, out var encrypted) && encrypted)
                {
                    part.Warn(WarningCodes.Encrypted, entry.FullName);
                    continue;
                }

                if (DeclaredRatioExceeded(entry, context))
                {
                    if (input.Depth > 0)
                    {
                        part.Warn(WarningCodes.PartFailed, "zip ratio");
                        continue;
                    }

                    throw ExtractionException.Limit(ErrorCodes.LimitZipRatio);
                }

                byte[] child;
                try
                {
                    child = ReadBounded(entry, context);
                }
                catch (ExtractionException ex) when (ex.ErrorCode == ErrorCodes.LimitZipRatio && input.Depth > 0)
                {
                    part.Warn(WarningCodes.PartFailed, "zip ratio");
                    continue;
                }
                catch (InvalidDataException ex)
                {
                    var code = ex.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase)
                        ? WarningCodes.Encrypted
                        : WarningCodes.PartFailed;
                    part.Warn(code, entry.FullName);
                    continue;
                }

                var childPath = input.LogicalPath + "/" + Normalize(entry.FullName);
                try
                {
                    context.ProcessChild(child, childPath, input.Depth + 1);
                }
                catch (ChildLimitException)
                {
                    part.Warn(WarningCodes.ChildLimit, entry.FullName);
                    break;
                }
            }
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

    internal static bool IsDirectory(ZipArchiveEntry entry) =>
        string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');

    internal static bool IsUnsafeEntry(string fullName)
    {
        var name = fullName.Replace('\\', '/').TrimStart('/');
        if (name.Contains(':') || name.Contains('\0'))
        {
            return true;
        }

        foreach (var segment in name.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or ".." || SafePath.IsReservedDeviceName(segment))
            {
                return true;
            }
        }

        return name.Length == 0;
    }

    internal static string Normalize(string fullName)
    {
        var name = fullName.Replace('\\', '/').TrimStart('/');
        while (name.Contains("//", StringComparison.Ordinal))
        {
            name = name.Replace("//", "/", StringComparison.Ordinal);
        }

        return name;
    }

    internal static bool DeclaredRatioExceeded(ZipArchiveEntry entry, ExtractionContext context) =>
        entry.Length > Limits.LimitBudget.ZipRatioHardFailBytes
        && Limits.LimitBudget.ExceedsZipRatio(entry.Length, entry.CompressedLength, context.Budget.MaxZipRatio);

    internal static long ReadCap(ZipArchiveEntry entry, double maxRatio)
    {
        var length = entry.Length < 0 ? 0 : entry.Length;
        if (length <= Limits.LimitBudget.ZipRatioHardFailBytes)
        {
            return length == 0 ? long.MaxValue : length;
        }

        var ratioCap = entry.CompressedLength <= 0
            ? 0L
            : (long)Math.Min(long.MaxValue, entry.CompressedLength * maxRatio);
        return length == 0 ? ratioCap : Math.Min(length, ratioCap);
    }

    internal static byte[] ReadBounded(ZipArchiveEntry entry, ExtractionContext context)
    {
        var cap = ReadCap(entry, context.Budget.MaxZipRatio);
        if (entry.Length > Limits.LimitBudget.ZipRatioHardFailBytes)
        {
            context.Budget.EnsureDecompressedFits(Math.Min(entry.Length, cap));
        }
        else if (entry.Length > 0)
        {
            context.Budget.EnsureDecompressedFits(entry.Length);
        }

        using var input = entry.Open();
        var known = entry.Length > 0 && entry.Length <= int.MaxValue && entry.Length <= cap
            ? (int)entry.Length
            : 0;
        using var output = known > 0 ? new MemoryStream(known) : new MemoryStream();
        var buffer = new byte[81920];
        long readTotal = 0;
        while (true)
        {
            if (cap != long.MaxValue && readTotal >= cap)
            {
                if (entry.Length > Limits.LimitBudget.ZipRatioHardFailBytes && cap < entry.Length)
                {
                    throw ExtractionException.Limit(ErrorCodes.LimitZipRatio);
                }

                break;
            }

            var room = context.Budget.RemainingDecompressed;
            if (room <= 0)
            {
                throw ExtractionException.Limit(ErrorCodes.LimitDecompressed);
            }

            var capRoom = cap == long.MaxValue ? room : cap - readTotal;
            var want = (int)Math.Min(buffer.Length, Math.Min(room, capRoom));
            if (want <= 0)
            {
                break;
            }

            var read = input.Read(buffer, 0, want);
            if (read == 0)
            {
                break;
            }

            readTotal += read;
            context.Budget.ChargeDecompressed(read);
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    internal static Dictionary<string, bool> ReadEncryptionFlags(byte[] data)
    {
        var map = new Dictionary<string, bool>(StringComparer.Ordinal);
        var start = Math.Max(0, data.Length - 65557);
        for (var i = data.Length - 22; i >= start; i--)
        {
            if (data[i] != 0x50 || data[i + 1] != 0x4B || data[i + 2] != 0x05 || data[i + 3] != 0x06)
            {
                continue;
            }

            if (i + 22 > data.Length)
            {
                return map;
            }

            var count = BitConverter.ToUInt16(data, i + 10);
            var offset = BitConverter.ToUInt32(data, i + 16);
            if (data.Length < 46 || offset == 0xFFFFFFFFu || offset > (uint)data.Length - 46)
            {
                return map;
            }

            var cursor = (int)offset;
            for (var n = 0; n < count; n++)
            {
                if (cursor < 0 || cursor > data.Length - 46)
                {
                    break;
                }

                if (BitConverter.ToUInt32(data, cursor) != 0x02014b50)
                {
                    break;
                }

                var flags = BitConverter.ToUInt16(data, cursor + 8);
                var nameLength = BitConverter.ToUInt16(data, cursor + 28);
                var extraLength = BitConverter.ToUInt16(data, cursor + 30);
                var commentLength = BitConverter.ToUInt16(data, cursor + 32);
                var next = (long)cursor + 46 + nameLength + extraLength + commentLength;
                if (nameLength > data.Length - (cursor + 46) || next > data.Length || next > int.MaxValue)
                {
                    break;
                }

                var name = Encoding.UTF8.GetString(data, cursor + 46, nameLength);
                map[name] = (flags & 1) != 0;
                cursor = (int)next;
            }

            break;
        }

        return map;
    }
}
