namespace Agentic.Core.Stdlib;

/// <summary>
/// SQLite database operations.
/// Verifier side: in-memory table store (Dictionary-based) — no actual database file needed.
/// Transpiler side: emits Microsoft.Data.Sqlite helper calls.
/// </summary>
public sealed class DbModule : IStdlibModule
{
    public void Register(StdlibRegistry registry)
    {
        var tables = new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.OrdinalIgnoreCase);


        registry.VerifierFuncs["db.connect"] = _ => null!;

        registry.VerifierFuncs["db.exec"] = args =>
        {
            var sql = args[0]?.ToString() ?? "";
            var upper = sql.TrimStart().ToUpperInvariant();
            if (upper.StartsWith("CREATE TABLE"))
            {
                var tokens = sql.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string tableName = tokens[^1].TrimStart('(').Trim('"', '\'', '`');
                for (int i = 0; i < tokens.Length; i++)
                {
                    if (tokens[i].Equals("EXISTS", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Length)
                    { tableName = tokens[i + 1].TrimStart('(').Trim('"', '\'', '`'); break; }
                    if (tokens[i].Equals("TABLE", StringComparison.OrdinalIgnoreCase) &&
                        !tokens.Any(t => t.Equals("IF", StringComparison.OrdinalIgnoreCase)))
                    { if (i + 1 < tokens.Length) tableName = tokens[i + 1].TrimStart('(').Trim('"', '\'', '`'); break; }
                }
                tables.TryAdd(tableName, new List<Dictionary<string, object>>());
            }
            return (object)0.0;
        };

        registry.VerifierFuncs["db.insert"] = args =>
        {
            string table = args[0]?.ToString() ?? "";
            if (!tables.ContainsKey(table)) tables[table] = new();
            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i + 1 < args.Length; i += 2)
                row[args[i]?.ToString() ?? ""] = args[i + 1];
            // Simulate UNIQUE constraint: check first key column for duplicates
            if (args.Length >= 3)
            {
                string keyCol = args[1]?.ToString() ?? "";
                string keyVal = args[2]?.ToString() ?? "";
                if (tables[table].Any(r => r.TryGetValue(keyCol, out var v) && v?.ToString() == keyVal))
                    return (object)0.0;  // duplicate — constraint violation
            }
            tables[table].Add(row);
            return (object)1.0;  // success
        };

        registry.VerifierFuncs["db.find-all"] = args =>
        {
            string table = args[0]?.ToString() ?? "";
            return (object)(tables.TryGetValue(table, out var rows) ? rows : new List<Dictionary<string, object>>());
        };

        registry.VerifierFuncs["db.find-by"] = args =>
        {
            string table = args[0]?.ToString() ?? "";
            string col   = args[1]?.ToString() ?? "";
            string val   = args[2]?.ToString() ?? "";
            if (!tables.TryGetValue(table, out var rows)) return (object)new List<Dictionary<string, object>>();
            return (object)rows.Where(r => r.TryGetValue(col, out var v) && v?.ToString() == val).ToList();
        };

        registry.VerifierFuncs["db.delete"] = args =>
        {
            string table = args[0]?.ToString() ?? "";
            string col   = args[1]?.ToString() ?? "";
            string val   = args[2]?.ToString() ?? "";
            if (tables.TryGetValue(table, out var rows))
                rows.RemoveAll(r => r.TryGetValue(col, out var v) && v?.ToString() == val);
            return null!;
        };

        registry.VerifierFuncs["db.update"] = args =>
        {
            string table  = args[0]?.ToString() ?? "";
            string idCol  = args[1]?.ToString() ?? "";
            string idVal  = args[2]?.ToString() ?? "";
            string setCol = args[3]?.ToString() ?? "";
            object setVal = args[4];
            if (tables.TryGetValue(table, out var rows))
                foreach (var row in rows.Where(r => r.TryGetValue(idCol, out var v) && v?.ToString() == idVal))
                    row[setCol] = setVal;
            return null!;
        };

        registry.VerifierFuncs["db.row.count"] = args =>
            (object)(double)((args[0] as List<Dictionary<string, object>>)?.Count ?? 0);

        registry.VerifierFuncs["db.row.get"] = args =>
        {
            var rows = args[0] as List<Dictionary<string, object>>;
            int idx  = (int)Convert.ToDouble(args[1]);
            string col = args[2]?.ToString() ?? "";
            if (rows == null || idx < 0 || idx >= rows.Count) return (object)"";
            return rows[idx].TryGetValue(col, out var val) ? val ?? (object)"" : (object)"";
        };

        // ── Transpiler ──────────────────────────────────────────────────────────

