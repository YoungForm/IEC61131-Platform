namespace Plc.Ir;

public enum PouType { Program, Function, FunctionBlock }

public sealed class Pou
{
    public string Name { get; }
    public PouType Type { get; }
    public List<Variable> Variables { get; } = new();
    public Pou(string name, PouType type) { Name = name; Type = type; }
}

public sealed class Variable
{
    public string Name { get; }
    public string DataType { get; }
    public string? Address { get; }
    public Variable(string name, string dataType, string? address = null) { Name = name; DataType = dataType; Address = address; }
}

public sealed class Project
{
    public string Name { get; }
    public List<Pou> Pous { get; } = new();
    public Project(string name) { Name = name; }
}
