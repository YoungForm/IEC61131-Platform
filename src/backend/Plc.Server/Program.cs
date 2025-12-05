using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Xml.Linq;
using System.Linq;
using System.Collections.Concurrent;
using System.Text.Json;
using System.IO;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5000");
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyHeader().AllowAnyMethod().WithOrigins("http://localhost:5173", "http://localhost:5174"));
});
var app = builder.Build();
app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/export/sample", () =>
{
    var project = new Plc.Ir.Project("Sample");
    var pou = new Plc.Ir.Pou("Main", Plc.Ir.PouType.Program);
    pou.Variables.Add(new Plc.Ir.Variable("x", "INT", "%MW0"));
    project.Pous.Add(pou);
    var doc = Plc.Xml.Plcopen.Exporter.ExportProject(project);
    return Results.Text(doc.ToString(), "application/xml");
});

app.MapPost("/compile/st", async (HttpRequest req) =>
{
    using var reader = new StreamReader(req.Body);
    var text = await reader.ReadToEndAsync();
    var result = Plc.Language.St.Parser.Parse(text);
    return Results.Json(new
    {
        program = result.Program?.Name,
        diagnostics = result.Diagnostics.Select(d => new { severity = d.Severity.ToString(), code = d.Code, d.Message, d.Line, column = d.Column }),
        variables = result.Program?.Variables.Select(v => new { name = v.Name, type = v.TypeName, domain = v.Domain, line = v.Line })
    });
});

var sim = new Plc.Runtime.Sim.SimulationService();

app.MapPost("/sim/start", (int? periodMs) => { sim.Start(periodMs ?? 100); return Results.Ok(); });
app.MapPost("/sim/stop", () => { sim.Stop(); return Results.Ok(); });
app.MapGet("/sim/vars", () => Results.Json(sim.Snapshot()));
app.MapPost("/sim/set", (string name, double value) => { sim.Set(name, value); return Results.Ok(); });

var varDefs = new ConcurrentDictionary<string, (string type, string? address)>();

