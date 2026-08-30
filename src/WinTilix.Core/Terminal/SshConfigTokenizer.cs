using System.Text;

namespace WinTilix.Terminal;

public static class SshConfigTokenizer
{
    public static bool IsSelectableAlias(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.StartsWith('!') &&
        value.IndexOfAny(['*', '?']) < 0;

    public static IReadOnlyList<string> Tokenize(string line)
    {
        var result = new List<string>();
        var token = new StringBuilder();
        var quoted = false;

        foreach (var ch in line)
        {
            if (ch is '"' or '\'')
            {
                quoted = !quoted;
                continue;
            }

            if (ch == '#' && !quoted)
            {
                break;
            }

            if (char.IsWhiteSpace(ch) && !quoted)
            {
                if (token.Length > 0)
                {
                    result.Add(token.ToString());
                    token.Clear();
                }

                continue;
            }

            token.Append(ch);
        }

        if (token.Length > 0)
        {
            result.Add(token.ToString());
        }

        return result;
    }

    public static string QuoteIfNeeded(string value)
    {
        if (value.Length == 0 || value.Any(char.IsWhiteSpace))
        {
            return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        }

        return value;
    }
}
