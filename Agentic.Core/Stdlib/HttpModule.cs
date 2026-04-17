namespace Agentic.Core.Stdlib;

/// <summary>
/// HTTP client operations. Verifier always throws (HTTP not available during verification).
/// Transpiler emits synchronous <c>HttpClient</c> calls.
/// </summary>
public sealed class HttpModule : IStdlibModule
{
    public void Register(StdlibRegistry registry)
    {
        // Verifier side — HTTP not available during verification
        registry.VerifierFuncs["http.get"] = _ =>
            throw new InvalidOperationException(
                "http.get: HTTP operations are not available during verification. " +
                "Test your logic with mock data instead.");

        registry.VerifierFuncs["http.post"] = _ =>
            throw new InvalidOperationException(
                "http.post: HTTP operations are not available during verification. " +
                "Test your logic with mock data instead.");

        // Transpiler side — emits HttpClient calls
        registry.TranspilerEmitters["http.get"] = (args, r) =>
            $"new System.Net.Http.HttpClient().GetStringAsync({r(args[0])}).Result";

        registry.TranspilerEmitters["http.post"] = (args, r) =>
            $"new System.Net.Http.HttpClient().PostAsync({r(args[0])}, " +
            $"new System.Net.Http.StringContent({r(args[1])})).Result.Content.ReadAsStringAsync().Result";

        // Permission requirements
        registry.PermissionRequirements["http.get"] = "http";
        registry.PermissionRequirements["http.post"] = "http";
    }
}