app.MapGet("/vars/defs", () => Results.Json(varDefs.Select(kv => new { name = kv.Key, type = kv.Value.type, address = kv.Value.address })));
app.MapPost("/vars/defs/upsert", (string name, string type, string? address) => { varDefs[name] = (type, address); return Results.Ok(); });
app.MapGet("/vars/validate", (string address) =>
{
    // 简化校验：%I0, %Q1, %M100, 或供应商类似标记
    var ok = System.Text.RegularExpressions.Regex.IsMatch(address, @"^%[IQM][0-9]+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    return Results.Json(new { ok });
});

var project = new Plc.Ir.Project("Workspace");
var canvasStore = new ConcurrentDictionary<string, string>();
var pouVars = new ConcurrentDictionary<string, List<(string name, string type, string? address, string domain)>>();

app.MapGet("/pous/list", () => Results.Json(project.Pous.Select(p => new { p.Name, type = p.Type.ToString() })));
app.MapPost("/pous/create", (string name, string type) =>
{
    var t = type.Equals("Function", StringComparison.OrdinalIgnoreCase) ? Plc.Ir.PouType.Function : type.Equals("FunctionBlock", StringComparison.OrdinalIgnoreCase) ? Plc.Ir.PouType.FunctionBlock : Plc.Ir.PouType.Program;
    if (project.Pous.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return Results.BadRequest("POU 已存在");
    project.Pous.Add(new Plc.Ir.Pou(name, t));
    return Results.Ok();
});
app.MapPost("/pous/delete", (string name) =>
{
    var found = project.Pous.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (found is null) return Results.NotFound();
    project.Pous.Remove(found);
    return Results.Ok();
});

app.MapGet("/pous/canvas", (string name) =>
{
    if (canvasStore.TryGetValue(name, out var json)) return Results.Text(json, "application/json");
    return Results.NotFound();
});

app.MapPost("/pous/canvas/save", async (string name, HttpRequest req) =>
{
    using var reader = new StreamReader(req.Body);
    var json = await reader.ReadToEndAsync();
    canvasStore[name] = json;
    return Results.Ok();
});

app.MapGet("/pous/vars", (string name) =>
{
    var list = pouVars.TryGetValue(name, out var vs) ? vs : new List<(string name, string type, string? address, string domain)>();
    return Results.Json(list.Select(v => new { name = v.name, type = v.type, address = v.address, domain = v.domain }));
});
app.MapPost("/pous/vars/upsert", (string name, string vname, string type, string? address, string domain) =>
{
    var list = pouVars.GetOrAdd(name, _ => new List<(string, string, string?, string)>());
    var idx = list.FindIndex(x => x.name.Equals(vname, StringComparison.OrdinalIgnoreCase));
    if (idx >= 0) list[idx] = (vname, type, string.IsNullOrWhiteSpace(address) ? null : address, domain);
    else list.Add((vname, type, string.IsNullOrWhiteSpace(address) ? null : address, domain));
    return Results.Ok();
});
app.MapPost("/pous/vars/delete", (string name, string vname) =>
{
    if (pouVars.TryGetValue(name, out var list))
    {
        list.RemoveAll(x => x.name.Equals(vname, StringComparison.OrdinalIgnoreCase));
    }
    return Results.Ok();
});

app.MapPost("/import", async (HttpRequest req) =>
{
    using var reader = new StreamReader(req.Body);
    var xml = await reader.ReadToEndAsync();
    var doc = XDocument.Parse(xml);
    var project = Plc.Xml.Plcopen.Importer.ImportProject(doc);
    return Results.Json(new
    {
        project = project.Name,
        pous = project.Pous.Select(p => new { p.Name, type = p.Type.ToString(), vars = p.Variables.Select(v => new { v.Name, v.DataType }) })
    });
});

app.MapGet("/export/pou", (string name) =>
{
    var ns = (XNamespace)"http://www.plcopen.org/xml/tc6_0200";
    var doc = new XDocument(new XElement(ns + "project"));
    var root = doc.Root!;
    var p = new Plc.Ir.Pou(name, Plc.Ir.PouType.Program);
    var iface = new XElement(ns + "interface");
    var pvars = pouVars.TryGetValue(name, out var lvs) ? lvs : new List<(string name, string type, string? address, string domain)>();
    if (pvars.Any(v => v.domain.Equals("input", StringComparison.OrdinalIgnoreCase)))
    {
        iface.Add(new XElement(ns + "inputVars",
            pvars.Where(v => v.domain.Equals("input", StringComparison.OrdinalIgnoreCase)).Select(v => new XElement(ns + "variable",
                new XAttribute("name", v.name),
                new XElement(ns + "type", new XElement(ns + v.type)),
                string.IsNullOrEmpty(v.address) ? null : new XElement(ns + "location", v.address)))));
    }
    if (pvars.Any(v => v.domain.Equals("output", StringComparison.OrdinalIgnoreCase)))
    {
        iface.Add(new XElement(ns + "outputVars",
            pvars.Where(v => v.domain.Equals("output", StringComparison.OrdinalIgnoreCase)).Select(v => new XElement(ns + "variable",
                new XAttribute("name", v.name),
                new XElement(ns + "type", new XElement(ns + v.type)),
                string.IsNullOrEmpty(v.address) ? null : new XElement(ns + "location", v.address)))));
    }
    if (pvars.Any(v => v.domain.Equals("inout", StringComparison.OrdinalIgnoreCase)))
    {
        iface.Add(new XElement(ns + "inOutVars",
            pvars.Where(v => v.domain.Equals("inout", StringComparison.OrdinalIgnoreCase)).Select(v => new XElement(ns + "variable",
                new XAttribute("name", v.name),
                new XElement(ns + "type", new XElement(ns + v.type)),
                string.IsNullOrEmpty(v.address) ? null : new XElement(ns + "location", v.address)))));
    }
    iface.Add(new XElement(ns + "localVars",
        pvars.Where(v => v.domain.Equals("local", StringComparison.OrdinalIgnoreCase)).Select(v => new XElement(ns + "variable",
            new XAttribute("name", v.name),
            new XElement(ns + "type", new XElement(ns + v.type)),
            string.IsNullOrEmpty(v.address) ? null : new XElement(ns + "location", v.address)))));
    var body = new XElement(ns + "body");

    if (canvasStore.TryGetValue(name, out var json))
    {
        var nodes = new List<Plc.Xml.Plcopen.FbdExporter.NodeModel>();
        var edges = new List<Plc.Xml.Plcopen.FbdExporter.EdgeModel>();
        using var docJson = JsonDocument.Parse(json);
        var rootJson = docJson.RootElement;
        foreach (var n in rootJson.GetProperty("nodes").EnumerateArray())
        {
            var nm = new Plc.Xml.Plcopen.FbdExporter.NodeModel
            {
                Id = n.GetProperty("id").GetString()!,
                Type = n.GetProperty("type").GetString()!,
                X = n.TryGetProperty("position", out var pos) && pos.TryGetProperty("x", out var x) ? x.GetDouble() : 0,
                Y = n.TryGetProperty("position", out var pos2) && pos2.TryGetProperty("y", out var y) ? y.GetDouble() : 0
            };
            var data = new Dictionary<string, object>();
            if (n.TryGetProperty("data", out var d))
            {
                foreach (var prop in d.EnumerateObject())
                {
                    if ((prop.Name == "inPorts" || prop.Name == "outPorts") && prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        var parts = new List<string>();
                        foreach (var pe in prop.Value.EnumerateArray())
                        {
                            var nmPort = pe.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
                            var tpPort = pe.TryGetProperty("type", out var tEl) ? tEl.GetString() : null;
                            if (!string.IsNullOrEmpty(nmPort) && !string.IsNullOrEmpty(tpPort)) parts.Add($"{nmPort}:{tpPort}");
                        }
                        data[prop.Name] = string.Join("|", parts);
                    }
                    else
                    {
                        data[prop.Name] = prop.Value.ToString();
                    }
                }
            }
            nm.Data = data;
            nodes.Add(nm);
        }
        foreach (var e in rootJson.GetProperty("edges").EnumerateArray())
        {
            var em = new Plc.Xml.Plcopen.FbdExporter.EdgeModel
            {
                Source = e.GetProperty("source").GetString()!,
                Target = e.GetProperty("target").GetString()!,
                SourceHandle = e.TryGetProperty("sourceHandle", out var sh) ? sh.GetString() : null,
                TargetHandle = e.TryGetProperty("targetHandle", out var th) ? th.GetString() : null
            };
            edges.Add(em);
        }
        var fbd = Plc.Xml.Plcopen.FbdExporter.BuildFbdBody(nodes, edges);
        body.Add(fbd);
    }
    else
    {
        body.Add(new XElement(ns + "ST", new XElement(ns + "xhtml")));
    }

    var pouEl = new XElement(ns + "pou",
        new XAttribute("name", name),
        new XAttribute("pouType", "program"),
        iface,
        body);

    var pous = new XElement(ns + "pous", pouEl);
    root.Add(new XElement(ns + "types"));
    root.Add(new XElement(ns + "instances"));
    root.Add(pous);

    return Results.Text(doc.ToString(), "application/xml");
});

app.MapPost("/import/pou", async (HttpRequest req) =>
{
    using var reader = new StreamReader(req.Body);
    var xml = await reader.ReadToEndAsync();
    var doc = XDocument.Parse(xml);
    var (nodes, edges) = Plc.Xml.Plcopen.FbdImporter.ImportCanvas(doc);
    return Results.Json(new { nodes, edges });
});

app.MapPost("/lsp/st/definition", async (HttpRequest req) =>
{
    using var doc = await JsonDocument.ParseAsync(req.Body);
    var root = doc.RootElement;
    var text = root.GetProperty("text").GetString() ?? "";
    var line = root.GetProperty("line").GetInt32();
    var column = root.GetProperty("column").GetInt32();
    var lines = text.Replace("\r", "").Split('\n');
    if (line < 1 || line > lines.Length) return Results.Json(new { found = false });
    var ln = lines[line - 1];
    int start = Math.Max(0, Math.Min(column - 1, ln.Length - 1));
    int s = start; int e = start;
    while (s > 0 && (char.IsLetterOrDigit(ln[s - 1]) || ln[Math.Max(s - 1,0)] == '_')) s--;
    while (e < ln.Length && (char.IsLetterOrDigit(ln[e]) || ln[e] == '_')) e++;
    var word = ln.Substring(s, Math.Max(0, e - s));
    var result = Plc.Language.St.Parser.Parse(text);
    var decl = result.Program?.Variables.FirstOrDefault(v => v.Name.Equals(word, StringComparison.OrdinalIgnoreCase));
    if (decl is null) return Results.Json(new { found = false });
    return Results.Json(new { found = true, name = decl.Name, line = decl.Line, type = decl.TypeName, domain = decl.Domain });
});

app.MapPost("/lsp/st/rename", async (HttpRequest req) =>
{
    using var doc = await JsonDocument.ParseAsync(req.Body);
    var root = doc.RootElement;
    var text = root.GetProperty("text").GetString() ?? "";
    var line = root.GetProperty("line").GetInt32();
    var column = root.GetProperty("column").GetInt32();
    var newName = root.GetProperty("newName").GetString() ?? "";
    if (!Regex.IsMatch(newName, @"^[A-Za-z_][A-Za-z0-9_]*$")) return Results.BadRequest("名称不合法");
    var lines = text.Replace("\r", "").Split('\n');
    if (line < 1 || line > lines.Length) return Results.Json(new { text });
    var ln = lines[line - 1];
    int start = Math.Max(0, Math.Min(column - 1, ln.Length - 1));
    int s = start; int e = start;
    while (s > 0 && (char.IsLetterOrDigit(ln[s - 1]) || ln[s - 1] == '_')) s--;
    while (e < ln.Length && (char.IsLetterOrDigit(ln[e]) || ln[e] == '_')) e++;
    var word = ln.Substring(s, Math.Max(0, e - s));
    if (string.IsNullOrWhiteSpace(word)) return Results.Json(new { text });
    var parse = Plc.Language.St.Parser.Parse(text);
    var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var v in parse.Program?.Variables ?? Enumerable.Empty<Plc.Language.St.VariableDecl>()) declared.Add(v.Name);
    if (!declared.Contains(word)) return Results.Json(new { text });
    var reId = new Regex(@"[A-Za-z_][A-Za-z0-9_]*");
    for (int i = 0; i < lines.Length; i++)
    {
        var sb = new System.Text.StringBuilder();
        int last = 0;
        foreach (Match m in reId.Matches(lines[i]))
        {
            sb.Append(lines[i], last, m.Index - last);
            var sym = m.Value;
            sb.Append(string.Equals(sym, word, StringComparison.OrdinalIgnoreCase) ? newName : sym);
            last = m.Index + m.Length;
        }
        sb.Append(lines[i], last, lines[i].Length - last);
        lines[i] = sb.ToString();
    }
    return Results.Json(new { text = string.Join("\n", lines) });
});

app.MapPost("/lsp/st/references", async (HttpRequest req) =>
{
    using var doc = await JsonDocument.ParseAsync(req.Body);
    var root = doc.RootElement;
    var text = root.GetProperty("text").GetString() ?? "";
    var line = root.GetProperty("line").GetInt32();
    var column = root.GetProperty("column").GetInt32();
    var lines = text.Replace("\r", "").Split('\n');
    if (line < 1 || line > lines.Length) return Results.Json(new { references = Array.Empty<object>() });
    var ln = lines[line - 1];
    int start = Math.Max(0, Math.Min(column - 1, ln.Length - 1));
    int s = start; int e = start;
    while (s > 0 && (char.IsLetterOrDigit(ln[s - 1]) || ln[s - 1] == '_')) s--;
    while (e < ln.Length && (char.IsLetterOrDigit(ln[e]) || ln[e] == '_')) e++;
    var word = ln.Substring(s, Math.Max(0, e - s));
    if (string.IsNullOrWhiteSpace(word)) return Results.Json(new { references = Array.Empty<object>() });
    var parse = Plc.Language.St.Parser.Parse(text);
    var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var v in parse.Program?.Variables ?? Enumerable.Empty<Plc.Language.St.VariableDecl>()) declared.Add(v.Name);
    if (!declared.Contains(word)) return Results.Json(new { references = Array.Empty<object>() });
    var refs = new List<object>();
    var reId = new Regex(@"[A-Za-z_][A-Za-z0-9_]*");
    for (int i = 0; i < lines.Length; i++)
    {
        foreach (Match m in reId.Matches(lines[i]))
        {
            var sym = m.Value;
            if (string.Equals(sym, word, StringComparison.OrdinalIgnoreCase))
            {
                refs.Add(new { line = i + 1, startColumn = m.Index + 1, endColumn = m.Index + m.Length + 1 });
            }
        }
    }
    return Results.Json(new { name = word, references = refs });
});

