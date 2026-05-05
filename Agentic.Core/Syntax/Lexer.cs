using System.Text;

namespace Agentic.Core.Syntax;

/// <summary>
/// Converts raw Agentic source text into a sequence of lexical tokens.
/// </summary>
/// <remarks>
/// The lexer recognizes parentheses, string literals, identifiers, numbers, and
/// tracks source positions for error reporting.
/// </remarks>
public sealed class Lexer
{
    private readonly string _source;
    private int _position;
    private int _line = 1;
    private int _column = 1;

    /// <summary>
    /// Creates a new lexer for the specified source text.
    /// </summary>
    /// <param name="source">The Agentic source code to tokenize.</param>
    public Lexer(string source)
    {
        _source = source;
    }

    /// <summary>
    /// Tokenizes the source text into a list of <see cref="Token"/> instances.
    /// </summary>
    /// <returns>The lexed tokens, including a terminating EndOfFile token.</returns>
    public IReadOnlyList<Token> Tokenize()
    {
        var tokens = new List<Token>();

        while (_position < _source.Length)
        {
            char current = Current;

            if (char.IsWhiteSpace(current))
            {
                Advance();
                continue;
            }

            // Line comments: skip from ';' to end of line
            if (current == ';')
            {
                while (Current != '\0' && Current != '\n')
                    Advance();
                continue;
            }

            if (current == '(')
            {
                tokens.Add(new Token(TokenType.OpenParen, "(", _line, _column));
                Advance();
                continue;
            }

            if (current == ')')
            {
                tokens.Add(new Token(TokenType.CloseParen, ")", _line, _column));
                Advance();
                continue;
            }

            if (current == '"')
            {
                tokens.Add(ReadString());
                continue;
            }

            tokens.Add(ReadSymbol());
        }

        tokens.Add(new Token(TokenType.EndOfFile, "\0", _line, _column));
        return tokens;
    }

    /// <summary>
    /// Gets the current character at the lexer's read position,
    /// or the null character if the end of source has been reached.
    /// </summary>
    private char Current => _position < _source.Length ? _source[_position] : '\0';

    /// <summary>
    /// Advances the read position by one character and updates line/column state.
    /// </summary>
    private void Advance()
    {
        if (Current == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }
        _position++;
    }

    /// <summary>
    /// Reads a quoted string literal token from the source text.
    /// </summary>
    /// <returns>The parsed string token, or an error token for unterminated strings.</returns>
    private Token ReadString()
    {
        int startColumn = _column;
        int startLine = _line;
        Advance();

        var sb = new StringBuilder();
        while (Current != '"' && Current != '\0')
        {
            if (Current == '\\')
            {
                Advance();
                sb.Append(Current switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    _ => Current
                });
            }
            else
            {
                sb.Append(Current);
            }
            Advance();
        }

        if (Current == '\0')
        {
            return new Token(TokenType.Error, "Unterminated string literal", startLine, startColumn);
        }

        Advance();
        return new Token(TokenType.String, sb.ToString(), startLine, startColumn);
    }

    /// <summary>
    /// Reads an identifier or numeric symbol token from the source text.
    /// </summary>
    /// <returns>A Number token if the symbol parses as a double; otherwise an Identifier token.</returns>
    private Token ReadSymbol()
    {
        int startColumn = _column;
        int startLine = _line;
        var sb = new StringBuilder();

        while (Current != '\0' && !char.IsWhiteSpace(Current) && Current != '(' && Current != ')')
        {
            sb.Append(Current);
            Advance();
        }

        string value = NormalizeIdentifier(sb.ToString());
        TokenType type = double.TryParse(value, out _) ? TokenType.Number : TokenType.Identifier;

        return new Token(type, value, startLine, startColumn);
    }

    // LLM-friendly aliases: the language accepts `math_pow` and `str_eq` as
    // equivalent to `math.pow` / `str.eq`. BPE tokenizers don't split the
    // underscore form so the model emits fewer tokens. Only PURE stdlib
    // namespaces are normalized; capability namespaces (env/file/http/db/...)
    // intentionally stay distinct so the user's `(extern defun file_read …
    // @capability "file.read")` indirection routes through the mock layer.
    private static readonly HashSet<string> _pureStdlibPrefixes = new()
    {
        "math", "str", "arr", "map", "json"
    };

    private static string NormalizeIdentifier(string value)
    {
        // Lean assertion aliases: `eq?` → `assert-eq`, `near?` → `assert-near`.
        if (value == "eq?") return "assert-eq";
        if (value == "near?") return "assert-near";
        // Underscore-prefixed stdlib form → dotted form (math/str/arr/map/json only).
        int idx = value.IndexOf('_');
        if (idx > 0 && _pureStdlibPrefixes.Contains(value[..idx]))
            return string.Concat(value.AsSpan(0, idx), ".", value.AsSpan(idx + 1));
        return value;
    }
}