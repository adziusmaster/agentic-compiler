namespace Agentic.Core.Execution;

/// <summary>
/// Thrown when a <c>(require …)</c> or <c>(ensure …)</c> contract evaluates to false.
/// </summary>
public sealed class ContractViolationException : Exception
{
    public string ContractKind { get; }
    public string Expression { get; }

    public ContractViolationException(string kind, string expression)
        : base($"Contract violation ({kind}): {expression}")
    {
        ContractKind = kind;
        Expression = expression;
    }
}

/// <summary>
/// Thrown when an assertion inside a <c>(test …)</c> block fails during compilation.
/// </summary>
public sealed class TestFailureException : Exception
{
    public string TestName { get; }
    public string Detail { get; }

    public TestFailureException(string testName, string detail)
        : base($"Test '{testName}' failed: {detail}")
    {
        TestName = testName;
        Detail = detail;
    }

    public TestFailureException(string testName, string detail, Exception inner)
        : base($"Test '{testName}' failed: {detail}", inner)
    {
        TestName = testName;
        Detail = detail;
    }
}
