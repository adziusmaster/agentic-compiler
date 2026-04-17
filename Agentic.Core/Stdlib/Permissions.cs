namespace Agentic.Core.Stdlib;

/// <summary>
/// Declares which I/O capabilities a compiled program is allowed to use.
/// Permissions are opt-in — nothing is allowed by default.
/// </summary>
public sealed record Permissions
{
    /// <summary>Allow reading files from the filesystem.</summary>
    public bool AllowFileRead { get; init; }

    /// <summary>Allow writing/deleting files on the filesystem.</summary>
    public bool AllowFileWrite { get; init; }

    /// <summary>Allow making outbound HTTP requests.</summary>
    public bool AllowHttp { get; init; }

    /// <summary>Allow reading environment variables.</summary>
    public bool AllowEnv { get; init; }

    /// <summary>Allow SQLite database operations.</summary>
    public bool AllowDb { get; init; }

    /// <summary>No permissions — pure computation only (default).</summary>
    public static Permissions None => new();

    /// <summary>All I/O permissions granted.</summary>
    public static Permissions All => new()
    {
        AllowFileRead = true,
        AllowFileWrite = true,
        AllowHttp = true,
        AllowEnv = true,
        AllowDb = true,
    };

    /// <summary>
    /// Asserts that a specific permission is granted. Throws if denied.
    /// </summary>
    public void Require(string capability)
    {
        bool granted = capability switch
        {
            "file.read"  => AllowFileRead,
            "file.write" => AllowFileWrite,
            "http"       => AllowHttp,
            "env"        => AllowEnv,
            "db"         => AllowDb,
            _            => throw new ArgumentException($"Unknown capability: {capability}")
        };

        if (!granted)
            throw new PermissionDeniedException(capability);
    }
}

/// <summary>
/// Thrown when a program attempts to use an I/O capability that was not granted.
/// </summary>
public sealed class PermissionDeniedException : Exception
{
    public string Capability { get; }

    public PermissionDeniedException(string capability)
        : base($"Permission denied: '{capability}' not granted. Use --allow-{capability.Replace('.', '-')} to enable.")
    {
        Capability = capability;
    }
}
