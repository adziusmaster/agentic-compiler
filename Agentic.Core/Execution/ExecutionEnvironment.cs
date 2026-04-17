using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Agentic.Core.Runtime;
using Agentic.Core.Syntax;

namespace Agentic.Core.Execution;

// Manages variable scopes (call stack), the user-defined function registry,
// and the user-declared (defstruct ...) type registry.
// The Verifier owns an instance of this; it never touches the stack directly.
internal sealed class ExecutionEnvironment
{
    public TypeRegistry Types { get; } = new();

    // Caps recursion depth in the verifier so runaway self-calls surface a readable
    // diagnostic instead of crashing the host with StackOverflowException.
    // Each verifier frame consumes several C# host-stack frames (Evaluate → switch →
    // ExecuteFunctionCall → Evaluate …), so the budget is intentionally modest.
    public const int MaxCallDepth = 192;

    private readonly Stack<Dictionary<string, object>> _callStack = new();
    private readonly Dictionary<string, ListNode> _functions = new();

    public ExecutionEnvironment()
    {
        _callStack.Push(new Dictionary<string, object>()); // global frame
    }

    public int CallDepth => _callStack.Count - 1;

    // ── Variable binding ──────────────────────────────────────────────────────

    // Defines a variable in the innermost (current) frame — used by (def ...)
    public object Define(string name, object value) => _callStack.Peek()[name] = value;

    // Assigns to an already-declared variable, searching from inner to global frame — used by (set ...)
    public object Set(string name, object value)
    {
        if (_callStack.Peek().ContainsKey(name)) return _callStack.Peek()[name] = value;
        var global = _callStack.ToArray()[^1];
        if (global.ContainsKey(name)) return global[name] = value;
        throw new System.Exception($"Memory Segfault: variable '{name}' not declared before set.");
    }

    // Resolves a variable, searching from inner to global frame — used by identifier atoms
    public object Get(string name)
    {
        if (_callStack.Peek().TryGetValue(name, out var v)) return v;
        var global = _callStack.ToArray()[^1];
        if (global.TryGetValue(name, out var g)) return g;
        return name; // fallback: unresolved identifier echoes its name (treats it as a string literal)
    }

    // ── Call stack management ─────────────────────────────────────────────────

    public void PushFrame(Dictionary<string, object> frame)
    {
        if (_callStack.Count >= MaxCallDepth)
            throw new InvalidOperationException(
                $"Stack overflow: call depth exceeded {MaxCallDepth}. " +
                "A recursive function is likely missing its base case. " +
                "Ensure every (defun ...) that calls itself has an (if ...) branch that returns without recursing.");
        // Belt-and-braces: if the host C# stack is close to its limit even within the
        // logical cap, convert the InsufficientExecutionStackException into our readable form.
        try { RuntimeHelpers.EnsureSufficientExecutionStack(); }
        catch (InsufficientExecutionStackException)
        {
            throw new InvalidOperationException(
                "Stack overflow: host call stack exhausted during recursion. " +
                "Check that every recursive (defun ...) has a terminating base case.");
        }
        _callStack.Push(frame);
    }

    public void PopFrame() => _callStack.Pop();

    // ── User-defined function registry ───────────────────────────────────────

    public void RegisterFunction(string name, ListNode definition) => _functions[name] = definition;

    public bool TryGetFunction(string name, out ListNode definition) =>
        _functions.TryGetValue(name, out definition!);
}
