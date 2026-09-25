using System.Text;

namespace DocExtract.Text;

public static class MarkdownText
{
    public static string Heading(string path)
    {
        var one = path.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return "## " + one + "\n\n";
    }

    public static string Table(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (rows.Count == 0)
        {
            return "";
        }

        var cols = 0;
        foreach (var row in rows)
        {
            if (row.Count > cols)
            {
                cols = row.Count;
            }
        }

        if (cols == 0)
        {
            return "";
        }

        var sb = new StringBuilder();
        AppendRow(sb, rows[0], cols, false);
        sb.Append('|');
        for (var c = 0; c < cols; c++)
        {
            sb.Append(" --- |");
        }

        sb.Append('\n');
        for (var r = 1; r < rows.Count; r++)
        {
            AppendRow(sb, rows[r], cols, false);
        }

        return sb.ToString();
    }

    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return value.Replace("|", "\\|").Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private static void AppendRow(StringBuilder sb, IReadOnlyList<string> row, int cols, bool _)
    {
        sb.Append('|');
        for (var c = 0; c < cols; c++)
        {
            var cell = c < row.Count ? Cell(row[c]) : "";
            sb.Append(' ').Append(cell).Append(" |");
        }

        sb.Append('\n');
    }
}
