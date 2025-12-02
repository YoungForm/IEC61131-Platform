namespace Plc.Language.St;

public enum DiagnosticSeverity { Info, Warning, Error }

public sealed class Diagnostic
{
    public DiagnosticSeverity Severity { get; }
    public string Message { get; }
    public int Line { get; }
    public Diagnostic(DiagnosticSeverity severity, string message, int line) { Severity = severity; Message = message; Line = line; }
}

public sealed class ParseResult
{
    public ProgramUnit? Program { get; }
    public List<Diagnostic> Diagnostics { get; } = new();
    public ParseResult(ProgramUnit? program) { Program = program; }
}

