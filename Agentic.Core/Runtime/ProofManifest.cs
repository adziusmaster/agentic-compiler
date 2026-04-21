using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agentic.Core.Capabilities;
using Agentic.Core.Stdlib;
using Agentic.Core.Syntax;

namespace Agentic.Core.Runtime;

/// <summary>
/// Structured manifest embedded into every compiled binary. Describes the
/// capabilities used, permissions granted at compile time, the set of tests
/// the source passed, and contracts declared — everything an auditor needs
/// to re-verify the binary without access to the original <c>.ag</c> source.
/// </summary>
public sealed record ProofManifest(
    string SchemaVersion,
    string SourceHash,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<EmbeddedTest> Tests,
    IReadOnlyList<EmbeddedContract> Contracts,
    DateTime BuiltAt,
    string BinaryHash = "")
{
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions
    {
        WriteIndented = true
    });

    public static ProofManifest FromJson(string json) =>
        JsonSerializer.Deserialize<ProofManifest>(json)
        ?? throw new InvalidOperationException("Manifest could not be deserialized.");
}

public sealed record EmbeddedTest(string Name, string SourceSnippet, int ExpectedPasses);

public sealed record EmbeddedContract(string Function, string Kind, string SourceSnippet);

/// <summary>
/// Extracts the structured manifest from an AST + compile context.
/// Consumed by the Transpiler (embeds as resource) and by `agc verify`
/// (reads back for independent audit).
/// </summary>
public static class ProofManifestBuilder
{
    public static ProofManifest Build(
        AstNode ast,
        string source,
        IReadOnlyCollection<Capability> capabilities,
        Permissions permissions,
        int testsPassed)
    {
        var tests = new List<EmbeddedTest>();
        var contracts = new List<EmbeddedContract>();
        Walk(ast, tests, contracts, currentFn: null);

        return new ProofManifest(
            SchemaVersion: "1.0",
            SourceHash: HashSource(source),
            Capabilities: capabilities.Select(c => c.Name).OrderBy(x => x).ToList(),
            Permissions: permissions.GrantedKeys().OrderBy(x => x).ToList(),
            Tests: tests.Select(t => t with { ExpectedPasses = testsPassed }).ToList(),
            Contracts: contracts,
            BuiltAt: DateTime.UtcNow);
    }

    private static void Walk(AstNode node, List<EmbeddedTest> tests, List<EmbeddedContract> contracts, string? currentFn)
    {
        if (node is not ListNode list || list.Elements.Count == 0) return;
        var op = (list.Elements[0] as AtomNode)?.Token.Value;

        if (op == "test" && list.Elements.Count >= 2 && list.Elements[1] is AtomNode nameAtom)
        {
            tests.Add(new EmbeddedTest(
                Name: nameAtom.Token.Value,
                SourceSnippet: AstToSexpr(list, maxLen: 200),
                ExpectedPasses: 0));
        }

        if (op == "defun" && list.Elements.Count >= 2 && list.Elements[1] is AtomNode fnAtom)
            currentFn = fnAtom.Token.Value;

        if ((op == "require" || op == "ensure") && currentFn is not null)
        {
            contracts.Add(new EmbeddedContract(
                Function: currentFn,
                Kind: op,
                SourceSnippet: AstToSexpr(list, maxLen: 120)));
        }

        foreach (var child in list.Elements) Walk(child, tests, contracts, currentFn);
    }

    private static string AstToSexpr(AstNode node, int maxLen)
    {
        var sb = new StringBuilder();
        Emit(node, sb);
        var s = sb.ToString();
        return s.Length > maxLen ? s[..maxLen] + "…" : s;
    }

    private static void Emit(AstNode node, StringBuilder sb)
    {
        if (node is AtomNode a)
        {
            if (a.Token.Type == TokenType.String) sb.Append('"').Append(a.Token.Value).Append('"');
            else sb.Append(a.Token.Value);
            return;
        }
        if (node is ListNode l)
        {
            sb.Append('(');
            for (int i = 0; i < l.Elements.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                Emit(l.Elements[i], sb);
            }
            sb.Append(')');
        }
    }

    public static string HashSource(string source)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// SHA256 of the binary at <paramref name="binaryPath"/>, lowercase hex.
    /// </summary>
    public static string HashBinary(string binaryPath)
    {
        using var stream = File.OpenRead(binaryPath);
        var bytes = SHA256.HashData(stream);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Sidecar manifest path for a given binary: `<binaryPath>.manifest.json`.
    /// The sidecar is the authoritative manifest post-C6. The embedded copy
    /// inside the binary is kept for `<bin> --verify` convenience but does
    /// not contain BinaryHash (chicken-and-egg).
    /// </summary>
    public static string SidecarPathFor(string binaryPath) =>
        binaryPath + ".manifest.json";

    /// <summary>
    /// Writes <paramref name="manifest"/>, extended with `BinaryHash = SHA256(β)`,
    /// to the sidecar path for the given binary. Returns the sidecar path.
    /// </summary>
    public static string WriteSidecar(string binaryPath, ProofManifest manifest)
    {
        var withHash = manifest with { BinaryHash = HashBinary(binaryPath) };
        var sidecarPath = SidecarPathFor(binaryPath);
        File.WriteAllText(sidecarPath, withHash.ToJson());
        return sidecarPath;
    }
}
