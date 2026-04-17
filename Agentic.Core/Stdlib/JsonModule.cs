using System.Text.Json;

namespace Agentic.Core.Stdlib;

/// <summary>
/// JSON parsing and construction operations. Both verifier and transpiler
/// use <c>System.Text.Json</c> — this is a pure in-memory module (no I/O).
/// </summary>
public sealed class JsonModule : IStdlibModule
{
    public void Register(StdlibRegistry registry)
    {
        // Verifier side — operates on actual JSON strings in-memory
        registry.VerifierFuncs["json.get"] = args =>
        {
            var json = args[0]?.ToString() ?? throw new ArgumentException("json.get: JSON string is null");
            var key = args[1]?.ToString() ?? throw new ArgumentException("json.get: key is null");
            return GetJsonValue(json, key);
        };

        registry.VerifierFuncs["json.get_num"] = args =>
        {
            var json = args[0]?.ToString() ?? throw new ArgumentException("json.get_num: JSON string is null");
            var key = args[1]?.ToString() ?? throw new ArgumentException("json.get_num: key is null");
            var value = GetJsonValue(json, key);
            return Convert.ToDouble(value);
        };

        registry.VerifierFuncs["json.object"] = args =>
        {
            if (args.Length % 2 != 0)
                throw new ArgumentException("json.object: requires even number of args (key-value pairs)");

            var pairs = new List<string>();
            for (int i = 0; i < args.Length; i += 2)
            {
                var key = args[i]?.ToString() ?? "";
                var value = args[i + 1];
                if (value is double d)
                    pairs.Add($"\"{key}\":{d}");
                else
                    pairs.Add($"\"{key}\":\"{value}\"");
            }
            return "{" + string.Join(",", pairs) + "}";
        };

        registry.VerifierFuncs["json.array_length"] = args =>
        {
            var json = args[0]?.ToString() ?? throw new ArgumentException("json.array_length: JSON string is null");
            using var doc = JsonDocument.Parse(json);
            return (double)doc.RootElement.GetArrayLength();
        };

        // Transpiler side — emits System.Text.Json calls
        registry.TranspilerEmitters["json.get"] = (args, r) =>
            $"System.Text.Json.JsonDocument.Parse({r(args[0])}).RootElement" +
            $".GetProperty({r(args[1])}).GetString()";

        registry.TranspilerEmitters["json.get_num"] = (args, r) =>
            $"System.Text.Json.JsonDocument.Parse({r(args[0])}).RootElement" +
            $".GetProperty({r(args[1])}).GetDouble()";

        registry.TranspilerEmitters["json.object"] = (args, r) =>
        {
            var pairs = new List<string>();
            for (int i = 0; i < args.Count; i += 2)
                pairs.Add($"\"\\\"\" + {r(args[i])} + \"\\\":\" + {r(args[i + 1])}");
            return $"\"{{\" + {string.Join(" + \",\" + ", pairs)} + \"}}\"";
        };

        registry.TranspilerEmitters["json.array_length"] = (args, r) =>
            $"(double)System.Text.Json.JsonDocument.Parse({r(args[0])}).RootElement.GetArrayLength()";
    }

    private static string GetJsonValue(string json, string key)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty(key, out var prop))
        {
            return prop.ValueKind switch
            {
                JsonValueKind.String => prop.GetString() ?? "",
                JsonValueKind.Number => prop.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "",
                _ => prop.GetRawText()
            };
        }
        throw new KeyNotFoundException($"json.get: key '{key}' not found in JSON");
    }
}
