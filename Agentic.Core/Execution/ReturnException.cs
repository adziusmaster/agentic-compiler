namespace Agentic.Core.Execution;

// Used as a control-flow signal — thrown by (return ...) and caught by the function call dispatcher.
// Not a real error; lives here so Verifier and user-defined function dispatch can both reference it.
internal sealed class ReturnException : Exception
{
    public object? Value { get; }
    public ReturnException(object? value) { Value = value; }
}
