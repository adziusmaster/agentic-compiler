namespace Agentic.Core.Stdlib;

/// <summary>
/// File I/O operations. Verifier uses an in-memory virtual filesystem
/// so tests can exercise file logic without touching disk.
/// Transpiler emits <c>System.IO.File</c> calls.
/// </summary>
public sealed class FileModule : IStdlibModule
{
    private readonly Dictionary<string, string> _virtualFs = new();

    /// <summary>The in-memory virtual filesystem used during verification.</summary>
    public IReadOnlyDictionary<string, string> VirtualFs => _virtualFs;

    public void Register(StdlibRegistry registry)
    {
        // Verifier side — operates on in-memory virtual filesystem
        registry.VerifierFuncs["file.read"] = args =>
        {
            var path = args[0]?.ToString() ?? throw new ArgumentException("file.read: path is null");
            return _virtualFs.TryGetValue(path, out var content)
                ? content
                : throw new FileNotFoundException($"file.read: file not found: {path}");
        };

        registry.VerifierFuncs["file.write"] = args =>
        {
            var path = args[0]?.ToString() ?? throw new ArgumentException("file.write: path is null");
            var content = args[1]?.ToString() ?? "";
            _virtualFs[path] = content;
            return 0.0;
        };

        registry.VerifierFuncs["file.append"] = args =>
        {
            var path = args[0]?.ToString() ?? throw new ArgumentException("file.append: path is null");
            var content = args[1]?.ToString() ?? "";
            _virtualFs[path] = _virtualFs.TryGetValue(path, out var existing)
                ? existing + content
                : content;
            return 0.0;
        };

        registry.VerifierFuncs["file.exists"] = args =>
        {
            var path = args[0]?.ToString() ?? throw new ArgumentException("file.exists: path is null");
            return _virtualFs.ContainsKey(path) ? 1.0 : 0.0;
        };

        registry.VerifierFuncs["file.delete"] = args =>
        {
            var path = args[0]?.ToString() ?? throw new ArgumentException("file.delete: path is null");
            _virtualFs.Remove(path);
            return 0.0;
        };

        // Transpiler side — emits real System.IO.File calls
        registry.TranspilerEmitters["file.read"] = (args, r) =>
            $"System.IO.File.ReadAllText({r(args[0])})";

        registry.TranspilerEmitters["file.write"] = (args, r) =>
            $"System.IO.File.WriteAllText({r(args[0])}, {r(args[1])})";

        registry.TranspilerEmitters["file.append"] = (args, r) =>
            $"System.IO.File.AppendAllText({r(args[0])}, {r(args[1])})";

        registry.TranspilerEmitters["file.exists"] = (args, r) =>
            $"(System.IO.File.Exists({r(args[0])}) ? 1.0 : 0.0)";

        registry.TranspilerEmitters["file.delete"] = (args, r) =>
            $"System.IO.File.Delete({r(args[0])})";

        // Permission requirements
        registry.PermissionRequirements["file.read"] = "file.read";
        registry.PermissionRequirements["file.write"] = "file.write";
        registry.PermissionRequirements["file.append"] = "file.write";
        registry.PermissionRequirements["file.exists"] = "file.read";
        registry.PermissionRequirements["file.delete"] = "file.write";
    }
}