        registry.TranspilerEmitters["db.connect"]  = (a, r) => $"_DbConnect({r(a[0])})";
        registry.TranspilerEmitters["db.exec"]      = (a, r) => $"_DbExec({r(a[0])}, {EmitParamsArray(a.Skip(1).ToList(), r)})";
        registry.TranspilerEmitters["db.insert"]    = (a, r) => $"_DbInsert({string.Join(", ", a.Select(r))})";
        registry.TranspilerEmitters["db.find-all"]  = (a, r) => $"_DbFindAll({r(a[0])})";
        registry.TranspilerEmitters["db.find-by"]   = (a, r) => $"_DbFindBy({r(a[0])}, {r(a[1])}, {r(a[2])})";
        registry.TranspilerEmitters["db.delete"]    = (a, r) => $"_DbDelete({r(a[0])}, {r(a[1])}, {r(a[2])})";
        registry.TranspilerEmitters["db.update"]    = (a, r) => $"_DbUpdate({r(a[0])}, {r(a[1])}, {r(a[2])}, {r(a[3])}, {r(a[4])})";
        registry.TranspilerEmitters["db.row.count"] = (a, r) => $"((double)({r(a[0])}).Count)";
        registry.TranspilerEmitters["db.row.get"]   = (a, r) => $"_DbRowGet({r(a[0])}, (int)({r(a[1])}), {r(a[2])})";

        registry.RequiresSqlite = true;

        string[] dbOps = ["db.connect", "db.exec", "db.insert", "db.find-all", "db.find-by",
                          "db.delete", "db.update", "db.row.count", "db.row.get"];
        foreach (var fn in dbOps)
            registry.PermissionRequirements[fn] = "db";
    }

    private static string EmitParamsArray(List<Agentic.Core.Syntax.AstNode> args, Func<Agentic.Core.Syntax.AstNode, string> r)
        => args.Count == 0 ? "Array.Empty<object>()" : $"new object[] {{{string.Join(", ", args.Select(r))}}}";

    /// <summary>
    /// Returns the C# helper code that must be emitted into every program using std.db.
    /// These are local functions that wrap Microsoft.Data.Sqlite.
    /// </summary>
    public static string HelperCode => """
        Microsoft.Data.Sqlite.SqliteConnection? _dbConn = null;
        void _DbConnect(string path) {
          _dbConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
          _dbConn.Open();
        }
        int _DbExec(string sql, object[] args) {
          using var _cmd = _dbConn!.CreateCommand();
          _cmd.CommandText = sql;
          for (int _i = 0; _i < args.Length; _i++) _cmd.Parameters.AddWithValue($"@p{_i}", args[_i]);
          return _cmd.ExecuteNonQuery();
        }
        List<Dictionary<string, object>> _DbQuery(string sql, object[] args) {
          using var _cmd = _dbConn!.CreateCommand();
          _cmd.CommandText = sql;
          for (int _i = 0; _i < args.Length; _i++) _cmd.Parameters.AddWithValue($"@p{_i}", args[_i]);
          using var _r = _cmd.ExecuteReader();
          var _rows = new List<Dictionary<string, object>>();
          while (_r.Read()) {
            var _row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            for (int _i = 0; _i < _r.FieldCount; _i++) _row[_r.GetName(_i)] = _r.GetValue(_i);
            _rows.Add(_row);
          }
          return _rows;
        }
        List<Dictionary<string, object>> _DbFindAll(string table) => _DbQuery($"SELECT * FROM {table}", Array.Empty<object>());
        List<Dictionary<string, object>> _DbFindBy(string table, string col, object val) => _DbQuery($"SELECT * FROM {table} WHERE \"{col}\" = @p0", new object[] { val });
        double _DbInsert(string table, params object[] kv) {
          var _cols = new List<string>(); var _vals = new List<object>();
          for (int _i = 0; _i + 1 < kv.Length; _i += 2) { _cols.Add(kv[_i].ToString()!); _vals.Add(kv[_i+1]); }
          try {
            _DbExec($"INSERT INTO {table} ({string.Join(",", _cols)}) VALUES ({string.Join(",", _cols.Select((_,_i) => $"@p{_i}"))})", _vals.ToArray());
            return 1.0;
          } catch (Microsoft.Data.Sqlite.SqliteException _ex) when (_ex.SqliteErrorCode == 19) {
            return 0.0;  // UNIQUE / NOT NULL constraint — not an error, caller can check
          }
        }
        void _DbDelete(string table, string col, object val) => _DbExec($"DELETE FROM {table} WHERE \"{col}\" = @p0", new object[] { val });
        void _DbUpdate(string table, string idCol, object idVal, string setCol, object setVal) =>
          _DbExec($"UPDATE {table} SET \"{setCol}\" = @p0 WHERE \"{idCol}\" = @p1", new object[] { setVal, idVal });
        string _DbRowGet(List<Dictionary<string, object>> rows, int idx, string col) {
          if (idx < 0 || idx >= rows.Count) return "";
          return rows[idx].TryGetValue(col, out var _v) ? _v?.ToString() ?? "" : "";
        }
        """;
}
