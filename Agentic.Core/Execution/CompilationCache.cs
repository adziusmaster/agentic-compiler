using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agentic.Core.Execution;

/// <summary>
/// Content-addressed compilation cache. Stores <see cref="CompileResult"/>
/// keyed by SHA-256 hash of source text. Supports in-memory and file-based persistence.
/// </summary>
public sealed class CompilationCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();
    private readonly string? _persistPath;

    /// <param name="persistPath">
    /// Directory for on-disk cache. When null, cache is in-memory only.
    /// </param>
    public CompilationCache(string? persistPath = null)
    {
        _persistPath = persistPath;
        if (_persistPath is not null)
        {
            Directory.CreateDirectory(_persistPath);
            LoadFromDisk();
        }
    }

    /// <summary>Number of entries currently in the cache.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Computes the SHA-256 content hash of source text.
    /// </summary>
    public static string ComputeHash(string source)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Attempts to retrieve a cached compile result for the given content hash.
    /// </summary>
    public bool TryGet(string hash, out CompileResult? result)
    {
        if (_entries.TryGetValue(hash, out var entry))
        {
            entry = entry with { HitCount = entry.HitCount + 1 };
            _entries[hash] = entry;
            result = entry.Result;
            return true;
        }
        result = null;
        return false;
    }

    /// <summary>
    /// Stores a compile result in the cache keyed by content hash.
    /// Only successful results are cached (failed builds may be retried with fixes).
    /// </summary>
    public void Store(string hash, CompileResult result)
    {
        if (!result.Success) return;

        var entry = new CacheEntry
        {
            Hash = hash,
            Result = StripBinaryPath(result),
            Timestamp = DateTimeOffset.UtcNow,
            HitCount = 0
        };
        _entries[hash] = entry;
    }

    /// <summary>
    /// Persists the in-memory cache to disk as JSON files.
    /// </summary>
    public void Flush()
    {
        if (_persistPath is null) return;

        foreach (var (hash, entry) in _entries)
        {
            var filePath = Path.Combine(_persistPath, $"{hash}.json");
            var json = JsonSerializer.Serialize(entry, SerializerOptions);
            File.WriteAllText(filePath, json);
        }
    }

    /// <summary>
    /// Removes all entries from the cache (memory and disk).
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
        if (_persistPath is not null && Directory.Exists(_persistPath))
        {
            foreach (var file in Directory.GetFiles(_persistPath, "*.json"))
                File.Delete(file);
        }
    }

    private void LoadFromDisk()
    {
        if (_persistPath is null || !Directory.Exists(_persistPath)) return;

        foreach (var file in Directory.GetFiles(_persistPath, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var entry = JsonSerializer.Deserialize<CacheEntry>(json, SerializerOptions);
                if (entry?.Hash is not null)
                    _entries.TryAdd(entry.Hash, entry);
            }
            catch
            {
                // Corrupted cache file — skip it
            }
        }
    }

    /// <summary>
    /// Strips the binary path from a cached result since it may not exist
    /// when restored from a different session.
    /// </summary>
    private static CompileResult StripBinaryPath(CompileResult result) =>
        result with { BinaryPath = null };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>
/// A single entry in the compilation cache.
/// </summary>
internal sealed record CacheEntry
{
    public required string Hash { get; init; }
    public required CompileResult Result { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public int HitCount { get; set; }
}
