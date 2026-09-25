using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocExtract.Contract;
using DocExtract.Extraction;
using DocExtract.Paths;

namespace DocExtract.Output;

public sealed class ResultDocument
{
    public int SchemaVersion { get; set; } = EngineCatalog.SchemaVersion;
    public string ExtractorVersion { get; set; } = EngineCatalog.Version;
    public string Status { get; set; } = "ok";
    public int TotalChars { get; set; }
    public bool Truncated { get; set; }
    public List<ResultPart> Parts { get; set; } = [];
    public List<ResultImage> Images { get; set; } = [];
}

public sealed class ResultPart
{
    public int Index { get; set; }
    public string Path { get; set; } = "";
    public int Depth { get; set; }
    public string Kind { get; set; } = "";
    public string Mime { get; set; } = "";
    public long SizeBytes { get; set; }
    public int? Pages { get; set; }
    public string Engine { get; set; } = "";
    public long DurationMs { get; set; }
    public int CharStart { get; set; }
    public int CharEnd { get; set; }
    public string MarkdownFile { get; set; } = "";
    public bool Truncated { get; set; }
    public List<ResultWarning> Warnings { get; set; } = [];
    public List<string> ImageIds { get; set; } = [];
}

public sealed class ResultWarning
{
    public string Code { get; set; } = "";
    public string Detail { get; set; } = "";
}

public sealed class ResultImage
{
    public string Id { get; set; } = "";
    public int PartIndex { get; set; }
    public string File { get; set; } = "";
    public string Mime { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public long SizeBytes { get; set; }
    public int OcrChars { get; set; }
}

public static class ResultWriter
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
    };

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static ResultDocument Write(string outputDir, ExtractionResult result)
    {
        Directory.CreateDirectory(outputDir);
        var document = ToDocument(result);
        foreach (var part in document.Parts)
        {
            var relative = part.MarkdownFile;
            var full = SafePath.Combine(outputDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            var draft = result.Parts[part.Index];
            File.WriteAllText(full, draft.Markdown, Utf8);
        }

        foreach (var image in document.Images)
        {
            var full = SafePath.Combine(outputDir, image.File);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            var draft = result.Images.First(i => i.Id == image.Id);
            File.WriteAllBytes(full, draft.Bytes);
        }

        var jsonPath = SafePath.Combine(outputDir, "result.json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(document, JsonOptions), Utf8);
        return document;
    }

    public static ResultDocument ToDocument(ExtractionResult result)
    {
        var document = new ResultDocument
        {
            Status = result.Status,
            TotalChars = result.TotalChars,
            Truncated = result.Truncated,
        };

        foreach (var part in result.Parts)
        {
            document.Parts.Add(new ResultPart
            {
                Index = part.Index,
                Path = part.Path,
                Depth = part.Depth,
                Kind = part.Kind,
                Mime = part.Mime,
                SizeBytes = part.SizeBytes,
                Pages = part.Pages,
                Engine = part.Engine,
                DurationMs = part.DurationMs,
                CharStart = part.CharStart,
                CharEnd = part.CharEnd,
                MarkdownFile = "parts/" + part.Index + ".md",
                Truncated = part.Truncated,
                Warnings = part.Warnings.Select(w => new ResultWarning { Code = w.Code, Detail = w.Detail }).ToList(),
                ImageIds = part.ImageIds.ToList(),
            });
        }

        foreach (var image in result.Images)
        {
            document.Images.Add(new ResultImage
            {
                Id = image.Id,
                PartIndex = image.PartIndex,
                File = "images/" + image.Id + image.Extension,
                Mime = image.Mime,
                Width = image.Width,
                Height = image.Height,
                SizeBytes = image.Bytes.LongLength,
                OcrChars = image.OcrChars,
            });
        }

        return document;
    }
}
