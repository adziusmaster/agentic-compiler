namespace Agentic.Core.Stdlib;

/// <summary>
/// HTTP server primitives. Enables Agentic programs to define API endpoints
/// that transpile to ASP.NET Minimal API web servers.
/// Verifier treats route declarations as no-ops; transpiler emits Kestrel code.
/// </summary>
public sealed class ServerModule : IStdlibModule
{
    public void Register(StdlibRegistry registry)
    {
        // Verifier: route declarations and server start are no-ops during verification
        registry.VerifierFuncs["server.get"] = _ => 0.0;
        registry.VerifierFuncs["server.post"] = _ => 0.0;
        registry.VerifierFuncs["server.listen"] = _ => 0.0;

        // Transpiler emitters are NOT registered here — the Transpiler handles
        // server.* directives directly to collect route metadata and change
        // the program template from CLI to web server.

        // Permissions
        registry.PermissionRequirements["server.get"] = "http";
        registry.PermissionRequirements["server.post"] = "http";
        registry.PermissionRequirements["server.listen"] = "http";
    }
}
