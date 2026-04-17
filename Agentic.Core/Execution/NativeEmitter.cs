using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Agentic.Core.Execution;

public sealed class NativeEmitter
{
    private readonly string _workspacePath;

    public NativeEmitter()
    {
        _workspacePath = Path.Combine(Path.GetTempPath(), "AgenticCompiler", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspacePath);
    }

    /// <param name="transpiledCode">The generated C# source.</param>
    /// <param name="outputName">Name for the output binary.</param>
    /// <param name="isServer">When true, emits a web project (ASP.NET Minimal API).</param>
    public string Emit(string transpiledCode, string outputName, bool isServer = false)
    {
        string csFilePath = Path.Combine(_workspacePath, "Program.cs");
        string csprojPath = Path.Combine(_workspacePath, $"{outputName}.csproj");

        File.WriteAllText(csFilePath, transpiledCode);
        bool usesSqlite = transpiledCode.Contains("Microsoft.Data.Sqlite") || transpiledCode.Contains("_DbConnect");
        File.WriteAllText(csprojPath, isServer ? GenerateWebCsproj(usesSqlite) : GenerateCsproj(usesSqlite));

        string rid = RuntimeInformation.RuntimeIdentifier;
        Console.WriteLine($"\n[EMITTER] Workspace locked: {_workspacePath}");

        string publishArgs;
        if (isServer)
        {
            Console.WriteLine($"[EMITTER] Building web server for {rid}...");
            publishArgs = $"publish -c Release -r {rid} --self-contained true";
        }
        else if (usesSqlite)
        {
            Console.WriteLine($"[EMITTER] Building self-contained binary for {rid} (SQLite; skipping AOT)...");
            publishArgs = $"publish -c Release -r {rid} --self-contained true";
        }
        else
        {
            Console.WriteLine($"[EMITTER] Engaging AOT Compilation for {rid}...");
            publishArgs = $"publish -c Release -r {rid} --self-contained true";
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = publishArgs,
                WorkingDirectory = _workspacePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.OutputDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
                Console.WriteLine($"  [AOT] {args.Data}");
        };

        process.ErrorDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
                Console.WriteLine($"  [AOT ERROR] {args.Data}");
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new Exception("Native Compilation Failed. See AOT ERROR logs above.");
        }

        string extension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";
        string binaryPath = Path.Combine(_workspacePath, "bin", "Release", "net8.0", rid, "publish", outputName + extension);

        return binaryPath;
    }

    private string GenerateCsproj(bool usesSqlite = false)
    {
        string sqliteRef = usesSqlite
            ? "\n  <ItemGroup>\n    <PackageReference Include=\"Microsoft.Data.Sqlite\" Version=\"8.0.0\" />\n  </ItemGroup>"
            : "";
        // NativeAOT is incompatible with Microsoft.Data.Sqlite — fall back to self-contained JIT
        string aotProps = usesSqlite
            ? "<SelfContained>true</SelfContained>"
            : "<PublishAot>true</PublishAot>\n    <OptimizationPreference>Speed</OptimizationPreference>\n    <StripSymbols>true</StripSymbols>";
        return $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    {aotProps}
  </PropertyGroup>{sqliteRef}
</Project>";
    }

    private string GenerateWebCsproj(bool usesSqlite = false)
    {
        string sqliteRef = usesSqlite
            ? "\n  <ItemGroup>\n    <PackageReference Include=\"Microsoft.Data.Sqlite\" Version=\"8.0.0\" />\n  </ItemGroup>"
            : "";
        return $@"<Project Sdk=""Microsoft.NET.Sdk.Web"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>{sqliteRef}
</Project>";
    }
}