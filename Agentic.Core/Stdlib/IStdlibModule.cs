namespace Agentic.Core.Stdlib;

/// <summary>
/// Contract for a stdlib module. Each module registers its verifier functions
/// and transpiler emitters in a single <see cref="Register"/> call.
/// </summary>
public interface IStdlibModule
{
    void Register(StdlibRegistry registry);
}
