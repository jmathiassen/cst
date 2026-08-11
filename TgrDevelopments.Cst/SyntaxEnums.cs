namespace TgrDevelopments.Cst;

/// <summary>Confidence of structural results. v1 is always syntax-tier.</summary>
public enum SyntaxConfidence
{
    /// <summary>Structural / CST-class only. Never semantic bind.</summary>
    Syntax = 0
}

/// <summary>Quality grade advertised by a pack or result.</summary>
public enum SyntaxQuality
{
    /// <summary>Good enough for outline/fold/preview.</summary>
    Preview = 0,

    /// <summary>Intentional structural extract (e.g. decls for indexer).</summary>
    Structural = 1,

    /// <summary>Incomplete grammar; tests may be sparse.</summary>
    Experimental = 2
}

/// <summary>Severity of a parse diagnostic.</summary>
public enum DiagnosticSeverity
{
    /// <summary>Informational note.</summary>
    Info = 0,

    /// <summary>Recoverable issue.</summary>
    Warning = 1,

    /// <summary>Syntax error with recovery.</summary>
    Error = 2
}
