namespace Agentic.Core.Syntax;

/// <summary>
/// Enumerates the kinds of lexical tokens recognized by the Agentic lexer.
/// </summary>
public enum TokenType
{
    /// <summary>
    /// The '(' token that begins a list expression.
    /// </summary>
    OpenParen,

    /// <summary>
    /// The ')' token that ends a list expression.
    /// </summary>
    CloseParen,

    /// <summary>
    /// An identifier token, such as a function or variable name.
    /// </summary>
    Identifier,

    /// <summary>
    /// A quoted string literal token.
    /// </summary>
    String,

    /// <summary>
    /// A numeric literal token.
    /// </summary>
    Number,

    /// <summary>
    /// Synthetic token inserted at the end of the input.
    /// </summary>
    EndOfFile,

    /// <summary>
    /// A token representing a lexical error.
    /// </summary>
    Error
}

/// <summary>
/// Represents a lexical token produced by the lexer.
/// </summary>
/// <param name="Type">The kind of token.</param>
/// <param name="Value">The source text or value associated with the token.</param>
/// <param name="Line">The line number where the token begins.</param>
/// <param name="Column">The column number where the token begins.</param>
public readonly record struct Token(TokenType Type, string Value, int Line, int Column);