app.MapGet("/compile/pou", (string name) =>
{
    var st = app.Services; // placeholder to capture app
    if (!canvasStore.TryGetValue(name, out var _))
    {
        // still generate ST using existing mechanism
    }
    var resSt = app.MapGet; // no-op to avoid unused warning
    var text = GetPouStText(name);
    var result = Plc.Language.St.Parser.Parse(text);
    return Results.Json(new
    {
        name,
        text,
        diagnostics = result.Diagnostics.Select(d => new { severity = d.Severity.ToString(), d.Message, d.Line }),
        variables = result.Program?.Variables.Select(v => new { name = v.Name, type = v.TypeName, domain = v.Domain, line = v.Line })
    });
});

app.MapGet("/compile/project", () =>
{
    var list = new List<object>();
    foreach (var p in project.Pous)
    {
        var text = GetPouStText(p.Name);
        var res = Plc.Language.St.Parser.Parse(text);
        list.Add(new
        {
            name = p.Name,
            diagnostics = res.Diagnostics.Select(d => new { severity = d.Severity.ToString(), code = d.Code, d.Message, d.Line, column = d.Column }),
            variables = res.Program?.Variables.Select(v => new { name = v.Name, type = v.TypeName, domain = v.Domain, line = v.Line })
        });
    }
    return Results.Json(new { items = list });
});

