using System.Diagnostics;
using System.Text;
using DocExtract.Contract;
using DocExtract.Extraction;
using DocExtract.Sniff;
using DocExtract.Text;
using MimeKit;

namespace DocExtract.Extractors;

public sealed class EmlExtractor : IExtractor
{
    public string Kind => "eml";

    public void Extract(ExtractionInput input, ExtractionContext context)
    {
        var watch = Stopwatch.StartNew();
        var part = context.AddPart(input.LogicalPath, input.Depth, "eml", input.Mime, input.Data.LongLength, "mimekit");
        var children = new List<(string Name, byte[] Data)>();
        try
        {
            using var stream = new MemoryStream(input.Data, writable: false);
            var message = MimeMessage.Load(stream);
            var sb = new StringBuilder();
            sb.Append(MarkdownText.Heading(input.LogicalPath));
            sb.Append("**From:** ").Append(message.From).Append("\n\n");
            sb.Append("**To:** ").Append(message.To).Append("\n\n");
            if (message.Cc.Count > 0)
            {
                sb.Append("**Cc:** ").Append(message.Cc).Append("\n\n");
            }

            sb.Append("**Subject:** ").Append(message.Subject).Append("\n\n");
            if (message.Date != default)
            {
                sb.Append("**Date:** ").Append(message.Date.ToUniversalTime().ToString("u")).Append("\n\n");
            }

            var body = message.TextBody;
            if (string.IsNullOrWhiteSpace(body))
            {
                body = HtmlToText.Convert(message.HtmlBody);
            }

            if (!string.IsNullOrWhiteSpace(body))
            {
                sb.Append(body.Trim()).Append("\n\n");
            }

            foreach (var entity in message.BodyParts)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                if (entity is MessagePart nested && nested.Message is not null)
                {
                    using var nestedStream = new MemoryStream();
                    nested.Message.WriteTo(nestedStream);
                    children.Add((SafeName(nested.ContentDisposition?.FileName, "attached-message.eml"), nestedStream.ToArray()));
                    continue;
                }

                if (entity is not MimePart mimePart)
                {
                    continue;
                }

                if (mimePart.ContentType.MimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) && !mimePart.IsAttachment)
                {
                    continue;
                }

                var mimeContent = mimePart.Content;
                if (mimeContent is null)
                {
                    continue;
                }

                using var content = new MemoryStream();
                mimeContent.DecodeTo(content);
                var bytes = content.ToArray();
                var name = SafeName(mimePart.FileName ?? mimePart.ContentType.Name, "attachment.bin");
                if (IsImage(mimePart.ContentType.MimeType, bytes))
                {
                    var ocr = context.TryAttachImage(part, bytes);
                    ImageExtractor.AppendOcr(sb, part, ocr);
                    continue;
                }

                children.Add((name, bytes));
            }

            if (children.Count > 0)
            {
                sb.Append("Attachments:\n\n");
                foreach (var child in children)
                {
                    sb.Append("- ").Append(child.Name).Append('\n');
                }

                sb.Append('\n');
            }

            part.Markdown = context.Take(sb.ToString(), part);
            Recurse(input, context, part, children);
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

    internal static void Recurse(ExtractionInput input, ExtractionContext context, PartDraft part, List<(string Name, byte[] Data)> children)
    {
        if (children.Count == 0)
        {
            return;
        }

        if (input.Depth >= context.Budget.MaxDepth)
        {
            part.Warn(WarningCodes.DepthLimit, "depth " + input.Depth);
            return;
        }

        foreach (var child in children)
        {
            if (!context.CanAddPart())
            {
                part.Warn(WarningCodes.ChildLimit, child.Name);
                break;
            }

            try
            {
                context.ProcessChild(child.Data, input.LogicalPath + "/" + child.Name, input.Depth + 1);
            }
            catch (ChildLimitException)
            {
                part.Warn(WarningCodes.ChildLimit, child.Name);
                break;
            }
        }
    }

    internal static string SafeName(string? name, string fallback)
    {
        var leaf = string.IsNullOrWhiteSpace(name) ? fallback : name.Replace('\\', '/');
        var slash = leaf.LastIndexOf('/');
        if (slash >= 0)
        {
            leaf = leaf[(slash + 1)..];
        }

        leaf = leaf.Replace(":", "_").Replace("\0", "").Trim();
        if (leaf is "" or "." or ".." || Paths.SafePath.IsReservedDeviceName(leaf))
        {
            return fallback;
        }

        return leaf;
    }

    internal static bool IsImage(string? mime, byte[] bytes)
    {
        if (!string.IsNullOrEmpty(mime) && mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TypeSniffer.Detect(bytes, null).Kind == DocumentKind.Image;
    }
}
