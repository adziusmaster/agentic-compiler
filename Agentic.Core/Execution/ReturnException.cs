namespace Agentic.Core.Execution;

/// <summary>
/// Control-flow signal thrown by <c>(return …)</c> and caught by the function call dispatcher.
/// Not a real error — used purely to unwind the call stack to the enclosing defun.
/// </summary>
internal sealed class ReturnException : Exception
{
    public object? Value { get; }
    public ReturnException(object? value) { Value = value; }
}
