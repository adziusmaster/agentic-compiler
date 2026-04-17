namespace Agentic.Core.Syntax;

/// <summary>
/// Base type for all nodes in the Agentic abstract syntax tree (AST).
/// </summary>
/// <remarks>
/// The AST is produced by the parser and consumed by later compilation stages
/// such as type inference, verification, and code generation.
/// </remarks>
public abstract record AstNode
{
    /// <summary>
    /// Optional source-text location, populated by the Parser for diagnostics.
    /// </summary>
    public SourceSpan? Span { get; init; }
}

/// <summary>
/// Represents an atomic syntax element in the AST.
/// </summary>
/// <param name="Token">The token that this atomic node represents.</param>
public sealed record AtomNode(Token Token) : AstNode;

/// <summary>
/// Represents a list expression in the AST.
/// </summary>
/// <param name="Elements">The ordered child nodes contained by this list.</param>
public sealed record ListNode(IReadOnlyList<AstNode> Elements) : AstNode;
