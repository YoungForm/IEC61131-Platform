namespace Plc.Language.St;

public sealed class ProgramUnit
{
    public string Name { get; }
    public List<VariableDecl> Variables { get; } = new();
    public List<IStatement> Statements { get; } = new();
    public ProgramUnit(string name) => Name = name;
}

public sealed class VariableDecl
{
    public string Name { get; }
    public string TypeName { get; }
    public string Domain { get; }
    public int Line { get; }
    public VariableDecl(string name, string typeName, string domain = "local", int line = 0) { Name = name; TypeName = typeName; Domain = domain; Line = line; }
}

public interface IStatement { }

public sealed class Assignment : IStatement
{
    public string Target { get; }
    public string Expression { get; }
    public int Line { get; }
    public Assignment(string target, string expression, int line) { Target = target; Expression = expression; Line = line; }
}

