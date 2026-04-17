using Agentic.Core.Syntax;

namespace Agentic.Core.Execution;

/// <summary>
/// Metadata extracted from a parsed .ag module file.
/// </summary>
internal sealed record ModuleInfo(
    string Name,
    string FilePath,
    AstNode Ast,
    IReadOnlyList<string> Imports);

/// <summary>
/// Compiles multiple .ag files in a directory as a single project.
/// Resolves module dependencies via import graph and merges into a combined AST.
/// </summary>
public sealed class ProjectCompiler
{
    private readonly bool _emitBinary;

    public ProjectCompiler(bool emitBinary = true)
    {
        _emitBinary = emitBinary;
    }

    /// <summary>
    /// Compiles all .ag files in the specified directory as a project.
    /// </summary>
    public CompileResult CompileDirectory(string directoryPath, string outputName = "program")
    {
        if (!Directory.Exists(directoryPath))
        {
            return new CompileResult
            {
                Success = false,
                Diagnostics = new[]
                {
                    new CompileDiagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Type = "project-error",
                        Message = $"Directory not found: {directoryPath}",
                        FixHint = "Provide a valid directory path containing .ag files."
                    }
                }
            };
        }

        var agFiles = Directory.GetFiles(directoryPath, "*.ag", SearchOption.AllDirectories);
        if (agFiles.Length == 0)
        {
            return new CompileResult
            {
                Success = false,
                Diagnostics = new[]
                {
                    new CompileDiagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Type = "project-error",
                        Message = $"No .ag files found in {directoryPath}",
                        FixHint = "Add .ag source files to the directory."
                    }
                }
            };
        }

        var modules = new List<ModuleInfo>();
        var diagnostics = new List<CompileDiagnostic>();

        foreach (var file in agFiles)
        {
            try
            {
                string source = File.ReadAllText(file);
                var tokens = new Lexer(source).Tokenize();
                var ast = new Parser(tokens).Parse();
                var info = ExtractModuleInfo(file, ast);
                modules.Add(info);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new CompileDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Type = "parse-error",
                    Message = $"Failed to parse {Path.GetFileName(file)}: {ex.Message}",
                    FixHint = "Check S-expression syntax in this file."
                });
            }
        }

        if (diagnostics.Count > 0)
        {
            return new CompileResult
            {
                Success = false,
                Diagnostics = diagnostics
            };
        }

        IReadOnlyList<ModuleInfo> sorted;
        try
        {
            sorted = TopologicalSort(modules);
        }
        catch (Exception ex)
        {
            return new CompileResult
            {
                Success = false,
                Diagnostics = new[]
                {
                    new CompileDiagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Type = "import-error",
                        Message = ex.Message,
                        FixHint = "Check module imports for circular dependencies or missing modules."
                    }
                }
            };
        }

        var mergedAst = MergeModules(sorted);

        var compiler = new Compiler(_emitBinary);
        return compiler.Compile(mergedAst, outputName);
    }

    /// <summary>
    /// Extracts module name and imports from a parsed AST.
    /// </summary>
    internal static ModuleInfo ExtractModuleInfo(string filePath, AstNode ast)
    {
        string moduleName = Path.GetFileNameWithoutExtension(filePath);
        var imports = new List<string>();

        if (ast is ListNode root && root.Elements.Count > 0)
        {
            var op = (root.Elements[0] as AtomNode)?.Token.Value;

            if (op == "module" && root.Elements.Count > 1)
            {
                moduleName = (root.Elements[1] as AtomNode)?.Token.Value ?? moduleName;

                for (int i = 2; i < root.Elements.Count; i++)
                    CollectImports(root.Elements[i], imports);
            }
            else if (op == "do")
            {
                for (int i = 1; i < root.Elements.Count; i++)
                    CollectImports(root.Elements[i], imports);
            }
        }

        return new ModuleInfo(moduleName, filePath, ast, imports);
    }

    private static void CollectImports(AstNode node, List<string> imports)
    {
        if (node is ListNode list && list.Elements.Count >= 2)
        {
            var op = (list.Elements[0] as AtomNode)?.Token.Value;
            if (op == "import")
            {
                var target = (list.Elements[1] as AtomNode)?.Token.Value;
                if (target is not null && target.StartsWith("./"))
                    imports.Add(target[2..]);
            }
        }
    }

    /// <summary>
    /// Topologically sorts modules based on their import dependencies.
    /// Throws if a circular dependency is detected.
    /// </summary>
    internal static IReadOnlyList<ModuleInfo> TopologicalSort(IReadOnlyList<ModuleInfo> modules)
    {
        var lookup = modules.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
        var sorted = new List<ModuleInfo>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in modules)
            Visit(module.Name, lookup, sorted, visited, visiting);

        return sorted;
    }

    private static void Visit(
        string name,
        Dictionary<string, ModuleInfo> lookup,
        List<ModuleInfo> sorted,
        HashSet<string> visited,
        HashSet<string> visiting)
    {
        if (visited.Contains(name)) return;

        if (!visiting.Add(name))
            throw new InvalidOperationException($"Circular dependency detected involving module '{name}'.");

        if (lookup.TryGetValue(name, out var module))
        {
            foreach (var import in module.Imports)
            {
                if (!lookup.ContainsKey(import))
                    throw new InvalidOperationException($"Module '{name}' imports '{import}' which was not found in the project.");
                Visit(import, lookup, sorted, visited, visiting);
            }

            sorted.Add(module);
        }

        visiting.Remove(name);
        visited.Add(name);
    }

    /// <summary>
    /// Merges sorted module ASTs into a single <c>(do …)</c> block.
    /// Module wrappers are unwrapped — only their body expressions are included.
    /// </summary>
    internal static AstNode MergeModules(IReadOnlyList<ModuleInfo> sorted)
    {
        var allExpressions = new List<AstNode>();

        var doToken = new Token(TokenType.Identifier, "do", 0, 0);
        allExpressions.Add(new AtomNode(doToken));

        foreach (var module in sorted)
        {
            var bodies = ExtractBodyExpressions(module.Ast);
            allExpressions.AddRange(bodies);
        }

        return new ListNode(allExpressions);
    }

    /// <summary>
    /// Extracts the body expressions from a module/do wrapper,
    /// skipping import/export declarations.
    /// </summary>
    private static IEnumerable<AstNode> ExtractBodyExpressions(AstNode ast)
    {
        if (ast is not ListNode root || root.Elements.Count == 0)
            yield break;

        var op = (root.Elements[0] as AtomNode)?.Token.Value;
        int startIndex;

        if (op == "module")
            startIndex = 2;
        else if (op == "do")
            startIndex = 1;
        else
        {
            yield return ast;
            yield break;
        }

        for (int i = startIndex; i < root.Elements.Count; i++)
        {
            var element = root.Elements[i];
            if (element is ListNode list && list.Elements.Count >= 1)
            {
                var childOp = (list.Elements[0] as AtomNode)?.Token.Value;
                if (childOp is "import" or "export")
                    continue;
            }
            yield return element;
        }
    }
}
