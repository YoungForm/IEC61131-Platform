using System.Text.RegularExpressions;

namespace Plc.Language.St;

public static class Parser
{
    public static ParseResult Parse(string source)
    {
        var lines = source.Replace("\r", "").Split('\n');
        ProgramUnit? program = null;
        var res = new ParseResult(program);
        bool inVar = false;
        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("PROGRAM ", StringComparison.OrdinalIgnoreCase))
            {
                var name = line.Substring(8).Trim();
                program = new ProgramUnit(name);
                res = new ParseResult(program);
                continue;
            }
            if (line.Equals("VAR", StringComparison.OrdinalIgnoreCase)) { inVar = true; continue; }
            if (line.Equals("END_VAR", StringComparison.OrdinalIgnoreCase)) { inVar = false; continue; }
            if (line.Equals("END_PROGRAM", StringComparison.OrdinalIgnoreCase)) { break; }

            if (program is null)
            {
                res.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "缺少 PROGRAM 头部", i + 1));
                break;
            }

            if (inVar)
            {
                var m = Regex.Match(line, @"^(?<id>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<type>[A-Za-z_][A-Za-z0-9_]*)\s*;\s*$");
                if (m.Success)
                {
                    program.Variables.Add(new VariableDecl(m.Groups["id"].Value, m.Groups["type"].Value));
                }
                else
                {
                    res.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "变量声明语法错误", i + 1));
                }
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
                continue;
            }
            res.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "无法识别的语句", i + 1));
        }
        return res;
    }
}
