namespace Agentic.Core.Syntax;

/// <summary>
/// Identifies a contiguous region of source text by its start and end line/column.
/// </summary>
/// <remarks>
/// Spans are anchored at the opening token of the construct they describe. Columns
/// are 1-based to match the Lexer's bookkeeping.
/// </remarks>
public readonly record struct SourceSpan(int StartLine, int StartColumn, int EndLine, int EndColumn)
{
    public static SourceSpan FromToken(Token token) =>
        new(token.Line, token.Column, token.Line, token.Column + token.Value.Length);
}
