using System.Text;

namespace NugetToCsv.Services;

public static class CsvLine
{
    public static List<string> Parse(string? line)
    {
        var result = new List<string>();
        if (line is null)
            return result;

        var builder = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        builder.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    builder.Append(c);
                }
            }
            else if (c == ',')
            {
                result.Add(builder.ToString());
                builder.Clear();
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else
            {
                builder.Append(c);
            }
        }

        result.Add(builder.ToString());
        return result;
    }

    public static string Join(IEnumerable<string> fields) =>
        string.Join(",", fields.Select(Escape));

    public static string Escape(string? value)
    {
        if (value is null)
            return string.Empty;

        var mustQuote = value.IndexOf(',') >= 0
                        || value.IndexOf('"') >= 0
                        || value.IndexOf('\n') >= 0
                        || value.IndexOf('\r') >= 0;
        if (!mustQuote)
            return value;

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
