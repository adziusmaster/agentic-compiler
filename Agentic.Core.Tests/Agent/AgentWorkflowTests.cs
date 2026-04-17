using Agentic.Core.Agent;
using Agentic.Core.Execution;
using Agentic.Core.Stdlib;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Agentic.Core.Tests.Agent;

public class AgentWorkflowTests
{
    #region Prompt Construction

    [Fact]
    public void BuildSystemPrompt_ContainsLanguageSpec()
    {
        var prompt = AgentWorkflow.BuildSystemPrompt();

        prompt.Should().Contain("Agentic Language Specification");
        prompt.Should().Contain("S-expressions");
        prompt.Should().Contain("Code Generation Rules");
    }

    [Fact]
    public void BuildSystemPrompt_ContainsGenerationRules()
    {
        var prompt = AgentWorkflow.BuildSystemPrompt();

        prompt.Should().Contain("explicit type annotations");
        prompt.Should().Contain("(module Name");
        prompt.Should().Contain("strictly binary");
    }

    [Fact]
    public void BuildInitialPrompt_ContainsIntentAndModuleName()
    {
        var prompt = AgentWorkflow.BuildInitialPrompt("Add two numbers", "Calculator");

        prompt.Should().Contain("Add two numbers");
        prompt.Should().Contain("Calculator");
        prompt.Should().Contain("(test");
        prompt.Should().Contain("(assert-eq");
    }

    [Fact]
    public void BuildRetryPrompt_ContainsPreviousErrorsAndSource()
    {
        var failedResult = new CompileResult
        {
            Success = false,
            Diagnostics = new[]
            {
                new CompileDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Type = "test-failure",
                    Message = "Expected 3, got 4",
                    FixHint = "Fix the add function"
                }
            }
        };

        var prompt = AgentWorkflow.BuildRetryPrompt(
            "Add two numbers",
            "(module Calc (defun add (a b) (return (+ a 1))))",
            failedResult);

