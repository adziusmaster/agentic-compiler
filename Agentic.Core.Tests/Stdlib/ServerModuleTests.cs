using Agentic.Core.Execution;
using Agentic.Core.Stdlib;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Stdlib;

public sealed class ServerModuleTests
{
    [Fact]
    public void ServerGet_ShouldTranspileToMinimalApi()
    {
        // Arrange
        const string source = @"
            (module Api
              (defun hello ((name : Str)) : Str
                (return (str.concat ""Hello, "" name)))
              (test hello (assert-eq (hello ""World"") ""Hello, World""))
              (server.get ""/hello/:name"" hello)
              (server.listen 8080))";

        var permissions = new Permissions { AllowHttp = true };
        var compiler = new Compiler(emitBinary: false, permissions: permissions);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeTrue();
        result.GeneratedSource.Should().Contain("WebApplication.CreateBuilder");
        result.GeneratedSource.Should().Contain("app.MapGet(\"/hello/{name}\"");
        result.GeneratedSource.Should().Contain("app.Run(\"http://0.0.0.0:8080\")");
    }

    [Fact]
    public void ServerPost_ShouldTranspileWithBodyParam()
    {
        // Arrange
        const string source = @"
            (module Api
              (defun create_item ((body : Str)) : Str
                (return (str.concat ""Created: "" body)))
              (test create (assert-eq (create_item ""test"") ""Created: test""))
              (server.post ""/items"" create_item)
              (server.listen 3000))";

        var permissions = new Permissions { AllowHttp = true };
        var compiler = new Compiler(emitBinary: false, permissions: permissions);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeTrue();
        result.GeneratedSource.Should().Contain("app.MapPost(\"/items\"");
        result.GeneratedSource.Should().Contain("app.Run(\"http://0.0.0.0:3000\")");
    }

    [Fact]
    public void ServerMode_WithoutPermission_ShouldFail()
    {
        // Arrange
        const string source = @"
            (module Api
              (defun hello () : Str (return ""hi""))
              (server.get ""/hello"" hello)
              (server.listen 8080))";

        var compiler = new Compiler(emitBinary: false, permissions: Permissions.None);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Type == "permission-denied");
    }

    [Fact]
    public void ServerMode_ShouldNotEmitAutoEntryPoint()
    {
        // Arrange — server programs should not get auto-generated CLI entry point
        const string source = @"
            (module Api
              (defun hello ((name : Str)) : Str
                (return (str.concat ""Hello, "" name)))
              (server.get ""/hello/:name"" hello)
              (server.listen 8080))";

        var permissions = new Permissions { AllowHttp = true };
        var compiler = new Compiler(emitBinary: false, permissions: permissions);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeTrue();
        result.GeneratedSource.Should().NotContain("class Program");
        result.GeneratedSource.Should().NotContain("static void Main");
    }

    [Fact]
    public void ServerMode_MultipleRoutes_ShouldRegisterAll()
    {
        // Arrange
        const string source = @"
            (module Api
              (defun get_user ((id : Str)) : Str
                (return (str.concat ""User: "" id)))
              (defun add_nums ((a : Num) (b : Num)) : Num
                (return (+ a b)))
              (test user (assert-eq (get_user ""42"") ""User: 42""))
              (test add (assert-eq (add_nums 3 4) 7))
              (server.get ""/user/:id"" get_user)
              (server.get ""/add/:a/:b"" add_nums)
              (server.listen 5000))";

        var permissions = new Permissions { AllowHttp = true };
        var compiler = new Compiler(emitBinary: false, permissions: permissions);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeTrue();
        result.GeneratedSource.Should().Contain("app.MapGet(\"/user/{id}\"");
        result.GeneratedSource.Should().Contain("app.MapGet(\"/add/{a}/{b}\"");
        result.TestsPassed.Should().Be(2);
    }

    [Fact]
    public void ServerMode_FunctionWithTests_ShouldVerifyThenTranspile()
    {
        // Arrange — tests must pass before the server code is generated
        const string source = @"
            (module Api
              (defun greet ((name : Str)) : Str
                (return (str.concat ""Hi "" name)))
              (test greet-bad (assert-eq (greet ""X"") ""WRONG""))
              (server.get ""/greet/:name"" greet)
              (server.listen 8080))";

        var permissions = new Permissions { AllowHttp = true };
        var compiler = new Compiler(emitBinary: false, permissions: permissions);

        // Act
        var result = compiler.Compile(source);

        // Assert — tests fail, so compilation fails (no server code generated)
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Type == "test-failure");
    }

    [Fact]
    public void JsonGet_ShouldEmitResultsContent()
    {
        // Arrange
        const string source = @"
            (module Api
              (defun user_json ((id : Str)) : Str
                (return (json.object ""id"" id ""status"" ""active"")))
              (test user (assert-eq (json.get (user_json ""42"") ""id"") ""42""))
              (server.json_get ""/user/:id"" user_json)
              (server.listen 8080))";

        var permissions = new Permissions { AllowHttp = true };
        var compiler = new Compiler(emitBinary: false, permissions: permissions);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeTrue();
        result.GeneratedSource.Should().Contain("Results.Content(");
        result.GeneratedSource.Should().Contain("application/json");
    }

    [Fact]
    public void JsonPost_ShouldParseBodyAndReturnJson()
    {
        // Arrange
        const string source = @"
            (module Api
              (defun create_item ((body : Str)) : Str
                (return (json.object ""received"" body ""status"" ""created"")))
              (test create (do
                (def result : Str (create_item ""test data""))
                (assert-eq (json.get result ""status"") ""created"")))
              (server.json_post ""/items"" create_item)
              (server.listen 3000))";

        var permissions = new Permissions { AllowHttp = true };
        var compiler = new Compiler(emitBinary: false, permissions: permissions);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeTrue();
        result.GeneratedSource.Should().Contain("Results.Content(");
        result.GeneratedSource.Should().Contain("app.MapPost(\"/items\"");
    }
}