string GetPouStText(string name)
{
    if (!canvasStore.TryGetValue(name, out var json))
    {
        var lines = new List<string>();
        lines.Add($"PROGRAM {name}");
        lines.Add("VAR");
        if (pouVars.TryGetValue(name, out var decls))
        {
            foreach (var v in decls.Where(d => d.domain.Equals("local", StringComparison.OrdinalIgnoreCase)))
            {
                lines.Add($"    {v.name} : {v.type};");
            }
        }
        lines.Add("END_VAR");
        lines.Add("END_PROGRAM");
        return string.Join("\n", lines);
    }
    var req = new DefaultHttpContext().Request; // not used
    using var docJson = JsonDocument.Parse(json);
    var root = docJson.RootElement;
    var nodes = root.GetProperty("nodes").EnumerateArray().ToList();
    var edges = root.GetProperty("edges").EnumerateArray().ToList();
    var varNameByNode = new Dictionary<string, string>();
    foreach (var n in nodes)
    {
        var type = n.GetProperty("type").GetString();
        if (type == "input" || type == "output")
        {
            var label = n.GetProperty("data").GetProperty("label").GetString() ?? "";
            var nameOnly = label.Split(':')[0];
            varNameByNode[n.GetProperty("id").GetString()!] = nameOnly;
        }
    }
    var blocks = nodes.Where(n => n.GetProperty("type").GetString() == "default").ToList();
    var blockIds = blocks.Select(b => b.GetProperty("id").GetString()!).ToHashSet();
    var graph = new Dictionary<string, List<string>>();
    var indeg = new Dictionary<string, int>();
    foreach (var id in blockIds) { graph[id] = new List<string>(); indeg[id] = 0; }
    foreach (var e in edges)
    {
        var s = e.GetProperty("source").GetString();
        var t = e.GetProperty("target").GetString();
        if (s != null && t != null && blockIds.Contains(s) && blockIds.Contains(t))
        {
            graph[s].Add(t);
            indeg[t] = indeg[t] + 1;
        }
    }
    var order = new List<string>();
    var q = new Queue<string>(indeg.Where(kv => kv.Value == 0).Select(kv => kv.Key));
    var visited = new HashSet<string>();
    while (q.Count > 0)
    {
        var u = q.Dequeue(); order.Add(u); visited.Add(u);
        foreach (var v in graph[u]) { indeg[v] = indeg[v] - 1; if (indeg[v] == 0) q.Enqueue(v); }
    }
    order.AddRange(blockIds.Where(id => !visited.Contains(id)));
    var linesSt = new List<string>();
    linesSt.Add($"PROGRAM {name}");
    linesSt.Add("VAR");
    if (pouVars.TryGetValue(name, out var decls2))
    {
        foreach (var v in decls2.Where(d => d.domain.Equals("input", StringComparison.OrdinalIgnoreCase))) linesSt.Add($"    {v.name} : {v.type};");
        foreach (var v in decls2.Where(d => d.domain.Equals("output", StringComparison.OrdinalIgnoreCase))) linesSt.Add($"    {v.name} : {v.type};");
        foreach (var v in decls2.Where(d => d.domain.Equals("inout", StringComparison.OrdinalIgnoreCase))) linesSt.Add($"    {v.name} : {v.type};");
        foreach (var v in decls2.Where(d => d.domain.Equals("local", StringComparison.OrdinalIgnoreCase))) linesSt.Add($"    {v.name} : {v.type};");
    }
    foreach (var b in blocks)
    {
        var label = b.GetProperty("data").GetProperty("label").GetString() ?? "FB";
        var id = b.GetProperty("id").GetString()!;
        linesSt.Add($"    FB_{id} : {label};");
    }
    linesSt.Add("END_VAR");
    foreach (var id in order)
    {
        var b = blocks.First(n => n.GetProperty("id").GetString()! == id);
        var label = b.GetProperty("data").GetProperty("label").GetString() ?? "FB";
        var bData = b.GetProperty("data");
        var bindings = new Dictionary<string, string>();
        if (bData.TryGetProperty("bindings", out var bindObj)) foreach (var prop in bindObj.EnumerateObject()) bindings[prop.Name] = prop.Value.GetString() ?? "";
        var inPortsStr = bData.TryGetProperty("inPorts", out var ips) ? (ips.ValueKind == JsonValueKind.String ? ips.GetString() : null) : null;
        var inPorts = (inPortsStr ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Split(':')[0]).ToList();
        var inputs = new List<string>();
        foreach (var e in edges)
        {
            var tgtId = e.GetProperty("target").GetString();
            if (tgtId == id)
            {
                var tHandle = e.TryGetProperty("targetHandle", out var th) ? th.GetString() : null;
                var port = tHandle != null && tHandle.StartsWith("in:") ? tHandle.Substring(3) : null;
                var srcId = e.GetProperty("source").GetString();
                var sHandle = e.TryGetProperty("sourceHandle", out var sh) ? sh.GetString() : null;
                string srcExpr;
                if (sHandle != null && sHandle.StartsWith("out:"))
                {
                    var p = sHandle.Substring(4);
                    srcExpr = $"FB_{srcId}.{p}";
                }
                else
                {
                    srcExpr = varNameByNode.TryGetValue(srcId!, out var v) ? v : "";
                }
                if (!string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(srcExpr)) inputs.Add($"{port}:={srcExpr}");
                if (port != null) inPorts.Remove(port);
            }
        }
        foreach (var p in inPorts) { if (bindings.TryGetValue(p, out var v) && !string.IsNullOrEmpty(v)) inputs.Add($"{p}:={v}"); }
        var call = inputs.Count > 0 ? string.Join(", ", inputs) : "";
        linesSt.Add(call.Length > 0 ? $"FB_{id}({call});" : $"FB_{id}();");
        foreach (var e in edges)
        {
            var srcId = e.GetProperty("source").GetString();
            if (srcId == id)
            {
                var sHandle = e.TryGetProperty("sourceHandle", out var sh) ? sh.GetString() : null;
                var port = sHandle != null && sHandle.StartsWith("out:") ? sHandle.Substring(4) : null;
                var tgtId = e.GetProperty("target").GetString();
                if (varNameByNode.TryGetValue(tgtId!, out var vname) && !string.IsNullOrEmpty(port)) linesSt.Add($"{vname} := FB_{id}.{port};");
            }
        }
    }
    linesSt.Add("END_PROGRAM");
    return string.Join("\n", linesSt);
}

