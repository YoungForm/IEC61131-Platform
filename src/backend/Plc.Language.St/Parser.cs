using System.Text.RegularExpressions;

namespace Plc.Language.St;

public static class Parser
{
    public static ParseResult Parse(string source)
    {
        var lines = source.Replace("\r", "").Split('\n');
        ProgramUnit? program = null;
        var res = new ParseResult(program);
        string varSection = ""; // "var" | "input" | "output" | "inout" | "temp"
        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("(*") && line.EndsWith("*)")) continue;
            if (line.StartsWith("PROGRAM ", StringComparison.OrdinalIgnoreCase))
            {
                var name = line.Substring(8).Trim();
                program = new ProgramUnit(name);
                res = new ParseResult(program);
                continue;
            }
            if (line.Equals("VAR", StringComparison.OrdinalIgnoreCase)) { varSection = "var"; continue; }
            if (line.Equals("VAR_INPUT", StringComparison.OrdinalIgnoreCase)) { varSection = "input"; continue; }
            if (line.Equals("VAR_OUTPUT", StringComparison.OrdinalIgnoreCase)) { varSection = "output"; continue; }
            if (line.Equals("VAR_IN_OUT", StringComparison.OrdinalIgnoreCase)) { varSection = "inout"; continue; }
            if (line.Equals("VAR_TEMP", StringComparison.OrdinalIgnoreCase)) { varSection = "temp"; continue; }
            if (line.Equals("END_VAR", StringComparison.OrdinalIgnoreCase)) { varSection = ""; continue; }
            if (line.Equals("END_PROGRAM", StringComparison.OrdinalIgnoreCase)) { break; }

            if (program is null)
            {
                res.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "缺少 PROGRAM 头部", i + 1));
                break;
            }

            if (!string.IsNullOrEmpty(varSection))
            {
                var m = Regex.Match(line, @"^(?<id>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<type>[A-Za-z_][A-Za-z0-9_\.]+).*;\s*$");
                if (m.Success)
                {
                    var domain = varSection == "var" || varSection == "temp" ? "local" : varSection;
                    program.Variables.Add(new VariableDecl(m.Groups["id"].Value, m.Groups["type"].Value, domain, i + 1));
                }
                else
                {
                    continue;
                }
                continue;
            }
            // tolerate common control flow keywords to avoid noisy warnings
            if (Regex.IsMatch(line, @"^(IF |ELSIF |ELSE|END_IF|CASE |OF |END_CASE|FOR |TO |BY |DO|END_FOR|WHILE |END_WHILE|REPEAT|UNTIL |END_REPEAT|RETURN)", RegexOptions.IgnoreCase))
            {
                continue;
            }
            // assignment: id := expr;
            var a = Regex.Match(line, @"^(?<id>[A-Za-z_][A-Za-z0-9_]*)\s*:=\s*(?<expr>.+);\s*$");
            if (a.Success)
            {
                var id = a.Groups["id"].Value;
                if (!program.Variables.Any(v => v.Name.Equals(id, StringComparison.OrdinalIgnoreCase)))
                {
                    res.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, $"未声明的变量: {id}", i + 1));
                }
                program.Statements.Add(new Assignment(id, a.Groups["expr"].Value, i + 1));
                var decl = program.Variables.FirstOrDefault(v => v.Name.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (decl != null && decl.Domain.Equals("input", StringComparison.OrdinalIgnoreCase))
                {
                    res.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "对输入变量赋值", i + 1));
                }
                var expr = a.Groups["expr"].Value;
                foreach (Match mId in Regex.Matches(expr, @"[A-Za-z_][A-Za-z0-9_]*"))
                {
                    var sym = mId.Value;
                    if (Regex.IsMatch(sym, @"^(TRUE|FALSE|AND|OR|NOT)$", RegexOptions.IgnoreCase)) continue;
                    if (!program.Variables.Any(v => v.Name.Equals(sym, StringComparison.OrdinalIgnoreCase)))
                    {
                        res.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, $"未声明的引用: {sym}", i + 1));
                    }
                }
                continue;
            }
            res.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "无法识别的语句", i + 1));
        }
        return res;
    }
}