        prompt.Should().Contain("Expected 3, got 4");
        prompt.Should().Contain("(module Calc");
        prompt.Should().Contain("Add two numbers");
    }

    #endregion

    #region Output Cleaning

    [Fact]
    public void CleanAgentOutput_RemovesMarkdownFences()
    {
        var raw = "```lisp\n(module Foo (def x 1))\n```";
        AgentWorkflow.CleanAgentOutput(raw).Should().Be("(module Foo (def x 1))");
    }

    [Fact]
    public void CleanAgentOutput_TrimsWhitespace()
    {
        var raw = "\n  (module Foo (def x 1))  \n";
        AgentWorkflow.CleanAgentOutput(raw).Should().Be("(module Foo (def x 1))");
    }

    [Fact]
    public void CleanAgentOutput_PassesThroughCleanSource()
    {
        var raw = "(module Foo (def x 1))";
        AgentWorkflow.CleanAgentOutput(raw).Should().Be("(module Foo (def x 1))");
    }

    [Fact]
    public void CleanAgentOutput_HandlesUnlabelledFences()
    {
        var raw = "```\n(do (def x 1))\n```";
        AgentWorkflow.CleanAgentOutput(raw).Should().Be("(do (def x 1))");
    }

    #endregion

    #region Workflow Integration

    [Fact]
    public async Task RunAsync_SuccessOnFirstAttempt()
    {
        var agent = Substitute.For<IAgentClient>();
        agent.GenerateCodeAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns("(do (defun add ((a : Num) (b : Num)) : Num (return (+ a b))) (test add (assert-eq (add 1 2) 3)))");

        var workflow = new AgentWorkflow(agent, maxAttempts: 3, emitBinary: false);
        var result = await workflow.RunAsync("Add two numbers", "Calculator");

        result.Success.Should().BeTrue();
        result.AttemptsUsed.Should().Be(1);
        result.Source.Should().Contain("defun add");
        result.CompileResult.Should().NotBeNull();
        result.CompileResult!.TestsPassed.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_SelfCorrectsOnRetry()
    {
        var agent = Substitute.For<IAgentClient>();
        int callCount = 0;

        agent.GenerateCodeAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount == 1)
                    // First attempt: bad code — assert-eq fails
                    return "(do (defun add ((a : Num) (b : Num)) : Num (return (+ a 1))) (test add (assert-eq (add 1 2) 3)))";
                else
                    // Second attempt: fixed
                    return "(do (defun add ((a : Num) (b : Num)) : Num (return (+ a b))) (test add (assert-eq (add 1 2) 3)))";
            });

        var workflow = new AgentWorkflow(agent, maxAttempts: 3, emitBinary: false);
        var result = await workflow.RunAsync("Add two numbers", "Calculator");

        result.Success.Should().BeTrue();
        result.AttemptsUsed.Should().Be(2);

        // Verify the retry prompt was called with error context
        await agent.Received(2).GenerateCodeAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ExhaustsAttempts()
    {
        var agent = Substitute.For<IAgentClient>();
        agent.GenerateCodeAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            // Always returns code with a failing test
            .Returns("(do (defun add ((a : Num) (b : Num)) : Num (return 0)) (test add (assert-eq (add 1 2) 3)))");

        var workflow = new AgentWorkflow(agent, maxAttempts: 2, emitBinary: false);
        var result = await workflow.RunAsync("Add two numbers", "Calculator");

        result.Success.Should().BeFalse();
        result.AttemptsUsed.Should().Be(2);
        result.CompileResult.Should().NotBeNull();
        result.CompileResult!.Diagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RunAsync_SystemPromptContainsLanguageSpec()
    {
        var agent = Substitute.For<IAgentClient>();
        string? capturedSystemPrompt = null;

        agent.GenerateCodeAsync(
            Arg.Do<string>(s => capturedSystemPrompt = s),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns("(do (def x 1))");

        var workflow = new AgentWorkflow(agent, maxAttempts: 1, emitBinary: false);
        await workflow.RunAsync("Anything");

        capturedSystemPrompt.Should().NotBeNull();
        capturedSystemPrompt.Should().Contain("Agentic Language Specification");
        capturedSystemPrompt.Should().Contain("Code Generation Rules");
    }

    [Fact]
    public async Task RunAsync_RetryPromptContainsStructuredErrors()
    {
        var agent = Substitute.For<IAgentClient>();
        string? capturedRetryPrompt = null;
        int callCount = 0;

        agent.GenerateCodeAsync(
            Arg.Any<string>(),
            Arg.Do<string>(s =>
            {
                callCount++;
                if (callCount == 2) capturedRetryPrompt = s;
            }),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (callCount <= 1)
                    return "(do (defun add ((a : Num) (b : Num)) : Num (return 0)) (test add (assert-eq (add 1 2) 3)))";
                return "(do (def x 1))";
            });

        var workflow = new AgentWorkflow(agent, maxAttempts: 2, emitBinary: false);
        await workflow.RunAsync("Add two numbers");

        capturedRetryPrompt.Should().NotBeNull();
        capturedRetryPrompt.Should().Contain("(error");
        capturedRetryPrompt.Should().Contain("test-failure");
    }

    [Fact]
    public async Task RunAsync_PassesLog()
    {
        var agent = Substitute.For<IAgentClient>();
        agent.GenerateCodeAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns("(do (def x 1))");

        var logMessages = new List<string>();
        var workflow = new AgentWorkflow(agent, maxAttempts: 1, emitBinary: false, log: logMessages.Add);
        await workflow.RunAsync("Test");

        logMessages.Should().Contain(m => m.Contains("[AGENT]"));
        logMessages.Should().Contain(m => m.Contains("[COMPILER]"));
        logMessages.Should().Contain(m => m.Contains("[SUCCESS]"));
    }

    [Fact]
    public async Task RunAsync_HandlesParseErrors()
    {
        var agent = Substitute.For<IAgentClient>();
        agent.GenerateCodeAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns("(defun broken ((( unclosed");

        var workflow = new AgentWorkflow(agent, maxAttempts: 1, emitBinary: false);
        var result = await workflow.RunAsync("Test");

        result.Success.Should().BeFalse();
        result.CompileResult.Should().NotBeNull();
    }

    [Fact]
    public async Task RunAsync_CleansMarkdownFromOutput()
    {
        var agent = Substitute.For<IAgentClient>();
        agent.GenerateCodeAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns("```lisp\n(do (defun add ((a : Num) (b : Num)) : Num (return (+ a b))) (test add (assert-eq (add 1 2) 3)))\n```");

        var workflow = new AgentWorkflow(agent, maxAttempts: 1, emitBinary: false);
        var result = await workflow.RunAsync("Add two numbers");

        result.Success.Should().BeTrue();
        result.Source.Should().StartWith("(do");
    }

    #endregion

    #region Permission Inference

    [Theory]
    [InlineData("Build an API endpoint that returns user data")]
    [InlineData("Create a REST server with product routes")]
    [InlineData("Build an http service that listens on port 8080")]
    [InlineData("Create a webhook handler")]
    [InlineData("Fetch data from a URL")]
    public void InferPermissions_DetectsHttpFromIntent(string intent)
    {
        var perms = AgentWorkflow.InferPermissions(intent);
        perms.AllowHttp.Should().BeTrue();
    }

    [Theory]
    [InlineData("Build a calculator")]
    [InlineData("Sort an array of numbers")]
    [InlineData("Compute standard deviation")]
    public void InferPermissions_NoHttpForPureComputation(string intent)
    {
        var perms = AgentWorkflow.InferPermissions(intent);
        perms.AllowHttp.Should().BeFalse();
    }

    [Theory]
    [InlineData("Read file contents and parse them")]
    [InlineData("Load file data from disk")]
    [InlineData("Parse a CSV file")]
    public void InferPermissions_DetectsFileReadFromIntent(string intent)
    {
        var perms = AgentWorkflow.InferPermissions(intent);
        perms.AllowFileRead.Should().BeTrue();
    }

    [Theory]
    [InlineData("Write results to a file")]
    [InlineData("Save file output as report")]
    public void InferPermissions_DetectsFileWriteFromIntent(string intent)
    {
        var perms = AgentWorkflow.InferPermissions(intent);
        perms.AllowFileWrite.Should().BeTrue();
        perms.AllowFileRead.Should().BeTrue();
    }

    [Theory]
    [InlineData("Read the API key from an environment variable")]
    [InlineData("Get config from env vars")]
    public void InferPermissions_DetectsEnvFromIntent(string intent)
    {
        var perms = AgentWorkflow.InferPermissions(intent);
        perms.AllowEnv.Should().BeTrue();
    }

    [Fact]
    public void MergePermissions_CombinesBothSets()
    {
        var a = new Permissions { AllowHttp = true };
        var b = new Permissions { AllowFileRead = true, AllowEnv = true };

        var merged = AgentWorkflow.MergePermissions(a, b);

        merged.AllowHttp.Should().BeTrue();
        merged.AllowFileRead.Should().BeTrue();
        merged.AllowEnv.Should().BeTrue();
        merged.AllowFileWrite.Should().BeFalse();
    }

    [Fact]
    public void BuildInitialPrompt_ServerAppOmitsSysInputGet()
    {
        var prompt = AgentWorkflow.BuildInitialPrompt("Build an API endpoint", "Api");
        prompt.Should().Contain("server.listen");
        prompt.Should().Contain("Do NOT use (sys.input.get");
    }

    [Fact]
    public void BuildInitialPrompt_RegularAppIncludesSysInputGet()
    {
        var prompt = AgentWorkflow.BuildInitialPrompt("Add two numbers", "Calc");
        prompt.Should().Contain("sys.input.get");
        prompt.Should().NotContain("server.listen");
    }

    #endregion
}