app.MapGet("/export/project", () =>
{
    var ns = (XNamespace)"http://www.plcopen.org/xml/tc6_0200";
    var doc = new XDocument(new XElement(ns + "project"));
    var root = doc.Root!;
    root.Add(new XElement(ns + "types"));
    root.Add(new XElement(ns + "instances"));
    var pousEl = new XElement(ns + "pous");
    foreach (var p in project.Pous)
    {
        var iface = new XElement(ns + "interface");
        var pvars = pouVars.TryGetValue(p.Name, out var lvs) ? lvs : new List<(string name, string type, string? address, string domain)>();
        if (pvars.Any(v => v.domain.Equals("input", StringComparison.OrdinalIgnoreCase)))
        {
            iface.Add(new XElement(ns + "inputVars",
                pvars.Where(v => v.domain.Equals("input", StringComparison.OrdinalIgnoreCase)).Select(v => new XElement(ns + "variable",
                    new XAttribute("name", v.name),
                    new XElement(ns + "type", new XElement(ns + v.type)),
                    string.IsNullOrEmpty(v.address) ? null : new XElement(ns + "location", v.address)))));
        }
        if (pvars.Any(v => v.domain.Equals("output", StringComparison.OrdinalIgnoreCase)))
        {
            iface.Add(new XElement(ns + "outputVars",
                pvars.Where(v => v.domain.Equals("output", StringComparison.OrdinalIgnoreCase)).Select(v => new XElement(ns + "variable",
                    new XAttribute("name", v.name),
                    new XElement(ns + "type", new XElement(ns + v.type)),
                    string.IsNullOrEmpty(v.address) ? null : new XElement(ns + "location", v.address)))));
        }
        if (pvars.Any(v => v.domain.Equals("inout", StringComparison.OrdinalIgnoreCase)))
        {
            iface.Add(new XElement(ns + "inOutVars",
                pvars.Where(v => v.domain.Equals("inout", StringComparison.OrdinalIgnoreCase)).Select(v => new XElement(ns + "variable",
                    new XAttribute("name", v.name),
                    new XElement(ns + "type", new XElement(ns + v.type)),
                    string.IsNullOrEmpty(v.address) ? null : new XElement(ns + "location", v.address)))));
        }
        iface.Add(new XElement(ns + "localVars",
            pvars.Where(v => v.domain.Equals("local", StringComparison.OrdinalIgnoreCase)).Select(v => new XElement(ns + "variable",
                new XAttribute("name", v.name),
                new XElement(ns + "type", new XElement(ns + v.type)),
                string.IsNullOrEmpty(v.address) ? null : new XElement(ns + "location", v.address)))));
        var body = new XElement(ns + "body");
        if (canvasStore.TryGetValue(p.Name, out var json))
        {
            var nodes = new List<Plc.Xml.Plcopen.FbdExporter.NodeModel>();
            var edges = new List<Plc.Xml.Plcopen.FbdExporter.EdgeModel>();
            using var docJson = JsonDocument.Parse(json);
            var rootJson = docJson.RootElement;
            foreach (var n in rootJson.GetProperty("nodes").EnumerateArray())
            {
                var nm = new Plc.Xml.Plcopen.FbdExporter.NodeModel
                {
                    Id = n.GetProperty("id").GetString()!,
                    Type = n.GetProperty("type").GetString()!,
                    X = n.TryGetProperty("position", out var pos) && pos.TryGetProperty("x", out var x) ? x.GetDouble() : 0,
                    Y = n.TryGetProperty("position", out var pos2) && pos2.TryGetProperty("y", out var y) ? y.GetDouble() : 0
                };
                var data = new Dictionary<string, object>();
                if (n.TryGetProperty("data", out var d))
                {
                    foreach (var prop in d.EnumerateObject())
                    {
                        if ((prop.Name == "inPorts" || prop.Name == "outPorts") && prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            var parts = new List<string>();
                            foreach (var pe in prop.Value.EnumerateArray())
                            {
                                var nmPort = pe.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
                                var tpPort = pe.TryGetProperty("type", out var tEl) ? tEl.GetString() : null;
                                if (!string.IsNullOrEmpty(nmPort) && !string.IsNullOrEmpty(tpPort)) parts.Add($"{nmPort}:{tpPort}");
                            }
                            data[prop.Name] = string.Join("|", parts);
                        }
                        else
                        {
                            data[prop.Name] = prop.Value.ToString();
                        }
                    }
                }
                nm.Data = data;
                nodes.Add(nm);
            }
            foreach (var e in rootJson.GetProperty("edges").EnumerateArray())
            {
                var em = new Plc.Xml.Plcopen.FbdExporter.EdgeModel
                {
                    Source = e.GetProperty("source").GetString()!,
                    Target = e.GetProperty("target").GetString()!,
                    SourceHandle = e.TryGetProperty("sourceHandle", out var sh) ? sh.GetString() : null,
                    TargetHandle = e.TryGetProperty("targetHandle", out var th) ? th.GetString() : null
                };
                edges.Add(em);
            }
            var fbd = Plc.Xml.Plcopen.FbdExporter.BuildFbdBody(nodes, edges);
            body.Add(fbd);
        }
        else
        {
            body.Add(new XElement(ns + "ST", new XElement(ns + "xhtml")));
        }
        pousEl.Add(new XElement(ns + "pou",
            new XAttribute("name", p.Name),
            new XAttribute("pouType", p.Type == Plc.Ir.PouType.Program ? "program" : p.Type == Plc.Ir.PouType.Function ? "function" : "functionBlock"),
            iface,
            body));
    }
    root.Add(pousEl);
    return Results.Text(doc.ToString(), "application/xml");
});

