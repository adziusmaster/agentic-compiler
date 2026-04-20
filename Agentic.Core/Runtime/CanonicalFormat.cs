using System.Globalization;
using System.Text;

namespace Agentic.Core.Runtime;

/// <summary>
/// Canonical, round-trippable text form for every runtime value the DSL can
/// produce. Used by <c>(sys.stdout.write …)</c> so that Implementer micro-test
/// probes can compare stdout byte-for-byte for non-numeric helpers (strings,
/// arrays, records, maps, nested combinations).
///
/// The same serializer ships inside AOT-compiled binaries (see
/// <see cref="EmittedSource"/>) so runtime output is identical to verifier
/// output.
/// </summary>
internal static class CanonicalFormat
{
    public static string Serialize(object? value)
    {
        var sb = new StringBuilder();
        Write(sb, value);
        return sb.ToString();
    }

    /// <summary>
    /// Output form for <c>(sys.stdout.write …)</c>. Primitives keep their
    /// existing unadorned form (numbers raw, strings unquoted, bools as
    /// <c>true</c>/<c>false</c>) so legacy samples don't regress. Composites
    /// (arrays, records, maps) render in canonical form — inside a composite,
    /// strings ARE quoted so structure is unambiguous on round-trip.
    /// </summary>
    public static string ForWrite(object? value) => value switch
    {
        null => "nil",
        bool b => b ? "true" : "false",
        double d => FormatDouble(d),
        string s => s,
        _ => Serialize(value)
    };

    private static void Write(StringBuilder sb, object? value)
    {
        switch (value)
        {
            case null: sb.Append("nil"); return;
            case bool b: sb.Append(b ? "true" : "false"); return;
            case double d: sb.Append(FormatDouble(d)); return;
            case string s: WriteString(sb, s); return;
            case Record r: WriteRecord(sb, r); return;
            case double[] da: WriteArray(sb, da.Length, i => Write(sb, da[i])); return;
            case string[] sa: WriteArray(sb, sa.Length, i => Write(sb, sa[i])); return;
            case object[] oa: WriteArray(sb, oa.Length, i => Write(sb, oa[i])); return;
            case System.Collections.IDictionary dict: WriteMap(sb, dict); return;
        }

        // Fallback: handle runtime records emitted by the transpiler (ITuple shape).
        // These are C# record structs like `Point(double x, double y)`.
        var type = value.GetType();
        if (type.IsValueType && type.Name.Length > 0 && char.IsUpper(type.Name[0]))
        {
            var fields = type.GetProperties();
            if (fields.Length > 0)
            {
                sb.Append(type.Name);
                sb.Append('{');
                for (int i = 0; i < fields.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(fields[i].Name);
                    sb.Append(": ");
                    Write(sb, fields[i].GetValue(value));
                }
                sb.Append('}');
                return;
            }
        }
        sb.Append(value);
    }

    private static string FormatDouble(double d)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsPositiveInfinity(d)) return "Inf";
        if (double.IsNegativeInfinity(d)) return "-Inf";
        if (d == 0.0) return "0";
        // Shortest round-trip form; normalize -0 already handled by d == 0.0 branch.
        string r = d.ToString("R", CultureInfo.InvariantCulture);
        // Compact integer-valued doubles: "42" over "42.0" — the DSL uses double everywhere.
        if (d == Math.Truncate(d) && !double.IsInfinity(d) && Math.Abs(d) < 1e16 && !r.Contains('E') && !r.Contains('e'))
        {
            int dot = r.IndexOf('.');
            if (dot >= 0) r = r[..dot];
        }
        return r;
    }

    private static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append($"\\u{(int)c:x4}");
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    private static void WriteArray(StringBuilder sb, int count, Action<int> writeAt)
    {
        sb.Append('[');
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(", ");
            writeAt(i);
        }
        sb.Append(']');
    }

    private static void WriteRecord(StringBuilder sb, Record r)
    {
        sb.Append(r.TypeName);
        sb.Append('{');
        int i = 0;
        foreach (var (field, value) in r.EnumerateFields())
        {
            if (i++ > 0) sb.Append(", ");
            sb.Append(field);
            sb.Append(": ");
            Write(sb, value);
        }
        sb.Append('}');
    }

    private static void WriteMap(StringBuilder sb, System.Collections.IDictionary dict)
    {
        // Canonical order: sort keys lexicographically so output is deterministic.
        var keys = new List<string>();
        foreach (var k in dict.Keys) keys.Add(k?.ToString() ?? string.Empty);
        keys.Sort(System.StringComparer.Ordinal);

        sb.Append('{');
        for (int i = 0; i < keys.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            WriteString(sb, keys[i]);
            sb.Append(": ");
            Write(sb, dict[keys[i]]);
        }
        sb.Append('}');
    }

    /// <summary>
    /// C# source of a self-contained canonical serializer, injected into emitted
    /// programs so AOT binaries print in the same format as the Verifier.
    /// Kept in sync with the Serialize method above.
    /// </summary>
    public const string EmittedSource = @"
