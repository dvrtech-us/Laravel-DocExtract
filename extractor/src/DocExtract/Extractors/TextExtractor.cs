using System.Diagnostics;
using DocExtract.Extraction;
using DocExtract.Text;

namespace DocExtract.Extractors;

public sealed class TextExtractor : IExtractor
{
    public string Kind => "text";

    public void Extract(ExtractionInput input, ExtractionContext context)
    {
        var watch = Stopwatch.StartNew();
        var part = context.AddPart(input.LogicalPath, input.Depth, input.Kind, input.Mime, input.Data.LongLength, "text");
        try
        {
            var remain = Math.Max(0, context.Budget.MaxOutputChars - context.Budget.OutputCharsUsed);
            var decoded = TextDecoder.DecodeLimited(input.Data, remain + 1);
            if (input.Mime == "text/html" || LooksLikeHtml(decoded))
            {
                decoded = HtmlToText.Convert(decoded);
            }
            else if (input.Mime == "application/rtf" || decoded.TrimStart().StartsWith("{\\rtf", StringComparison.Ordinal))
            {
                decoded = StripRtf(decoded);
            }

            var body = MarkdownText.Heading(input.LogicalPath) + decoded.Trim() + "\n";
            part.Markdown = context.Take(body, part);
        }
        catch (Exception ex)
        {
            HandleOrRethrow(input, part, context, ex);
        }
        finally
        {
            part.DurationMs = ExtractorTiming.Elapsed(watch);
        }
    }

    internal static void HandleOrRethrow(ExtractionInput input, PartDraft part, ExtractionContext context, Exception ex)
    {
        if (ex is Contract.ExtractionException or OperationCanceledException)
        {
            throw ex;
        }

        if (!input.IsRoot)
        {
            var code = Contract.ExtractionException.IsEncryptedError(ex)
                ? Contract.WarningCodes.Encrypted
                : Contract.WarningCodes.PartFailed;
            part.Warn(code, ex.GetType().Name);
            if (part.Markdown.Length == 0)
            {
                part.Markdown = context.Take(MarkdownText.Heading(input.LogicalPath) + "This part could not be read.\n", part);
            }

            return;
        }

        if (Contract.ExtractionException.IsEncryptedError(ex))
        {
            throw Contract.ExtractionException.Encrypted(ex);
        }

        if (Contract.ExtractionException.IsCorruptError(ex))
        {
            throw Contract.ExtractionException.Corrupt(ex);
        }

        throw Contract.ExtractionException.Failed(ex);
    }

    private static bool LooksLikeHtml(string text)
    {
        var t = text.TrimStart();
        return t.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripRtf(string rtf)
    {
        var sb = new System.Text.StringBuilder(rtf.Length);
        var slash = false;
        foreach (var ch in rtf)
        {
            if (ch == '\\')
            {
                slash = true;
                continue;
            }

            if (slash)
            {
                if (ch is ' ' or '\n' or '\r')
                {
                    slash = false;
                }
                else if (char.IsLetter(ch))
                {
                    continue;
                }
                else
                {
                    slash = false;
                }

                continue;
            }

            if (ch is '{' or '}')
            {
                continue;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }
}
