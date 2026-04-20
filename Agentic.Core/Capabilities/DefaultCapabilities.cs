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

        // file.read : Str -> Str — reads UTF-8 text content of a file.
        r.Register(new Capability(
            Name: "file.read",
            ParamTypes: new AgType[] { AgType.Str },
            ReturnType: AgType.Str,
            Permission: "file.read",
            Adapter: args => System.IO.File.ReadAllText((string)args[0]),
            CSharpEmitExpr: "System.IO.File.ReadAllText({0})"));

        // file.write : (Str, Str) -> Num — writes content to path; returns 1.0 on success.
        r.Register(new Capability(
            Name: "file.write",
            ParamTypes: new AgType[] { AgType.Str, AgType.Str },
            ReturnType: AgType.Num,
            Permission: "file.write",
            Adapter: args =>
            {
                System.IO.File.WriteAllText((string)args[0], (string)args[1]);
                return 1.0;
            },
            CSharpEmitExpr: "((System.Func<double>)(() => {{ System.IO.File.WriteAllText({0}, {1}); return 1.0; }}))()"));

        // env.get : Str -> Str — reads environment variable (empty string if unset).
        r.Register(new Capability(
            Name: "env.get",
            ParamTypes: new AgType[] { AgType.Str },
            ReturnType: AgType.Str,
            Permission: "env",
            Adapter: args => System.Environment.GetEnvironmentVariable((string)args[0]) ?? "",
            CSharpEmitExpr: "(System.Environment.GetEnvironmentVariable({0}) ?? \"\")"));

        // db.query : (Str, Str) -> Str — opens SQLite connection, runs scalar SQL, returns
        // first column of first row as a string (empty if no rows). Mock-first; real adapter
        // only invoked under --allow-real-io.
        r.Register(new Capability(
            Name: "db.query",
            ParamTypes: new AgType[] { AgType.Str, AgType.Str },
            ReturnType: AgType.Str,
            Permission: "db",
            // Compiler process does not link Microsoft.Data.Sqlite — the real adapter
            // runs only inside emitted binaries (where the NuGet reference is added
            // automatically by NativeEmitter). Under --allow-real-io in the verifier,
            // db.query is out of reach. Tests drive this through (mocks …).
            Adapter: _ => throw new NotSupportedException(
                "db.query: real adapter only runs in emitted binary; provide a (mocks …) clause for tests."),
            CSharpEmitExpr:
                "((System.Func<string>)(() => {{ " +
                "using var _c = new Microsoft.Data.Sqlite.SqliteConnection({0}); " +
                "_c.Open(); " +
                "using var _cmd = _c.CreateCommand(); " +
                "_cmd.CommandText = {1}; " +
                "return _cmd.ExecuteScalar()?.ToString() ?? \"\"; " +
                "}}))()"));

        // process.spawn : Str -> Str — runs a shell command, captures stdout. Mock-first;
        // the verifier refuses to run it without a mock unless --allow-real-io is set.
        r.Register(new Capability(
            Name: "process.spawn",
            ParamTypes: new AgType[] { AgType.Str },
            ReturnType: AgType.Str,
            Permission: "process",
            Adapter: args =>
            {
                var cmd = (string)args[0];
                bool isWin = System.OperatingSystem.IsWindows();
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = isWin ? "cmd.exe" : "/bin/sh",
                    Arguments = isWin ? "/c " + cmd : "-c \"" + cmd.Replace("\"", "\\\"") + "\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                };
                using var p = System.Diagnostics.Process.Start(psi)!;
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return output;
            },
            CSharpEmitExpr:
                "((System.Func<string>)(() => {{ " +
                "var _psi = new System.Diagnostics.ProcessStartInfo " +
                "{{ FileName = System.OperatingSystem.IsWindows() ? \"cmd.exe\" : \"/bin/sh\", " +
                "Arguments = System.OperatingSystem.IsWindows() ? (\"/c \" + ({0})) : (\"-c \\\"\" + (({0}).Replace(\"\\\"\", \"\\\\\\\"\")) + \"\\\"\"), " +
                "RedirectStandardOutput = true, UseShellExecute = false }}; " +
                "using var _p = System.Diagnostics.Process.Start(_psi)!; " +
                "string _out = _p.StandardOutput.ReadToEnd(); _p.WaitForExit(); return _out; " +
                "}}))()"));

        return r;
    }
}
