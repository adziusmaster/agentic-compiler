using Agentic.Core.Syntax;

namespace Agentic.Core.Execution;

/// <summary>
/// Metadata extracted from a loaded .ag module.
/// </summary>
internal sealed record LoadedModule(
    string FullPath,
    AstNode Ast,
    IReadOnlyList<string> ExportedSymbols);

/// <summary>
/// Resolves, parses, and caches imported .ag modules.
/// Detects circular imports and deduplicates diamond imports.
/// Thread-safe for a single compilation session.
/// </summary>
internal sealed class ModuleLoader
{
    private readonly string _basePath;
    private readonly Dictionary<string, LoadedModule> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loading = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="basePath">Directory of the root source file being compiled.</param>
    public ModuleLoader(string basePath)
    {
        _basePath = basePath;
    }

    /// <summary>
    /// Loads and parses an imported module by its import path (e.g. "./utils.ag").
    /// Returns a cached result if the module has already been loaded (diamond pattern).
    /// Throws on circular imports or missing files.
    /// </summary>
    public LoadedModule Load(string importPath)
    {
        string fullPath = ResolveImportPath(importPath);

        if (_cache.TryGetValue(fullPath, out var cached))
            return cached;

        if (!_loading.Add(fullPath))
            throw new InvalidOperationException(
                $"Circular import detected: '{importPath}' is already being loaded. " +
                "Break the cycle by restructuring module dependencies.");

        try
        {
            if (!File.Exists(fullPath))
                throw new InvalidOperationException(
                    $"Import not found: '{importPath}' (resolved to '{fullPath}'). " +
                    "Check the file path and ensure the .ag file exists.");

            string source = File.ReadAllText(fullPath);
            var tokens = new Lexer(source).Tokenize();
            var ast = new Parser(tokens).Parse();
            var exports = ExtractExports(ast);

            var module = new LoadedModule(fullPath, ast, exports);
            _cache[fullPath] = module;
            return module;
        }
        finally
        {
            _loading.Remove(fullPath);
        }
    }

    /// <summary>
    /// Creates a child loader for resolving imports relative to an imported module's directory.
    /// Shares the same cache and circular-detection state.
    /// </summary>
    public ModuleLoader ForDirectory(string moduleFullPath)
    {
        string dir = Path.GetDirectoryName(moduleFullPath) ?? _basePath;
        return new ModuleLoader(dir, _cache, _loading);
    }

    private ModuleLoader(string basePath, Dictionary<string, LoadedModule> cache, HashSet<string> loading)
    {
        _basePath = basePath;
        _cache = cache;
        _loading = loading;
    }

    private string ResolveImportPath(string importPath)
    {
        if (!importPath.StartsWith("./") && !importPath.StartsWith("../"))
            throw new InvalidOperationException(
                $"Invalid import path '{importPath}': file imports must start with './' or '../'.");

        // Add .ag extension if not present
        if (!importPath.EndsWith(".ag", StringComparison.OrdinalIgnoreCase))
            importPath += ".ag";

        return Path.GetFullPath(Path.Combine(_basePath, importPath));
    }

    /// <summary>
    /// Extracts the list of exported symbol names from a module AST.
    /// Looks for <c>(export name1 name2 ...)</c> declarations.
    /// </summary>
    private static IReadOnlyList<string> ExtractExports(AstNode ast)
    {
        var exports = new List<string>();

        if (ast is not ListNode root || root.Elements.Count == 0)
            return exports;

        var op = (root.Elements[0] as AtomNode)?.Token.Value;
        int startIndex = op == "module" ? 2 : op == "do" ? 1 : 0;

        for (int i = startIndex; i < root.Elements.Count; i++)
        {
            if (root.Elements[i] is ListNode child && child.Elements.Count >= 2)
            {
                var childOp = (child.Elements[0] as AtomNode)?.Token.Value;
                if (childOp == "export")
                {
                    for (int j = 1; j < child.Elements.Count; j++)
                    {
                        if (child.Elements[j] is AtomNode exportAtom)
                            exports.Add(exportAtom.Token.Value);
                    }
                }
            }
        }

        return exports;
    }
}
