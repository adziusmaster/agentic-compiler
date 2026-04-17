using System.Runtime.CompilerServices;
using Agentic.Core.Runtime;
using Agentic.Core.Syntax;

namespace Agentic.Core.Execution;

/// <summary>
/// Manages variable scopes (call stack), the user-defined function registry,
/// and the <c>(defstruct …)</c> type registry during verification.
/// </summary>
internal sealed class ExecutionEnvironment
{
    /// <summary>Registry of user-declared struct types.</summary>
    public TypeRegistry Types { get; } = new();

    /// <summary>
    /// Maximum logical recursion depth. Caps recursive calls so runaway
    /// self-calls produce a readable diagnostic rather than a host crash.
    /// </summary>
    public const int MaxCallDepth = 192;

    private readonly Stack<Dictionary<string, object>> _callStack = new();
    private readonly Dictionary<string, ListNode> _functions = new();

    public ExecutionEnvironment()
    {
        _callStack.Push(new Dictionary<string, object>());
    }

    public int CallDepth => _callStack.Count - 1;

    /// <summary>Defines a variable in the innermost (current) frame.</summary>
    public object Define(string name, object value) => _callStack.Peek()[name] = value;

    /// <summary>Assigns to an already-declared variable, searching inner → global.</summary>
    public object Set(string name, object value)
    {
        if (_callStack.Peek().ContainsKey(name)) return _callStack.Peek()[name] = value;
        var global = _callStack.ToArray()[^1];
        if (global.ContainsKey(name)) return global[name] = value;
        throw new Exception($"Memory Segfault: variable '{name}' not declared before set.");
    }

    /// <summary>Resolves a variable, searching inner → global. Falls back to the name as a string literal.</summary>
    public object Get(string name)
    {
        if (_callStack.Peek().TryGetValue(name, out var v)) return v;
        var global = _callStack.ToArray()[^1];
        if (global.TryGetValue(name, out var g)) return g;
        return name;
    }

    /// <summary>Pushes a new call frame, guarding against stack overflow.</summary>
    public void PushFrame(Dictionary<string, object> frame)
    {
        if (_callStack.Count >= MaxCallDepth)
            throw new InvalidOperationException(
                $"Stack overflow: call depth exceeded {MaxCallDepth}. " +
                "Check that every recursive function has a base case.");
        try { RuntimeHelpers.EnsureSufficientExecutionStack(); }
        catch (InsufficientExecutionStackException)
        {
            throw new InvalidOperationException(
                "Stack overflow: host call stack exhausted. " +
                "Check that every recursive function has a base case.");
        }
        _callStack.Push(frame);
    }

    public void PopFrame() => _callStack.Pop();

    /// <summary>Registers a user-defined function AST node by name.</summary>
    public void RegisterFunction(string name, ListNode definition) => _functions[name] = definition;

    public bool TryGetFunction(string name, out ListNode definition) =>
        _functions.TryGetValue(name, out definition!);
}
