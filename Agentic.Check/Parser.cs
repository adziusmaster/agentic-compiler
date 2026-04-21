namespace Agentic.Check;

// Mini S-expression parser for the Agentic subset the checker needs to
// re-run tests. Independent of Agentic.Core's parser.
//
// Two node kinds:
//   - Atom : a bare token (number, string, boolean, symbol).
//   - List : a parenthesised sequence of nodes.
//
// Strings are double-quoted with `\"` / `\\` / `\n` / `\t` escapes.
// Comments begin with `;` and run to end of line.
//
// TCB cost: ~180 LOC.

public abstract record Node;
public sealed record Atom(string Value, AtomKind Kind) : Node;
public sealed record SList(IReadOnlyList<Node> Elements) : Node;

public enum AtomKind { Symbol, Number, String, True, False }

public static class Parser
{
    public static Node Parse(string source)
    {
        var tokens = Tokenize(source);
        int i = 0;
        var node = ReadExpr(tokens, ref i);
        if (i < tokens.Count) throw new ParseException($"Unexpected trailing token '{tokens[i]}'.");
        return node;
    }

    /// <summary>Parse a source that may contain multiple top-level forms.</summary>
    public static IReadOnlyList<Node> ParseAll(string source)
    {
        var tokens = Tokenize(source);
        int i = 0;
        var result = new List<Node>();
        while (i < tokens.Count) result.Add(ReadExpr(tokens, ref i));
        return result;
    }

    private static Node ReadExpr(IReadOnlyList<string> tokens, ref int i)
    {
        if (i >= tokens.Count) throw new ParseException("Unexpected end of input.");
        var tok = tokens[i++];

        if (tok == "(")
        {
            var items = new List<Node>();
            while (i < tokens.Count && tokens[i] != ")") items.Add(ReadExpr(tokens, ref i));
            if (i >= tokens.Count) throw new ParseException("Unclosed '('.");
            i++; // consume ')'
            return new SList(items);
        }
        if (tok == ")") throw new ParseException("Unexpected ')'.");
        return ClassifyAtom(tok);
    }

    private static Atom ClassifyAtom(string tok)
    {
        if (tok.Length >= 2 && tok[0] == '"' && tok[^1] == '"')
            return new Atom(Unescape(tok[1..^1]), AtomKind.String);
        if (tok == "true") return new Atom("true", AtomKind.True);
        if (tok == "false") return new Atom("false", AtomKind.False);
        if (double.TryParse(tok, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _))
            return new Atom(tok, AtomKind.Number);
        return new Atom(tok, AtomKind.Symbol);
    }

    private static IReadOnlyList<string> Tokenize(string source)
    {
        var tokens = new List<string>();
        int n = source.Length, i = 0;
        while (i < n)
        {
            char c = source[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == ';')
            {
                while (i < n && source[i] != '\n') i++;
                continue;
            }
            if (c == '(' || c == ')')
            {
                tokens.Add(c.ToString());
                i++;
                continue;
            }
            if (c == '"')
            {
                int start = i++;
                var sb = new System.Text.StringBuilder();
                sb.Append('"');
                while (i < n && source[i] != '"')
                {
                    if (source[i] == '\\' && i + 1 < n)
                    {
                        sb.Append(source[i]).Append(source[i + 1]);
                        i += 2;
                    }
                    else sb.Append(source[i++]);
                }
                if (i >= n) throw new ParseException("Unterminated string.");
                sb.Append('"');
                tokens.Add(sb.ToString());
                i++; // consume closing "
                continue;
            }
            // atom — read until whitespace or paren
            int s = i;
            while (i < n && !char.IsWhiteSpace(source[i]) && source[i] != '(' && source[i] != ')')
                i++;
            tokens.Add(source[s..i]);
        }
        return tokens;
    }

    private static string Unescape(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                char e = s[++i];
                sb.Append(e switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '"' => '"',
                    '\\' => '\\',
                    _ => e
                });
            }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }
}

public sealed class ParseException : Exception
{
    public ParseException(string message) : base(message) { }
}
