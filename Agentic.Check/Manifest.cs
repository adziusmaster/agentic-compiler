using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agentic.Check;

// Independent manifest reader. Deliberately duplicated from Agentic.Core's
// ProofManifest — the checker must not reference the compiler. BCL-only.
//
// TCB cost: ~80 LOC.

public sealed record CheckManifest(
    [property: JsonPropertyName("SchemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("SourceHash")] string SourceHash,
    [property: JsonPropertyName("Capabilities")] List<string> Capabilities,
    [property: JsonPropertyName("Permissions")] List<string> Permissions,
    [property: JsonPropertyName("Tests")] List<CheckTest> Tests,
    [property: JsonPropertyName("Contracts")] List<CheckContract> Contracts,
    [property: JsonPropertyName("BuiltAt")] DateTime BuiltAt,
    [property: JsonPropertyName("BinaryHash")] string BinaryHash = "");

public sealed record CheckTest(
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("SourceSnippet")] string SourceSnippet,
    [property: JsonPropertyName("ExpectedPasses")] int ExpectedPasses);

public sealed record CheckContract(
    [property: JsonPropertyName("Function")] string Function,
    [property: JsonPropertyName("Kind")] string Kind,
    [property: JsonPropertyName("SourceSnippet")] string SourceSnippet);

public static class ManifestLoader
{
    public static CheckManifest FromJson(string json) =>
        JsonSerializer.Deserialize<CheckManifest>(json)
        ?? throw new InvalidOperationException("Manifest deserialized as null.");

    public static CheckManifest FromSidecar(string binaryPath)
    {
        string sidecar = SidecarPath(binaryPath);
        if (!File.Exists(sidecar))
            throw new FileNotFoundException($"No manifest sidecar at '{sidecar}'.", sidecar);
        return FromJson(File.ReadAllText(sidecar));
    }

    public static string SidecarPath(string binaryPath) => binaryPath + ".manifest.json";

    public static string Sha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string Sha256HexOfFile(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string Sha256HexOfString(string s) => Sha256Hex(Encoding.UTF8.GetBytes(s));
}
