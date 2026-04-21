namespace Agentic.Check;

// Checker top-level logic, extracted from Program.cs so tests can drive it
// without spawning a subprocess.
//
// TCB cost: ~130 LOC (counted as part of the checker's verdict logic — the
// *decision* that a binary is accepted lives here).

public enum Verdict { Accept, Reject }

public sealed record CheckResult(
    Verdict Verdict,
    string Code,
    string Message,
    int TestsPassed,
    int TestsTotal,
    IReadOnlyCollection<string> ObservedCapabilities,
    IReadOnlyCollection<string> DeclaredCapabilities);

public sealed class Checker
{
    public static CheckResult Run(string binaryPath, string? sourcePath = null, string policy = "safety")
    {
        if (!File.Exists(binaryPath))
            return new(Verdict.Reject, "io-error", $"binary not found: {binaryPath}",
                0, 0, Array.Empty<string>(), Array.Empty<string>());

        // WF1–WF3
        CheckManifest manifest;
        try { manifest = ManifestLoader.FromSidecar(binaryPath); }
        catch (FileNotFoundException)
        {
            return new(Verdict.Reject, "no-manifest",
                $"No sidecar at '{ManifestLoader.SidecarPath(binaryPath)}'.",
                0, 0, Array.Empty<string>(), Array.Empty<string>());
        }

        if (manifest.SchemaVersion != "1.0")
            return Rej("well-formedness", $"WF1: unsupported schema '{manifest.SchemaVersion}'.", manifest);
        if (string.IsNullOrEmpty(manifest.BinaryHash))
            return Rej("well-formedness", "WF3: manifest has empty BinaryHash.", manifest);

        string actualBin = ManifestLoader.Sha256HexOfFile(binaryPath);
        if (!string.Equals(actualBin, manifest.BinaryHash, StringComparison.OrdinalIgnoreCase))
            return Rej("binary-tampered",
                $"WF3: SHA256(β) mismatch. declared={manifest.BinaryHash} actual={actualBin}", manifest);

        if (sourcePath is not null)
        {
            if (!File.Exists(sourcePath))
                return Rej("source-missing", $"source not found: {sourcePath}", manifest);
            string srcHash = ManifestLoader.Sha256HexOfString(File.ReadAllText(sourcePath));
            if (!string.Equals(srcHash, manifest.SourceHash, StringComparison.OrdinalIgnoreCase))
                return Rej("source-mismatch",
                    $"WF2: SHA256(σ) mismatch. declared={manifest.SourceHash} actual={srcHash}", manifest);
        }

        // CS
        var observed = CapabilityExtractor.Extract(binaryPath);
        var declared = new HashSet<string>(manifest.Capabilities);
        var extra = new HashSet<string>(observed);
        extra.ExceptWith(declared);
        if (extra.Count > 0)
            return Rej("capability-undeclared",
                $"CS: undeclared {string.Join(", ", extra)}", manifest, observed, declared);

        if (policy == "strict")
        {
            var missing = new HashSet<string>(declared);
            missing.ExceptWith(observed);
            if (missing.Count > 0)
                return Rej("capability-unused",
                    $"CS-strict: unused {string.Join(", ", missing)}", manifest, observed, declared);
        }

        // TC cross-check: manifest's own ExpectedPasses field must agree with Tests.Count.
        if (manifest.Tests.Count > 0)
        {
            int expected = manifest.Tests[0].ExpectedPasses;
            if (expected != manifest.Tests.Count)
                return Rej("test-count-mismatch",
                    $"TC: manifest claims ExpectedPasses={expected} but has {manifest.Tests.Count} tests.",
                    manifest, observed, declared);
        }

        // TC
        var (tcVerdict, tcCode, tcMsg, tcPassed) = RunTC(manifest, sourcePath);
        if (tcVerdict == Verdict.Reject)
            return new(Verdict.Reject, tcCode, tcMsg, tcPassed, manifest.Tests.Count, observed, declared);

        // CV (structural — full CV awaits C8)
        foreach (var c in manifest.Contracts)
        {
            try { _ = Parser.Parse(c.SourceSnippet); }
            catch (ParseException pe)
            {
                return Rej("contract-malformed",
                    $"CV: contract on '{c.Function}' ({c.Kind}): {pe.Message}", manifest, observed, declared);
            }
        }

        return new(Verdict.Accept, "accept", "all checks passed",
            tcPassed, manifest.Tests.Count, observed, declared);
    }

