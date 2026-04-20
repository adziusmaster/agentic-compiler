using Agentic.Core.Syntax;

namespace Agentic.Core.Capabilities;

/// <summary>
/// Default trusted capabilities exposed to Agentic programs.
/// All require explicit permission grants (see Stdlib/Permissions.cs).
/// </summary>
public static class DefaultCapabilities
{
    public static CapabilityRegistry BuildTrusted()
    {
        var r = new CapabilityRegistry();

        // http.fetch : Str -> Str — synchronous GET, returns response body.
        r.Register(new Capability(
            Name: "http.fetch",
            ParamTypes: new AgType[] { AgType.Str },
            ReturnType: AgType.Str,
            Permission: "http",
            Adapter: args =>
            {
                using var client = new System.Net.Http.HttpClient();
                return client.GetStringAsync((string)args[0]).GetAwaiter().GetResult();
            },
            CSharpEmitExpr: "_httpClient.GetStringAsync({0}).GetAwaiter().GetResult()"));

        // time.now_unix : () -> Num — seconds since epoch.
        r.Register(new Capability(
            Name: "time.now_unix",
            ParamTypes: Array.Empty<AgType>(),
            ReturnType: AgType.Num,
            Permission: "time",
            Adapter: _ => (double)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            CSharpEmitExpr: "((double)System.DateTimeOffset.UtcNow.ToUnixTimeSeconds())"));

        return r;
    }
}
