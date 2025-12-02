using System.Xml.Linq;
using Plc.Ir;

namespace Plc.Xml.Plcopen;

public static class Importer
{
    public static Project ImportProject(XDocument doc)
    {
        var ns = (XNamespace)"http://www.plcopen.org/xml/tc6_0200";
        var root = doc.Root ?? throw new InvalidOperationException("XML 无根节点");
        var project = new Project(root.Attribute("name")?.Value ?? "Imported");
        var pous = root.Element(ns + "pous");
        if (pous != null)
        {
            foreach (var pouEl in pous.Elements(ns + "pou"))
            {
                var name = pouEl.Attribute("name")?.Value ?? "POU";
                var typeAttr = pouEl.Attribute("pouType")?.Value ?? "program";
                var type = typeAttr == "function" ? PouType.Function : typeAttr == "functionBlock" ? PouType.FunctionBlock : PouType.Program;
                var pou = new Pou(name, type);
                var iface = pouEl.Element(ns + "interface");
                var locals = iface?.Element(ns + "localVars");
                if (locals != null)
                {
                    foreach (var v in locals.Elements(ns + "variable"))
                    {
                        var vname = v.Attribute("name")?.Value ?? "var";
                        var typeEl = v.Element(ns + "type");
                        var dt = typeEl?.Elements().FirstOrDefault()?.Name.LocalName ?? "INT";
                        var addr = v.Element(ns + "location")?.Value;
                        pou.Variables.Add(new Variable(vname, dt, addr));
                    }
                }
                project.Pous.Add(pou);
            }
        }
        return project;
    }
}
