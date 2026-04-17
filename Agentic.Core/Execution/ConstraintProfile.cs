namespace Agentic.Core.Execution;

public sealed record ConstraintTest(
    IReadOnlyList<string> Inputs,
    string ExpectStdout);

/// <summary>
/// A single micro-test for a pre-declared helper function. Inputs are stringified
/// S-expression literals (numbers today) that get spliced into a call site.
/// </summary>
public sealed record FunctionTest(
    IReadOnlyList<string> Inputs,
    string Expect);

/// <summary>
/// Author-declared helper function for the Planner→Implementer pipeline.
/// The Implementer verifies its generated body against <see cref="Tests"/> in isolation
/// before the Composer ever sees it.
/// </summary>
public sealed record FunctionSpec(
    string Name,
    IReadOnlyList<string> Parameters,
    string Intent,
    IReadOnlyList<FunctionTest> Tests);

public sealed record ConstraintProfile(
    string Version,
    string Name,
    string Objective,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<ConstraintTest> Tests,
    IReadOnlyList<FunctionSpec>? Functions = null,
    bool Pipeline = false)
{
    public IReadOnlyList<FunctionSpec> FunctionsOrEmpty =>
        Functions ?? System.Array.Empty<FunctionSpec>();
}
