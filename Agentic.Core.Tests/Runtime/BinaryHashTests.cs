using Agentic.Core.Runtime;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Runtime;

public sealed class BinaryHashTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { /* best-effort */ }
        }
    }

    private string WriteTempBinary(byte[] contents)
    {
        string path = Path.Combine(Path.GetTempPath(), $"agc-c6-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, contents);
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public void HashBinary_ShouldReturnStableSha256()
    {
        // Arrange
        var path = WriteTempBinary(new byte[] { 0x41, 0x42, 0x43 }); // "ABC"

        // Act
        string hash = ProofManifestBuilder.HashBinary(path);

        // Assert
        // echo -n ABC | sha256sum  => b5d4045c3f466fa91fe2cc6abe79232a1a57cdf104f7a26e716e0a1e2789df78
        hash.Should().Be("b5d4045c3f466fa91fe2cc6abe79232a1a57cdf104f7a26e716e0a1e2789df78");
    }

    [Fact]
    public void HashBinary_ShouldChangeOnSingleBitTamper()
    {
        // Arrange
        var original = WriteTempBinary(new byte[] { 0x00, 0x01, 0x02, 0x03 });
        var tampered = WriteTempBinary(new byte[] { 0x00, 0x01, 0x02, 0x04 });

        // Act
        string h1 = ProofManifestBuilder.HashBinary(original);
        string h2 = ProofManifestBuilder.HashBinary(tampered);

        // Assert
        h1.Should().NotBe(h2);
    }

    [Fact]
    public void SidecarPathFor_ShouldAppendManifestJsonExtension()
    {
        // Arrange + Act
        string sidecar = ProofManifestBuilder.SidecarPathFor("/tmp/my-app");

        // Assert
        sidecar.Should().Be("/tmp/my-app.manifest.json");
    }

    [Fact]
    public void WriteSidecar_ShouldPopulateBinaryHashField()
    {
        // Arrange
        var binaryPath = WriteTempBinary(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE });
        _tempFiles.Add(ProofManifestBuilder.SidecarPathFor(binaryPath));
        var manifest = new ProofManifest(
            SchemaVersion: "1.0",
            SourceHash: "deadbeef",
            Capabilities: new[] { "http.fetch" },
            Permissions: new[] { "http" },
            Tests: Array.Empty<EmbeddedTest>(),
            Contracts: Array.Empty<EmbeddedContract>(),
            BuiltAt: DateTime.UtcNow);

        // Act
        string sidecarPath = ProofManifestBuilder.WriteSidecar(binaryPath, manifest);

        // Assert
        File.Exists(sidecarPath).Should().BeTrue();
        var written = ProofManifest.FromJson(File.ReadAllText(sidecarPath));
        written.BinaryHash.Should().Be(ProofManifestBuilder.HashBinary(binaryPath));
        written.SourceHash.Should().Be("deadbeef");
        written.Capabilities.Should().ContainSingle().Which.Should().Be("http.fetch");
    }

    [Fact]
    public void WriteSidecar_ShouldNotMutateOriginalManifest()
    {
        // Arrange
        var binaryPath = WriteTempBinary(new byte[] { 0x01 });
        _tempFiles.Add(ProofManifestBuilder.SidecarPathFor(binaryPath));
        var manifest = new ProofManifest(
            SchemaVersion: "1.0",
            SourceHash: "x",
            Capabilities: Array.Empty<string>(),
            Permissions: Array.Empty<string>(),
            Tests: Array.Empty<EmbeddedTest>(),
            Contracts: Array.Empty<EmbeddedContract>(),
            BuiltAt: DateTime.UtcNow);

        // Act
        ProofManifestBuilder.WriteSidecar(binaryPath, manifest);

        // Assert
        manifest.BinaryHash.Should().Be(""); // original record untouched
    }

    [Fact]
    public void SidecarHash_ShouldStillMatchAfterBinaryUnchanged()
    {
        // Arrange
        var binaryPath = WriteTempBinary(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        _tempFiles.Add(ProofManifestBuilder.SidecarPathFor(binaryPath));
        var manifest = new ProofManifest("1.0", "src", Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<EmbeddedTest>(), Array.Empty<EmbeddedContract>(), DateTime.UtcNow);

        // Act
        ProofManifestBuilder.WriteSidecar(binaryPath, manifest);
        string recheck = ProofManifestBuilder.HashBinary(binaryPath);
        var loaded = ProofManifest.FromJson(File.ReadAllText(ProofManifestBuilder.SidecarPathFor(binaryPath)));

        // Assert
        loaded.BinaryHash.Should().Be(recheck);
    }

    [Fact]
    public void SidecarHash_ShouldNotMatchAfterBinaryTampered()
    {
        // Arrange
        var binaryPath = WriteTempBinary(new byte[] { 0x11, 0x22, 0x33 });
        _tempFiles.Add(ProofManifestBuilder.SidecarPathFor(binaryPath));
        var manifest = new ProofManifest("1.0", "src", Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<EmbeddedTest>(), Array.Empty<EmbeddedContract>(), DateTime.UtcNow);
        ProofManifestBuilder.WriteSidecar(binaryPath, manifest);
        var declared = ProofManifest.FromJson(File.ReadAllText(ProofManifestBuilder.SidecarPathFor(binaryPath))).BinaryHash;

        // Act — flip a single byte
        File.WriteAllBytes(binaryPath, new byte[] { 0x11, 0x22, 0x34 });
        string actual = ProofManifestBuilder.HashBinary(binaryPath);

        // Assert
        actual.Should().NotBe(declared);
    }

    [Fact]
    public void ProofManifest_WithBinaryHash_ShouldRoundTripThroughJson()
    {
        // Arrange
        var manifest = new ProofManifest(
            SchemaVersion: "1.0",
            SourceHash: "aaa",
            Capabilities: new[] { "env.get" },
            Permissions: new[] { "env" },
            Tests: Array.Empty<EmbeddedTest>(),
            Contracts: Array.Empty<EmbeddedContract>(),
            BuiltAt: new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc),
            BinaryHash: "bbb");

        // Act
        string json = manifest.ToJson();
        var back = ProofManifest.FromJson(json);

        // Assert
        back.BinaryHash.Should().Be("bbb");
        back.SchemaVersion.Should().Be("1.0");
    }

    [Fact]
    public void ProofManifest_WithoutBinaryHash_ShouldDeserializeAsEmpty()
    {
        // Arrange: legacy manifest JSON (pre-C6) has no BinaryHash property.
        const string legacyJson = """
        {
          "SchemaVersion": "1.0",
          "SourceHash": "xyz",
          "Capabilities": [],
          "Permissions": [],
          "Tests": [],
          "Contracts": [],
          "BuiltAt": "2026-01-01T00:00:00Z"
        }
        """;

        // Act
        var manifest = ProofManifest.FromJson(legacyJson);

        // Assert
        manifest.BinaryHash.Should().Be("");
        manifest.SourceHash.Should().Be("xyz");
    }
}
