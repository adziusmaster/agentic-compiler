using Agentic.Check;

// Entry-point for the independent checker `agc-check`.
//
// Usage: agc-check <binary> [--source <file.ag>] [--policy safety|strict]
//
// Exit codes:
//   0  accepted
//   1  rejected (well-formedness, CS, TC, or CV failed)
//   2  usage / I/O error
//
// All verdict logic lives in Checker.Run — this file is just argv parsing
// and CheckResult → stdout/exit-code translation.

void PrintUsage()
{
    Console.WriteLine("Usage: agc-check <binary> [--source <file.ag>] [--policy safety|strict]");
    Console.WriteLine();
    Console.WriteLine("  --source <file>    Re-hash the .ag source and compare against manifest.SourceHash.");
    Console.WriteLine("  --policy safety    (default) enforce CS + TC + CV (docs/safety-policy.md).");
    Console.WriteLine("  --policy strict    as 'safety' plus: observed ⊊ declared counts as reject.");
    Console.WriteLine();
    Console.WriteLine("Exit: 0 accept  |  1 reject  |  2 usage/io error.");
}

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintUsage();
    return args.Length == 0 ? 2 : 0;
}

string binaryPath = args[0];
string? sourcePath = null;
string policy = "safety";
for (int i = 1; i < args.Length; i++)
{
    if (args[i] == "--source" && i + 1 < args.Length) { sourcePath = args[++i]; continue; }
    if (args[i] == "--policy" && i + 1 < args.Length) { policy = args[++i]; continue; }
    Console.Error.WriteLine($"Unknown argument: {args[i]}");
    PrintUsage();
    return 2;
}

if (policy is not ("safety" or "strict"))
{
    Console.Error.WriteLine($"Unknown policy: {policy} (expected 'safety' or 'strict')");
    return 2;
}

CheckResult result;
try { result = Checker.Run(binaryPath, sourcePath, policy); }
catch (Exception ex)
{
    Console.Error.WriteLine($"internal error: {ex.GetType().Name}: {ex.Message}");
    return 2;
}

if (result.Verdict == Verdict.Reject)
{
    // "io-error" and "source-missing" are pre-check argv/IO problems → exit 2.
    int code = result.Code is "io-error" or "source-missing" ? 2 : 1;
    Console.Error.WriteLine($"reject: {result.Code}");
    Console.Error.WriteLine($"  {result.Message}");
    return code;
}

Console.WriteLine("accept");
Console.WriteLine($"  binary           : {binaryPath}");
Console.WriteLine($"  declared-caps    : {(result.DeclaredCapabilities.Count == 0 ? "(none)" : string.Join(", ", result.DeclaredCapabilities))}");
Console.WriteLine($"  observed-caps    : {(result.ObservedCapabilities.Count == 0 ? "(none)" : string.Join(", ", result.ObservedCapabilities))}");
Console.WriteLine($"  tests-passed     : {result.TestsPassed}/{result.TestsTotal}");
return 0;
