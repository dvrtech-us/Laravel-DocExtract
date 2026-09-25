using System.Diagnostics;
using System.Text;
using DocExtract.Contract;
using DocExtract.Extraction;
using DocExtract.Text;
using MsgReader.Outlook;

namespace DocExtract.Extractors;

public sealed class MsgExtractor : IExtractor
{
    public string Kind => "msg";

    public void Extract(ExtractionInput input, ExtractionContext context)
    {
        var part = context.AddPart(input.LogicalPath, input.Depth, "msg", input.Mime, input.Data.LongLength, "msgreader");
        try
        {
            using var stream = new MemoryStream(input.Data, writable: false);
            using var message = new Storage.Message(stream, FileAccess.Read, true);
            WriteMessage(input, context, part, message, input.Data.LongLength);
        }
        catch (Exception ex)
        {
            TextExtractor.HandleOrRethrow(input, part, context, ex);
        }
    }

    private static void WriteMessage(ExtractionInput input, ExtractionContext context, PartDraft part, Storage.Message message, long size)
    {
        var children = new List<(string Name, byte[] Data)>();
        var nested = new List<Storage.Message>();
        var watch = Stopwatch.StartNew();
        try
        {
            var sb = new StringBuilder();
            sb.Append(MarkdownText.Heading(part.Path));
            var from = message.Sender;
            sb.Append("**From:** ").Append(from?.DisplayName ?? "").Append(" <").Append(from?.Email ?? "").Append(">\n\n");
            sb.Append("**To:** ").Append(message.GetEmailRecipients(RecipientType.To, false, false)).Append("\n\n");
            sb.Append("**Subject:** ").Append(message.Subject).Append("\n\n");
            var body = message.BodyText;
            if (string.IsNullOrWhiteSpace(body))
            {
                body = HtmlToText.Convert(message.BodyHtml);
            }

            if (!string.IsNullOrWhiteSpace(body))
            {
                sb.Append(body.Trim()).Append("\n\n");
            }

            foreach (var attachment in message.Attachments)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                if (attachment is Storage.Attachment file)
                {
                    var bytes = file.Data ?? [];
                    var name = EmlExtractor.SafeName(file.FileName, "attachment.bin");
                    if (bytes.Length == 0)
                    {
                        part.Warn(WarningCodes.PartFailed, name);
                        continue;
                    }

                    if (file.IsInline || EmlExtractor.IsImage(null, bytes))
                    {
                        if (EmlExtractor.IsImage(null, bytes))
                        {
                            var ocr = context.TryAttachImage(part, bytes);
                            ImageExtractor.AppendOcr(sb, part, ocr);
                            continue;
                        }
                    }

                    children.Add((name, bytes));
                }
                else if (attachment is Storage.Message childMessage)
                {
                    nested.Add(childMessage);
                }
            }

            if (children.Count > 0 || nested.Count > 0)
            {
                sb.Append("Attachments:\n\n");
                foreach (var child in children)
                {
                    sb.Append("- ").Append(child.Name).Append('\n');
                }

                foreach (var child in nested)
                {
                    sb.Append("- ").Append(child.Subject ?? "message").Append(".msg\n");
                }

                sb.Append('\n');
            }

            part.SizeBytes = size;
            part.Markdown = context.Take(sb.ToString(), part);
            watch.Stop();
            part.DurationMs = watch.ElapsedMilliseconds;
            EmlExtractor.Recurse(input, context, part, children);
            if (nested.Count > 0)
            {
                if (input.Depth >= context.Budget.MaxDepth)
                {
                    part.Warn(WarningCodes.DepthLimit, "depth " + input.Depth);
                }
                else
                {
                    foreach (var child in nested)
                    {
                        if (!context.CanAddPart())
                        {
                            part.Warn(WarningCodes.ChildLimit, child.Subject ?? "message");
                            break;
                        }

                        var childPart = context.AddPart(
                            input.LogicalPath + "/" + EmlExtractor.SafeName(child.Subject, "message") + ".msg",
                            input.Depth + 1,
                            "msg",
                            "application/vnd.ms-outlook",
                            0,
                            "msgreader");
                        var childInput = new ExtractionInput
                        {
                            Data = [],
                            LogicalPath = childPart.Path,
                            Depth = input.Depth + 1,
                            IsRoot = false,
                            Kind = "msg",
                            Mime = childPart.Mime,
                        };
                        WriteMessage(childInput, context, childPart, child, 0);
                    }
                }
            }
        }
        catch (Exception)
        {
            if (part.DurationMs == 0)
            {
                watch.Stop();
                part.DurationMs = watch.ElapsedMilliseconds;
            }

            throw;
        }
    }
}
