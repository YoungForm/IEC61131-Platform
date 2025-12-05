namespace Plc.Language.St;

public enum DiagnosticSeverity { Info, Warning, Error }

public sealed class Diagnostic
{
    public DiagnosticSeverity Severity { get; }
    public string Message { get; }
    public int Line { get; }
    public string Code { get; }
    public int Column { get; }
    public Diagnostic(DiagnosticSeverity severity, string message, int line, string code = "", int column = 1)
    {
        Severity = severity; Message = message; Line = line; Code = code; Column = column;
    }
}

public sealed class ParseResult
{
    public ProgramUnit? Program { get; }
    public List<Diagnostic> Diagnostics { get; } = new();
    public ParseResult(ProgramUnit? program) { Program = program; }
}

