using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace DocExtract.Text;

public static partial class HtmlToText
{
    public static string Convert(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return "";
        }

        var withoutScripts = ScriptPattern().Replace(html, " ");
        withoutScripts = StylePattern().Replace(withoutScripts, " ");
        withoutScripts = BreakPattern().Replace(withoutScripts, "\n");
        withoutScripts = TagPattern().Replace(withoutScripts, " ");
        var decoded = WebUtility.HtmlDecode(withoutScripts);
        var sb = new StringBuilder(decoded.Length);
        var newlineRun = 0;
        var spaceRun = false;
        foreach (var ch in decoded)
        {
            if (ch is '\r')
            {
                continue;
            }

            if (ch is '\n')
            {
                spaceRun = false;
                newlineRun++;
                if (newlineRun <= 2)
                {
                    sb.Append('\n');
                }

                continue;
            }

            newlineRun = 0;
            if (char.IsWhiteSpace(ch))
            {
                if (!spaceRun && sb.Length > 0 && sb[^1] != '\n')
                {
                    sb.Append(' ');
                    spaceRun = true;
                }

                continue;
            }

            spaceRun = false;
            sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    [GeneratedRegex(@"<script\b[^>]*>.*?</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.NonBacktracking, 1000)]
    private static partial Regex ScriptPattern();

    [GeneratedRegex(@"<style\b[^>]*>.*?</style>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.NonBacktracking, 1000)]
    private static partial Regex StylePattern();

    [GeneratedRegex(@"<br\s*/?>|</p>|</div>|</tr>|</h[1-6]>", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, 1000)]
    private static partial Regex BreakPattern();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.NonBacktracking, 1000)]
    private static partial Regex TagPattern();
}
