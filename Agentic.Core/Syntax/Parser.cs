namespace Agentic.Core.Syntax;

/// <summary>
/// Parses a stream of lexical tokens into an Agentic abstract syntax tree (AST).
/// </summary>
public sealed class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private int _position;

    /// <summary>
    /// Creates a new parser for the supplied token stream.
    /// </summary>
    /// <param name="tokens">The token list produced by the lexer.</param>
    public Parser(IReadOnlyList<Token> tokens)
    {
        _tokens = tokens;
    }

    /// <summary>
    /// Parses the token stream and returns the root AST node.
    /// </summary>
    /// <returns>The parsed AST for the supplied tokens.</returns>
    public AstNode Parse()
    {
        if (IsAtEnd) throw new InvalidOperationException("Cannot parse empty token stream.");
        return ParseExpression();
    }

    /// <summary>
    /// Parses either an atom or a list expression.
    /// </summary>
    /// <returns>An <see cref="AstNode"/> representing the current expression.</returns>
    private AstNode ParseExpression()
    {
        var token = Current;

        if (token.Type == TokenType.OpenParen)
        {
            return ParseList();
        }

        Advance();
        return new AtomNode(token) { Span = SourceSpan.FromToken(token) };
    }

    /// <summary>
    /// Parses a parenthesized list expression.
    /// </summary>
    /// <returns>A <see cref="ListNode"/> containing the parsed child expressions.</returns>
    private ListNode ParseList()
    {
        var openParen = Current;
        Advance();
        var elements = new List<AstNode>();

        while (!IsAtEnd && Current.Type != TokenType.CloseParen)
        {
            elements.Add(ParseExpression());
        }

        if (IsAtEnd)
        {
            throw new Exception($"Compile Error: Missing closing parenthesis. Scope unclosed at line {Current.Line}.");
        }

        var closeParen = Current;
        Advance();
        return new ListNode(elements)
        {
            Span = new SourceSpan(openParen.Line, openParen.Column, closeParen.Line, closeParen.Column + 1)
        };
    }

    /// <summary>
    /// Gets the current token that has not yet been consumed.
    /// </summary>
    private Token Current => _position < _tokens.Count ? _tokens[_position] : _tokens[^1];

    /// <summary>
    /// Advances the parser to the next token.
    /// </summary>
    private void Advance() => _position++;

    /// <summary>
    /// Determines whether the parser has reached the end of the token stream.
    /// </summary>
    private bool IsAtEnd => Current.Type == TokenType.EndOfFile;
}