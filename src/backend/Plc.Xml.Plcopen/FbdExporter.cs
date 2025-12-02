using System.Xml.Linq;

namespace Plc.Xml.Plcopen;

public static class FbdExporter
{
    public sealed class NodeModel
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public Dictionary<string, object> Data { get; set; } = new();
        public double X { get; set; }
        public double Y { get; set; }
    }

    public sealed class EdgeModel
    {
        public string Source { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string? SourceHandle { get; set; }
        public string? TargetHandle { get; set; }
    }

    public static XElement BuildFbdBody(IEnumerable<NodeModel> nodes, IEnumerable<EdgeModel> edges)
    {
        var ns = (XNamespace)"http://www.plcopen.org/xml/tc6_0200";
        var network = new XElement(ns + "network");

        var idMap = new Dictionary<string, int>();
        int nextId = 1;

        foreach (var n in nodes)
        {
            idMap[n.Id] = nextId++;
            if (n.Type == "default")
            {
                var typeName = (n.Data.TryGetValue("label", out var l) ? l?.ToString() : "FB") ?? "FB";
                var block = new XElement(ns + "block",
                    new XAttribute("localId", idMap[n.Id]),
                    new XAttribute("typeName", typeName),
                    new XAttribute("instanceName", n.Id),
                    new XAttribute("x", n.X),
                    new XAttribute("y", n.Y));
                var inVars = new XElement(ns + "inputVars");
                var outVars = new XElement(ns + "outputVars");
                if (n.Data.TryGetValue("inPorts", out var inPortsObj) && inPortsObj is string inPortsStr)
                {
                    // inPorts serialized from frontend as string; we only declare placeholders
                    foreach (var p in inPortsStr.Split('|'))
                    {
                        var parts = p.Split(':');
                        var nm = parts.Length > 0 ? parts[0] : "";
                        var tp = parts.Length > 1 ? parts[1] : "";
                        if (!string.IsNullOrWhiteSpace(nm))
                        {
                            var v = new XElement(ns + "variable", new XAttribute("formalParameter", nm));
                            if (!string.IsNullOrWhiteSpace(tp)) v.Add(new XElement(ns + "type", new XElement(ns + tp)));
                            inVars.Add(v);
                        }
                    }
                }
                if (n.Data.TryGetValue("outPorts", out var outPortsObj) && outPortsObj is string outPortsStr)
                {
                    foreach (var p in outPortsStr.Split('|'))
                    {
                        var parts = p.Split(':');
                        var nm = parts.Length > 0 ? parts[0] : "";
                        var tp = parts.Length > 1 ? parts[1] : "";
                        if (!string.IsNullOrWhiteSpace(nm))
                        {
                            var v = new XElement(ns + "variable", new XAttribute("formalParameter", nm));
                            if (!string.IsNullOrWhiteSpace(tp)) v.Add(new XElement(ns + "type", new XElement(ns + tp)));
                            outVars.Add(v);
                        }
                    }
                }
                block.Add(inVars);
                block.Add(outVars);
                network.Add(block);
            }
            else if (n.Type == "input")
            {
                var inv = new XElement(ns + "inVariable",
                    new XAttribute("localId", idMap[n.Id]),
                    new XAttribute("x", n.X),
                    new XAttribute("y", n.Y));
                network.Add(inv);
            }
            else if (n.Type == "output")
            {
                var outv = new XElement(ns + "outVariable",
                    new XAttribute("localId", idMap[n.Id]),
                    new XAttribute("x", n.X),
                    new XAttribute("y", n.Y));
                network.Add(outv);
            }
        }

        foreach (var e in edges)
        {
            var conn = new XElement(ns + "connection",
                new XElement(ns + "source",
                    new XAttribute("refLocalId", idMap[e.Source])));
            var dest = new XElement(ns + "destination",
                new XAttribute("refLocalId", idMap[e.Target]));
            if (!string.IsNullOrEmpty(e.TargetHandle) && e.TargetHandle!.StartsWith("in:"))
            {
                dest.Add(new XAttribute("formalParameter", e.TargetHandle!.Substring(3)));
            }
            var srcEl = conn.Element(ns + "source")!;
            if (!string.IsNullOrEmpty(e.SourceHandle) && e.SourceHandle!.StartsWith("out:"))
            {
                srcEl.Add(new XAttribute("formalParameter", e.SourceHandle!.Substring(4)));
            }
            conn.Add(dest);
            network.Add(conn);
        }

        return new XElement(ns + "FBD", network);
    }
}
