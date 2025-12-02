using System.Xml.Linq;
using Plc.Ir;

namespace Plc.Xml.Plcopen;

public static class Exporter
{
    public static XDocument ExportProject(Project project)
    {
        var ns = (XNamespace)"http://www.plcopen.org/xml/tc6_0200";
        var root = new XElement(ns + "project",
            new XElement(ns + "types"),
            new XElement(ns + "instances"),
            new XElement(ns + "pous",
                project.Pous.Select(p => ExportPou(ns, p))));
        return new XDocument(root);
    }

    static XElement ExportPou(XNamespace ns, Pou pou)
    {
        var vars = new XElement(ns + "interface",
            new XElement(ns + "localVars",
                pou.Variables.Select(v => new XElement(ns + "variable",
                    new XAttribute("name", v.Name),
                    new XElement(ns + "type", new XElement(ns + v.DataType)),
                    v.Address is null ? null : new XElement(ns + "location", v.Address)))));

        var body = new XElement(ns + "body",
            new XElement(ns + "ST", new XElement(ns + "xhtml")));

        return new XElement(ns + "pou",
            new XAttribute("name", pou.Name),
            new XAttribute("pouType", pou.Type switch { PouType.Program => "program", PouType.Function => "function", _ => "functionBlock" }),
            vars,
            body);
    }
}
