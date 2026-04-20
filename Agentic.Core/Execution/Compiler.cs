using Agentic.Core.Runtime;
using Agentic.Core.Stdlib;
using Agentic.Core.Syntax;

namespace Agentic.Core.Execution;

/// <summary>
/// Deterministic compiler facade. Runs the full pipeline:
/// parse → type-check → verify (including tests/contracts) → transpile → emit.
/// No LLM. No network. Returns structured <see cref="CompileResult"/>.
/// Supports optional <see cref="CompilationCache"/> for incremental checking.
/// </summary>
public sealed class Compiler
{
    private readonly bool _emitBinary;
    private readonly CompilationCache? _cache;
    private readonly Permissions _permissions;

    /// <param name="emitBinary">When true, runs native AOT emission. When false, stops after transpile.</param>
    /// <param name="cache">Optional compilation cache for incremental checking.</param>
    /// <param name="permissions">I/O permissions for file/http/json operations. Defaults to none.</param>
    public Compiler(bool emitBinary = true, CompilationCache? cache = null, Permissions? permissions = null)
    {
        _emitBinary = emitBinary;
        _cache = cache;
        _permissions = permissions ?? Permissions.None;
    }

    /// <summary>
    /// Compiles an Agentic source string through the full pipeline.
    /// Returns a cached result if the source hash matches a previous successful build.
    /// </summary>
    /// <param name="source">The S-expression source code.</param>
    /// <param name="outputName">Name for the output binary (without extension).</param>
    /// <param name="basePath">Directory of the source file, for resolving relative imports.</param>
    public CompileResult Compile(string source, string outputName = "program", string? basePath = null)
    {
        if (_cache is not null)
        {
            var hash = CompilationCache.ComputeHash(source);
            if (_cache.TryGet(hash, out var cached))
                return cached!;
        }

        var diagnostics = new List<CompileDiagnostic>();

        AstNode ast;
        try
        {
            var tokens = new Lexer(source).Tokenize();
            ast = new Parser(tokens).Parse();
        }
        catch (Exception ex)
        {
            diagnostics.Add(new CompileDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Type = "parse-error",
                Message = ex.Message,
                FixHint = "Check S-expression syntax: matching parentheses, valid tokens."
            });
            return new CompileResult
            {
                Success = false,
                Diagnostics = diagnostics
            };
        }

        var result = Compile(ast, outputName, basePath, source);

        if (_cache is not null && result.Success)
            _cache.Store(CompilationCache.ComputeHash(source), result);