app.MapPost("/project/save", () =>
{
    var payload = new
    {
        pous = project.Pous.Select(p => new { p.Name, type = p.Type.ToString() }).ToArray(),
        pouVars = pouVars.Select(kv => new { name = kv.Key, vars = kv.Value.Select(v => new { name = v.name, type = v.type, address = v.address, domain = v.domain }).ToArray() }).ToArray(),
        canvases = canvasStore.Select(kv => new { name = kv.Key, json = kv.Value }).ToArray()
    };
    var path = Path.Combine(AppContext.BaseDirectory, "workspace.json");
    File.WriteAllText(path, JsonSerializer.Serialize(payload));
    return Results.Ok(new { path });
});

app.MapPost("/project/load", () =>
{
    var path = Path.Combine(AppContext.BaseDirectory, "workspace.json");
    if (!File.Exists(path)) return Results.NotFound();
    using var doc = JsonDocument.Parse(File.ReadAllText(path));
    var root = doc.RootElement;
    project.Pous.Clear();
    varDefs.Clear();
    pouVars.Clear();
    canvasStore.Clear();
    if (root.TryGetProperty("pous", out var pArr))
    {
        foreach (var p in pArr.EnumerateArray())
        {
            var name = p.GetProperty("Name").GetString() ?? p.GetProperty("name").GetString() ?? "POU";
            var typeStr = p.GetProperty("type").GetString() ?? "Program";
            var t = typeStr.Equals("Function", StringComparison.OrdinalIgnoreCase) ? Plc.Ir.PouType.Function : typeStr.Equals("FunctionBlock", StringComparison.OrdinalIgnoreCase) ? Plc.Ir.PouType.FunctionBlock : Plc.Ir.PouType.Program;
            project.Pous.Add(new Plc.Ir.Pou(name, t));
        }
    }
    if (root.TryGetProperty("pouVars", out var pvArr))
    {
        foreach (var item in pvArr.EnumerateArray())
        {
            var pname = item.GetProperty("name").GetString() ?? "POU";
            var list = new List<(string name, string type, string? address, string domain)>();
            foreach (var v in item.GetProperty("vars").EnumerateArray())
            {
                var vname = v.GetProperty("name").GetString() ?? "var";
                var vtype = v.GetProperty("type").GetString() ?? "INT";
                var vaddr = v.TryGetProperty("address", out var a) ? a.GetString() : null;
                var vdom = v.GetProperty("domain").GetString() ?? "local";
                list.Add((vname, vtype, vaddr, vdom));
            }
            pouVars[pname] = list;
        }
    }
    if (root.TryGetProperty("canvases", out var cArr))
    {
        foreach (var c in cArr.EnumerateArray())
        {
            var name = c.GetProperty("name").GetString() ?? "POU";
            var json = c.GetProperty("json").GetString() ?? "{}";
            canvasStore[name] = json;
        }
    }
    return Results.Ok();
});

