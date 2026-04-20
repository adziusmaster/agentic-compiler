using Agentic.Core.Syntax;

namespace Agentic.Core.Runtime;

/// <summary>
/// First-class function value — a reference to a user-declared <c>(defun …)</c>
/// that can be stored in variables, passed to functions, and invoked by the Verifier.
/// The name doubles as the C# method name at transpile time.
/// </summary>
public sealed record AgFunc(string Name, ListNode Definition);