        return result;
    }

    /// <summary>
    /// Compiles a pre-parsed AST through the pipeline (type-check → verify → transpile → emit).
    /// </summary>
    public CompileResult Compile(AstNode ast, string outputName = "program", string? basePath = null, string? source = null)
    {
        var diagnostics = new List<CompileDiagnostic>();
        int testsPassed = 0;
        int testsFailed = 0;

        var moduleLoader = basePath != null ? new ModuleLoader(basePath) : null;

        var verifier = new Verifier(null, moduleLoader, null) { CollectAllErrors = true };
        try
        {
            verifier.Evaluate(ast);
            testsPassed = verifier.TestsPassed;
            testsFailed = verifier.TestsFailed;

            foreach (var failure in verifier.TestFailures)
            {
                diagnostics.Add(new CompileDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Type = "test-failure",
                    Message = failure.Message,
                    InContext = failure.TestName,
                    Expected = failure.Detail,
                    FixHint = $"Fix the function under test '{failure.TestName}' so all assertions pass."
                });
            }

            if (verifier.TestFailures.Count > 0)
            {
                return new CompileResult
                {
                    Success = false,
                    TestsPassed = testsPassed,
                    TestsFailed = testsFailed,
                    Diagnostics = diagnostics
                };
            }
        }
        catch (TestFailureException ex)
        {
            testsPassed = verifier.TestsPassed;
            testsFailed = verifier.TestsFailed + 1;
            diagnostics.Add(new CompileDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Type = "test-failure",
                Message = ex.Message,
                InContext = ex.TestName,
                Expected = ex.Detail,
                FixHint = $"Fix the function under test '{ex.TestName}' so all assertions pass."
            });
            return new CompileResult
            {
                Success = false,
                TestsPassed = testsPassed,
                TestsFailed = testsFailed,
                Diagnostics = diagnostics
            };
        }
        catch (ContractViolationException ex)
        {
            testsPassed = verifier.TestsPassed;
            diagnostics.Add(new CompileDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Type = "contract-violation",
                Message = ex.Message,
                InContext = ex.ContractKind,
                FixHint = ex.ContractKind == "require"
                    ? "Ensure the caller provides values that satisfy the precondition."
                    : "Ensure the function body produces a result that satisfies the postcondition."
            });
            return new CompileResult
            {
                Success = false,
                TestsPassed = testsPassed,
                Diagnostics = diagnostics
            };
        }
        catch (Exception ex) when (ex is not ReturnException)
        {
            testsPassed = verifier.TestsPassed;

            bool isInputFault = ex.Message.StartsWith("OS Fault:");
            if (isInputFault && testsFailed == 0)
            {
                // Fall through to transpile — main body is not under test
            }
            else
            {
                diagnostics.Add(new CompileDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Type = "runtime-error",
                    Message = ex.Message,
                    FixHint = "Review the expression that caused this runtime error."
                });
                return new CompileResult
                {
                    Success = false,
                    TestsPassed = testsPassed,
                    Diagnostics = diagnostics
                };
            }
        }

        string csharp;
        bool isServer;
        try
        {
            var transpiler = new Transpiler(_permissions, moduleLoader, null);
            transpiler.EmbeddedManifest = ProofManifestBuilder.Build(
                ast,
                source ?? string.Empty,
                verifier.Capabilities.All.Where(c => verifier.DeclaredCapabilities.Contains(c.Name)).ToList(),
                _permissions,
                testsPassed);
            csharp = transpiler.Transpile(ast);
            isServer = transpiler.IsServerMode;
        }
        catch (PermissionDeniedException ex)
        {
            diagnostics.Add(new CompileDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Type = "permission-denied",
                Message = ex.Message,
                FixHint = $"Grant the '{ex.Capability}' permission or remove the I/O operation."
            });
            return new CompileResult
            {
                Success = false,
                TestsPassed = testsPassed,
                Diagnostics = diagnostics
            };
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? $"{ex.Message} → {ex.InnerException.Message}" : ex.Message;
            diagnostics.Add(new CompileDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Type = "transpile-error",
                Message = detail,
                FixHint = "The transpiler failed generating C#. Ensure all functions have explicit type annotations: (defun name ((param : Type)) : ReturnType ...)."
            });
            return new CompileResult
            {
                Success = false,
                TestsPassed = testsPassed,
                Diagnostics = diagnostics
            };
        }

        if (!_emitBinary)
        {
            return new CompileResult
            {
                Success = true,
                TestsPassed = testsPassed,
                GeneratedSource = csharp,
                Diagnostics = diagnostics
            };
        }

        try
        {
            var emitter = new NativeEmitter();
            string binaryPath = emitter.Emit(csharp, outputName, isServer);
            return new CompileResult
            {
                Success = true,
                BinaryPath = binaryPath,
                TestsPassed = testsPassed,
                GeneratedSource = csharp,
                Diagnostics = diagnostics
            };
        }
        catch (Exception ex)
        {
            diagnostics.Add(new CompileDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Type = "emit-error",
                Message = ex.Message,
                FixHint = "C# compilation failed. Check the generated source for type mismatches.",
                Actual = csharp
            });
            return new CompileResult
            {
                Success = false,
                TestsPassed = testsPassed,
                GeneratedSource = csharp,
                Diagnostics = diagnostics
            };
        }
    }
}