app.MapGet("/project/export-json", () =>
{
    var payload = new
    {
        pous = project.Pous.Select(p => new { p.Name, type = p.Type.ToString() }).ToArray(),
        vars = varDefs.Select(kv => new { name = kv.Key, type = kv.Value.type, address = kv.Value.address }).ToArray(),
        canvases = canvasStore.Select(kv => new { name = kv.Key, json = kv.Value }).ToArray()
    };
    return Results.Json(payload);
});

app.MapPost("/project/import-json", async (HttpRequest req) =>
{
    using var reader = new StreamReader(req.Body);
    var text = await reader.ReadToEndAsync();
    using var doc = JsonDocument.Parse(text);
    var root = doc.RootElement;
    project.Pous.Clear();
    varDefs.Clear();
    canvasStore.Clear();
    if (root.TryGetProperty("pous", out var pArr))
    {
        foreach (var p in pArr.EnumerateArray())
        {
            var name = p.GetProperty("Name").GetString() ?? p.GetProperty("name").GetString() ?? "POU";
            var typeStr = p.GetProperty("type").GetString() ?? "Program";
            var t = typeStr.Equals("Function", StringComparison.OrdinalIgnoreCase) ? Plc.Ir.PouType.Function : typeStr.Equals("FunctionBlock", StringComparison.OrdinalIgnoreCase) ? Plc.Ir.PouType.FunctionBlock : Plc.Ir.PouType.Program;
            project.Pous.Add(new Plc.Ir.Pou(name, t));
        }
    }
    if (root.TryGetProperty("vars", out var vArr))
    {
        foreach (var v in vArr.EnumerateArray())
        {
            var name = v.GetProperty("name").GetString() ?? "var";
            var type = v.GetProperty("type").GetString() ?? "INT";
            var address = v.TryGetProperty("address", out var a) ? a.GetString() : null;
            varDefs[name] = (type, address);
        }
    }
    if (root.TryGetProperty("canvases", out var cArr))
    {
        foreach (var c in cArr.EnumerateArray())
        {
            var name = c.GetProperty("name").GetString() ?? "POU";
            var json = c.GetProperty("json").GetString() ?? "{}";
            canvasStore[name] = json;
        }
    }
    return Results.Ok();
});

