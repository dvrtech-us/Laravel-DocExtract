using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using DocExtract.Contract;
using DocExtract.Extraction;
using DocExtract.Limits;
using DocExtract.Ocr;
using DocExtract.Output;

namespace DocExtract.Tests;

public sealed class ScriptedOcr : IOcrEngine
{
    public bool IsAvailable { get; init; } = true;
    public string? UnavailableReason => IsAvailable ? null : "tessdata/eng.traineddata missing";
    public string Text { get; init; } = "FAKE-OCR";
    public int Calls { get; private set; }

    public string Recognize(ReadOnlyMemory<byte> image, CancellationToken cancellationToken)
    {
        Calls++;
        return Text;
    }
}

public static class TestSupport
{
    public static ExtractionPipeline Pipeline(IOcrEngine? ocr = null) => new(ocr ?? new ScriptedOcr { IsAvailable = false });

    public static ExtractionResult Extract(byte[] data, string name, LimitBudget? budget = null, ExtractSettings? settings = null, IOcrEngine? ocr = null) =>
        Pipeline(ocr).Extract(data, name, budget ?? new LimitBudget(), settings ?? new ExtractSettings());

    public static string Markdown(ExtractionResult result) => string.Concat(result.Parts.Select(p => p.Markdown));

    public static (int ExitCode, string Stdout, string Stderr) RunCli(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("exec");
        psi.ArgumentList.Add(typeof(EngineCatalog).Assembly.Location);
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(120_000);
        return (process.ExitCode, stdout, stderr);
    }

    public static ResultDocument WriteResult(ExtractionResult result)
    {
        var dir = Path.Combine(Path.GetTempPath(), "docextract-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            return ResultWriter.Write(dir, result);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    public static byte[] CraftPng(uint width, uint height)
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        WriteBe(stream, 13);
        stream.Write(Encoding.ASCII.GetBytes("IHDR"));
        WriteBe(stream, width);
        WriteBe(stream, height);
        stream.Write(new byte[] { 8, 2, 0, 0, 0 });
        WriteBe(stream, 0);
        return stream.ToArray();
    }

    public static byte[] ZipWith(params (string Name, byte[] Data, CompressionLevel Level)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            foreach (var entry in entries)
            {
                var zipEntry = archive.CreateEntry(entry.Name, entry.Level);
                using var output = zipEntry.Open();
                output.Write(entry.Data);
            }
        }

        return stream.ToArray();
    }

    public static byte[] RawStoredZip(string name, byte[] data, bool encrypted = false)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var crc = Crc32(data);
        var flags = (ushort)(encrypted ? 1 : 0);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0x04034b50);
        writer.Write((ushort)20);
        writer.Write(flags);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(crc);
        writer.Write((uint)data.Length);
        writer.Write((uint)data.Length);
        writer.Write((ushort)nameBytes.Length);
        writer.Write((ushort)0);
        writer.Write(nameBytes);
        writer.Write(data);
        var localSize = (uint)(30 + nameBytes.Length + data.Length);
        writer.Write(0x02014b50);
        writer.Write((ushort)20);
        writer.Write((ushort)20);
        writer.Write(flags);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(crc);
        writer.Write((uint)data.Length);
        writer.Write((uint)data.Length);
        writer.Write((ushort)nameBytes.Length);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(nameBytes);
        var centralSize = (uint)(46 + nameBytes.Length);
        writer.Write(0x06054b50);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write(centralSize);
        writer.Write(localSize);
        writer.Write((ushort)0);
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteBe(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
