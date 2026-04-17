namespace Agentic.Core.Stdlib;

/// <summary>
/// Environment variable access. Requires <c>--allow-env</c> permission.
/// During verification, returns safe test defaults (empty string / provided default).
/// </summary>
public sealed class EnvModule : IStdlibModule
{
    public void Register(StdlibRegistry registry)
    {
        // env.get returns empty string during verification (no real env access in tests)
        registry.VerifierFuncs["env.get"] = a => "";

        // env.get_or returns the default value during verification
        registry.VerifierFuncs["env.get_or"] = a => Convert.ToString(a[1]) ?? "";

        registry.TranspilerEmitters["env.get"] = (a, r) =>
            $"(Environment.GetEnvironmentVariable({r(a[0])}) ?? throw new Exception(\"Missing environment variable: \" + {r(a[0])}))";

        registry.TranspilerEmitters["env.get_or"] = (a, r) =>
            $"(Environment.GetEnvironmentVariable({r(a[0])}) ?? {r(a[1])})";

        registry.PermissionRequirements["env.get"] = "env";
        registry.PermissionRequirements["env.get_or"] = "env";
    }
}