app.MapGet("/pous/st", (string name) =>
{
    if (!canvasStore.TryGetValue(name, out var json)) return Results.NotFound();
    using var docJson = JsonDocument.Parse(json);
    var root = docJson.RootElement;
    var nodes = root.GetProperty("nodes").EnumerateArray().ToList();
    var edges = root.GetProperty("edges").EnumerateArray().ToList();
    var varNameByNode = new Dictionary<string, string>();
    foreach (var n in nodes)
    {
        var type = n.GetProperty("type").GetString();
        if (type == "input" || type == "output")
        {
            var label = n.GetProperty("data").GetProperty("label").GetString() ?? "";
            var nameOnly = label.Split(':')[0];
            varNameByNode[n.GetProperty("id").GetString()!] = nameOnly;
        }
    }
    var blocks = nodes.Where(n => n.GetProperty("type").GetString() == "default").ToList();
    var blockIds = blocks.Select(b => b.GetProperty("id").GetString()!).ToHashSet();
    var graph = new Dictionary<string, List<string>>();
    var indeg = new Dictionary<string, int>();
    foreach (var id in blockIds) { graph[id] = new List<string>(); indeg[id] = 0; }
    foreach (var e in edges)
    {
        var s = e.GetProperty("source").GetString();
        var t = e.GetProperty("target").GetString();
        if (s != null && t != null && blockIds.Contains(s) && blockIds.Contains(t))
        {
            graph[s].Add(t);
            indeg[t] = indeg[t] + 1;
        }
    }
    var order = new List<string>();
    var q = new Queue<string>(indeg.Where(kv => kv.Value == 0).Select(kv => kv.Key));
    var visited = new HashSet<string>();
    while (q.Count > 0)
    {
        var u = q.Dequeue(); order.Add(u); visited.Add(u);
        foreach (var v in graph[u]) { indeg[v] = indeg[v] - 1; if (indeg[v] == 0) q.Enqueue(v); }
    }
    order.AddRange(blockIds.Where(id => !visited.Contains(id))); // 处理环
    var lines = new List<string>();
    lines.Add($"PROGRAM {name}");
    lines.Add("VAR");
    // declare interface variables
    if (pouVars.TryGetValue(name, out var decls))
    {
        lines.Add("    // 输入变量");
        foreach (var v in decls.Where(d => d.domain.Equals("input", StringComparison.OrdinalIgnoreCase)))
        {
            lines.Add($"    {v.name} : {v.type};");
        }
        lines.Add("    // 输出变量");
        foreach (var v in decls.Where(d => d.domain.Equals("output", StringComparison.OrdinalIgnoreCase)))
        {
            lines.Add($"    {v.name} : {v.type};");
        }
        lines.Add("    // 局部变量");
        foreach (var v in decls.Where(d => d.domain.Equals("local", StringComparison.OrdinalIgnoreCase)))
        {
            lines.Add($"    {v.name} : {v.type};");
        }
    }
    // declare FB instances
    foreach (var b in blocks)
    {
        var label = b.GetProperty("data").GetProperty("label").GetString() ?? "FB";
        var id = b.GetProperty("id").GetString()!;
        lines.Add($"    FB_{id} : {label};");
    }
    lines.Add("END_VAR");
    foreach (var id in order)
    {
        var b = blocks.First(n => n.GetProperty("id").GetString()! == id);
        var label = b.GetProperty("data").GetProperty("label").GetString() ?? "FB";
        var bData = b.GetProperty("data");
        var bindings = new Dictionary<string, string>();
        if (bData.TryGetProperty("bindings", out var bindObj))
        {
            foreach (var prop in bindObj.EnumerateObject()) bindings[prop.Name] = prop.Value.GetString() ?? "";
        }
        var inPortsStr = bData.TryGetProperty("inPorts", out var ips) ? (ips.ValueKind == JsonValueKind.String ? ips.GetString() : null) : null;
        var inPorts = (inPortsStr ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Split(':')[0]).ToList();
        var inputs = new List<string>();
        foreach (var e in edges)
        {
            var tgtId = e.GetProperty("target").GetString();
            if (tgtId == id)
            {
                var tHandle = e.TryGetProperty("targetHandle", out var th) ? th.GetString() : null;
                var port = tHandle != null && tHandle.StartsWith("in:") ? tHandle.Substring(3) : null;
                var srcId = e.GetProperty("source").GetString();
                var sHandle = e.TryGetProperty("sourceHandle", out var sh) ? sh.GetString() : null;
                string srcExpr;
                if (sHandle != null && sHandle.StartsWith("out:"))
                {
                    var p = sHandle.Substring(4);
                    srcExpr = $"FB_{srcId}.{p}";
                }
                else
                {
                    srcExpr = varNameByNode.TryGetValue(srcId!, out var v) ? v : "";
                }
                if (!string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(srcExpr)) inputs.Add($"{port}:={srcExpr}");
                if (port != null) inPorts.Remove(port);
            }
        }
        foreach (var p in inPorts)
        {
            if (bindings.TryGetValue(p, out var v) && !string.IsNullOrEmpty(v)) inputs.Add($"{p}:={v}");
        }
        if (label is string lab && (lab.Equals("TON", StringComparison.OrdinalIgnoreCase) || lab.Equals("TOF", StringComparison.OrdinalIgnoreCase) || lab.Equals("TP", StringComparison.OrdinalIgnoreCase)))
        {
            if (bData.TryGetProperty("params", out var prm) && prm.TryGetProperty("PT", out var ptEl) && ptEl.ValueKind == JsonValueKind.Number)
            {
                var ms = ptEl.GetInt32(); inputs.Add($"PT:=T#{ms}ms");
            }
        }
        if (label is string lab2 && (lab2.Equals("CTU", StringComparison.OrdinalIgnoreCase) || lab2.Equals("CTD", StringComparison.OrdinalIgnoreCase)))
        {
            if (bData.TryGetProperty("params", out var prm2) && prm2.TryGetProperty("PV", out var pvEl) && pvEl.ValueKind == JsonValueKind.Number)
            {
                var pv = pvEl.GetInt32(); inputs.Add($"PV:={pv}");
            }
        }
        var call = inputs.Count > 0 ? string.Join(", ", inputs) : "";
        lines.Add(call.Length > 0 ? $"FB_{id}({call});" : $"FB_{id}();");
        foreach (var e in edges)
        {
            var srcId = e.GetProperty("source").GetString();
            if (srcId == id)
            {
                var sHandle = e.TryGetProperty("sourceHandle", out var sh) ? sh.GetString() : null;
                var port = sHandle != null && sHandle.StartsWith("out:") ? sHandle.Substring(4) : null;
                var tgtId = e.GetProperty("target").GetString();
                if (varNameByNode.TryGetValue(tgtId!, out var vname) && !string.IsNullOrEmpty(port))
                {
                    lines.Add($"{vname} := FB_{id}.{port};");
                }
            }
        }
    }
    lines.Add("END_PROGRAM");
    var text = string.Join("\n", lines);
    return Results.Text(text, "text/plain");
});

app.Run();
