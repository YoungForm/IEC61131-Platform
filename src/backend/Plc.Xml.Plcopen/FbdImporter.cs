using System.Xml.Linq;

namespace Plc.Xml.Plcopen;

public static class FbdImporter
{
    public static (List<object> nodes, List<object> edges) ImportCanvas(XDocument doc)
    {
        var ns = (XNamespace)"http://www.plcopen.org/xml/tc6_0200";
        var network = doc.Descendants(ns + "network").FirstOrDefault();
        var nodes = new List<object>();
        var edges = new List<object>();
        if (network == null) return (nodes, edges);

        foreach (var block in network.Elements(ns + "block"))
        {
            var id = (string?)block.Attribute("instanceName") ?? (string?)block.Attribute("localId") ?? Guid.NewGuid().ToString();
            var typeName = (string?)block.Attribute("typeName") ?? "FB";
            var x = (double?)block.Attribute("x") ?? 300; var y = (double?)block.Attribute("y") ?? 100;
            nodes.Add(new { id, type = "default", position = new { x, y }, data = new { label = typeName } });
        }
        foreach (var inv in network.Elements(ns + "inVariable"))
        {
            var id = (string?)inv.Attribute("localId") ?? Guid.NewGuid().ToString();
            var x = (double?)inv.Attribute("x") ?? 100; var y = (double?)inv.Attribute("y") ?? 120;
            nodes.Add(new { id, type = "input", position = new { x, y }, data = new { label = "IN", direction = "out", portType = "BOOL" } });
        }
        foreach (var outv in network.Elements(ns + "outVariable"))
        {
            var id = (string?)outv.Attribute("localId") ?? Guid.NewGuid().ToString();
            var x = (double?)outv.Attribute("x") ?? 500; var y = (double?)outv.Attribute("y") ?? 120;
            nodes.Add(new { id, type = "output", position = new { x, y }, data = new { label = "OUT", direction = "in", portType = "BOOL" } });
        }
        foreach (var conn in network.Elements(ns + "connection"))
        {
            var src = conn.Element(ns + "source");
            var dst = conn.Element(ns + "destination");
            if (src == null || dst == null) continue;
            var sId = (string?)src.Attribute("refLocalId") ?? "";
            var tId = (string?)dst.Attribute("refLocalId") ?? "";
            var sHandle = (string?)src.Attribute("formalParameter");
            var tHandle = (string?)dst.Attribute("formalParameter");
            edges.Add(new { id = Guid.NewGuid().ToString(), source = sId, target = tId, sourceHandle = sHandle != null ? $"out:{sHandle}" : null, targetHandle = tHandle != null ? $"in:{tHandle}" : null });
        }
        return (nodes, edges);
    }
}
