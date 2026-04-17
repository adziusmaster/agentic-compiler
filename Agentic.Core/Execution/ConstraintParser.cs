using System.Text;

namespace Agentic.Core.Execution;

public sealed class ConstraintParser
{
    private enum ParserState { Root, Objective, Permissions, Tests, Functions }

    public ConstraintProfile Parse(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Compiler trap: Constraint file not found at '{filePath}'");

        return ParseLines(File.ReadAllLines(filePath));
    }

    internal ConstraintProfile ParseLines(string[] lines)
    {
        string version = string.Empty;
        string name = string.Empty;
        bool pipeline = false;
        var objective = new StringBuilder();
        var permissions = new List<string>();
        var tests = new List<ConstraintTest>();
        var functions = new List<FunctionSpec>();

        ParserState state = ParserState.Root;
        ConstraintTestBuilder? currentTest = null;
        FunctionSpecBuilder? currentFunction = null;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#')) continue;

            if (line.StartsWith("objective: |")) { FlushTest(ref currentTest, tests); FlushFunction(ref currentFunction, functions); state = ParserState.Objective; continue; }
            if (line.StartsWith("permissions:"))   { FlushTest(ref currentTest, tests); FlushFunction(ref currentFunction, functions); state = ParserState.Permissions; continue; }
            if (line.StartsWith("tests:"))         { FlushTest(ref currentTest, tests); FlushFunction(ref currentFunction, functions); state = ParserState.Tests; continue; }
            if (line.StartsWith("functions:"))     { FlushTest(ref currentTest, tests); FlushFunction(ref currentFunction, functions); state = ParserState.Functions; continue; }

            switch (state)
            {
                case ParserState.Root:
                    if (trimmed.StartsWith("version:")) version = ExtractValue(trimmed);
                    else if (trimmed.StartsWith("name:")) name = ExtractValue(trimmed);
                    else if (trimmed.StartsWith("pipeline:")) pipeline = ExtractValue(trimmed).Equals("true", System.StringComparison.OrdinalIgnoreCase);
                    break;

                case ParserState.Objective:
                    if (line.StartsWith("  ")) objective.AppendLine(trimmed);
                    else i--;
                    break;

                case ParserState.Permissions:
                    if (trimmed.StartsWith('-')) permissions.Add(trimmed[1..].Trim());
                    else i--;
                    break;

                case ParserState.Tests:
                    if (trimmed.StartsWith("- input:"))
                    {
                        FlushTest(ref currentTest, tests);
                        currentTest = new ConstraintTestBuilder();
                        FillInputs(ExtractValue(trimmed), currentTest.Inputs);
                    }
                    else if (trimmed.StartsWith("expect_stdout:") && currentTest != null)
                    {
                        currentTest.ExpectStdout = ExtractValue(trimmed).Trim('"');
                    }
                    else if (!line.StartsWith("  ")) { i--; }
                    break;

                case ParserState.Functions:
                    if (trimmed.StartsWith("- name:"))
                    {
                        FlushFunction(ref currentFunction, functions);
                        currentFunction = new FunctionSpecBuilder { Name = ExtractValue(trimmed).Trim('"') };
                    }
                    else if (trimmed.StartsWith("signature:") && currentFunction != null)
                    {
                        ParseSignature(ExtractValue(trimmed), currentFunction);
                    }
                    else if (trimmed.StartsWith("intent:") && currentFunction != null)
                    {
                        currentFunction.Intent = ExtractValue(trimmed).Trim('"');
                    }
                    else if (trimmed.StartsWith("- inputs:") && currentFunction != null)
                    {
                        var micro = new FunctionTestBuilder();
                        FillInputs(ExtractValue(trimmed), micro.Inputs);
                        currentFunction.PendingTests.Add(micro);
                    }
                    else if (trimmed.StartsWith("expect:") && currentFunction?.PendingTests.Count > 0)
                    {
                        currentFunction.PendingTests[^1].Expect = ExtractValue(trimmed).Trim('"');
                    }
                    else if (!line.StartsWith("  ")) { i--; }
                    break;
            }
        }

        FlushTest(ref currentTest, tests);
        FlushFunction(ref currentFunction, functions);

        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Contract missing 'name'.");
        if (objective.Length == 0) throw new InvalidOperationException("Contract missing 'objective'.");

        return new ConstraintProfile(
            version,
            name,
            objective.ToString().TrimEnd(),
            permissions,
            tests,
            functions.Count == 0 ? null : functions,
            pipeline);
    }

    private static void FlushTest(ref ConstraintTestBuilder? builder, List<ConstraintTest> sink)
    {
        if (builder != null) { sink.Add(builder.Build()); builder = null; }
    }

    private static void FlushFunction(ref FunctionSpecBuilder? builder, List<FunctionSpec> sink)
    {
        if (builder != null) { sink.Add(builder.Build()); builder = null; }
    }

    private static void FillInputs(string arrayPayload, List<string> target)
    {
        string stripped = arrayPayload.Trim().Trim('[', ']');
        if (string.IsNullOrWhiteSpace(stripped)) return;
        foreach (var item in stripped.Split(','))
            target.Add(item.Trim().Trim('"'));
    }

    private static void ParseSignature(string sig, FunctionSpecBuilder target)
    {
        string inner = sig.Trim().TrimStart('(').TrimEnd(')').Trim();
        var parts = inner.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1)
        {
            for (int j = 1; j < parts.Length; j++) target.Parameters.Add(parts[j]);
        }
    }

    private static string ExtractValue(string line) => line[(line.IndexOf(':') + 1)..].Trim();

    private sealed class ConstraintTestBuilder
    {
        public List<string> Inputs { get; } = new();
        public string ExpectStdout { get; set; } = string.Empty;
        public ConstraintTest Build() => new(Inputs, ExpectStdout);
    }

    private sealed class FunctionSpecBuilder
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Parameters { get; } = new();
        public string Intent { get; set; } = string.Empty;
        public List<FunctionTestBuilder> PendingTests { get; } = new();

        public FunctionSpec Build()
        {
            var tests = PendingTests.Select(t => t.Build()).ToList();
            return new FunctionSpec(Name, Parameters, Intent, tests);
        }
    }

    private sealed class FunctionTestBuilder
    {
        public List<string> Inputs { get; } = new();
        public string Expect { get; set; } = string.Empty;
        public FunctionTest Build() => new(Inputs, Expect);
    }
}