static class AgCanonical {
    // User-facing stdout form: primitives raw; composites use canonical form.
    public static string Out(object? v) {
        if (v is null) return ""nil"";
        if (v is bool bb) return bb ? ""true"" : ""false"";
        if (v is double dd) return Fd(dd);
        if (v is string ss) return ss;
        return Write(v);
    }
    // Canonical form (strings quoted, etc.) — used inside composites and for round-trip.
    public static string Write(object? v) {
        var sb = new System.Text.StringBuilder();
        W(sb, v);
        return sb.ToString();
    }
    static void W(System.Text.StringBuilder sb, object? v) {
        if (v is null) { sb.Append(""nil""); return; }
        if (v is bool b) { sb.Append(b ? ""true"" : ""false""); return; }
        if (v is double d) { sb.Append(Fd(d)); return; }
        if (v is string s) { Ws(sb, s); return; }
        if (v is double[] da) { Wa(sb, da.Length, i => W(sb, da[i])); return; }
        if (v is string[] sa) { Wa(sb, sa.Length, i => W(sb, sa[i])); return; }
        if (v is object[] oa) { Wa(sb, oa.Length, i => W(sb, oa[i])); return; }
        if (v is System.Collections.IDictionary dict) { Wm(sb, dict); return; }
        var t = v.GetType();
        if (t.IsValueType && t.Name.Length > 0 && char.IsUpper(t.Name[0])) {
            var ps = t.GetProperties();
            if (ps.Length > 0) {
                sb.Append(t.Name); sb.Append('{');
                for (int i = 0; i < ps.Length; i++) {
                    if (i > 0) sb.Append("", "");
                    sb.Append(ps[i].Name); sb.Append("": "");
                    W(sb, ps[i].GetValue(v));
                }
                sb.Append('}'); return;
            }
        }
        sb.Append(v);
    }
    static string Fd(double d) {
        if (double.IsNaN(d)) return ""NaN"";
        if (double.IsPositiveInfinity(d)) return ""Inf"";
        if (double.IsNegativeInfinity(d)) return ""-Inf"";
        if (d == 0.0) return ""0"";
        var r = d.ToString(""R"", System.Globalization.CultureInfo.InvariantCulture);
        if (d == System.Math.Truncate(d) && !double.IsInfinity(d) && System.Math.Abs(d) < 1e16 && !r.Contains('E') && !r.Contains('e')) {
            int dot = r.IndexOf('.'); if (dot >= 0) r = r.Substring(0, dot);
        }
        return r;
    }
    static void Ws(System.Text.StringBuilder sb, string s) {
        sb.Append('""');
        foreach (char c in s) {
            if (c == '\\') sb.Append(""\\\\"");
            else if (c == '""') sb.Append(""\\\"""");
            else if (c == '\n') sb.Append(""\\n"");
            else if (c == '\r') sb.Append(""\\r"");
            else if (c == '\t') sb.Append(""\\t"");
            else if (c < 0x20) sb.Append($""\\u{(int)c:x4}"");
            else sb.Append(c);
        }
        sb.Append('""');
    }
    static void Wa(System.Text.StringBuilder sb, int n, System.Action<int> wi) {
        sb.Append('[');
        for (int i = 0; i < n; i++) { if (i > 0) sb.Append("", ""); wi(i); }
        sb.Append(']');
    }
    static void Wm(System.Text.StringBuilder sb, System.Collections.IDictionary dict) {
        var keys = new System.Collections.Generic.List<string>();
        foreach (var k in dict.Keys) keys.Add(k?.ToString() ?? """");
        keys.Sort(System.StringComparer.Ordinal);
        sb.Append('{');
        for (int i = 0; i < keys.Count; i++) {
            if (i > 0) sb.Append("", "");
            Ws(sb, keys[i]); sb.Append("": ""); W(sb, dict[keys[i]]);
        }
        sb.Append('}');
    }
}";
}