    private static (Verdict, string, string, int) RunTC(CheckManifest manifest, string? sourcePath)
    {
        if (manifest.Tests.Count == 0) return (Verdict.Accept, "accept", "no tests", 0);

        int passed = 0;
        if (sourcePath is null)
        {
            foreach (var t in manifest.Tests)
            {
                var interp = new ReferenceInterpreter(manifest.Capabilities);
                try
                {
                    var node = Parser.Parse(t.SourceSnippet);
                    interp.RunTest(node);
                }
                catch (Exception ex)
                { return (Verdict.Reject, "test-fail",
                    $"TC: test '{t.Name}' threw {ex.GetType().Name}: {ex.Message}", passed); }
                var entry = interp.Log.Entries.FirstOrDefault();
                if (entry.Status == "pass") passed++;
                else return (Verdict.Reject, "test-fail",
                    $"TC: test '{t.Name}' failed: {entry.Reason ?? "(no reason)"}", passed);
            }
            return (Verdict.Accept, "accept", "tc ok (snippet-only)", passed);
        }

        // With --source: pre-evaluate definitions, then run each test form.
        string srcText = File.ReadAllText(sourcePath);
        IReadOnlyList<Node> forms;
        try { forms = Parser.ParseAll(srcText); }
        catch (ParseException pe)
        { return (Verdict.Reject, "well-formedness", $"WF5: source parse: {pe.Message}", passed); }

        var topForms = new List<Node>();
        foreach (var f in forms)
        {
            if (f is SList sl && sl.Elements.Count > 0 && sl.Elements[0] is Atom a && a.Value == "module")
                for (int i = 2; i < sl.Elements.Count; i++) topForms.Add(sl.Elements[i]);
            else topForms.Add(f);
        }

        var interpShared = new ReferenceInterpreter(manifest.Capabilities);
        var testsByName = new Dictionary<string, Node>();
        var defKinds = new HashSet<string> { "defun", "defstruct", "extern", "def" };

        foreach (var form in topForms)
        {
            if (form is not SList sl || sl.Elements.Count == 0 || sl.Elements[0] is not Atom a) continue;
            if (a.Value == "test" && sl.Elements.Count >= 2 && sl.Elements[1] is Atom nm)
            { testsByName[nm.Value] = form; continue; }
            if (!defKinds.Contains(a.Value)) continue;
            try { interpShared.Run(new[] { form }); }
            catch (Exception ex)
            { return (Verdict.Reject, "source-eval-error",
                $"top-level '{a.Value}': {ex.Message}", passed); }
        }

        foreach (var t in manifest.Tests)
        {
            if (!testsByName.TryGetValue(t.Name, out var testForm))
                return (Verdict.Reject, "test-missing", $"TC: '{t.Name}' not found in source", passed);
            interpShared.RunTest(testForm);
            var entry = interpShared.Log.Entries[^1];
            if (entry.Status == "pass") passed++;
            else return (Verdict.Reject, "test-fail",
                $"TC: '{t.Name}' failed: {entry.Reason ?? "(no reason)"}", passed);
        }

        return (Verdict.Accept, "accept", "tc ok", passed);
    }

    private static CheckResult Rej(string code, string msg, CheckManifest m,
        HashSet<string>? observed = null, HashSet<string>? declared = null) =>
        new(Verdict.Reject, code, msg, 0, m.Tests.Count,
            observed ?? new HashSet<string>(),
            declared ?? new HashSet<string>(m.Capabilities));
}
