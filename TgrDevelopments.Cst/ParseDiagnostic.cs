using System;

namespace TgrDevelopments.Cst;

/// <summary>A diagnostic produced during parse or outline.</summary>
public sealed class ParseDiagnostic
{
    /// <summary>Stable code (e.g. <c>JS001</c>, <c>CST_LIMIT</c>).</summary>
    public string Code { get; }

    /// <summary>Human-readable message.</summary>
    public string Message { get; }

    /// <summary>Related source span.</summary>
    public TextSpan Span { get; }

    /// <summary>Severity.</summary>
    public DiagnosticSeverity Severity { get; }

    /// <summary>Creates a diagnostic.</summary>
    public ParseDiagnostic(string code, string message, TextSpan span, DiagnosticSeverity severity)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(message);
        Code = code;
        Message = message;
        Span = span;
        Severity = severity;
    }
}
