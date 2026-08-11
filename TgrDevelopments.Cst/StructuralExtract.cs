using System;
using System.Collections.Generic;
using System.Threading;

namespace TgrDevelopments.Cst;

/// <summary>Declared symbol extracted at syntax confidence.</summary>
public sealed class DeclSymbol
{
    /// <summary>Symbol name.</summary>
    public string Name { get; }

    /// <summary>Kind string (e.g. <c>function</c>, <c>class</c>).</summary>
    public string Kind { get; }

    /// <summary>Full span.</summary>
    public TextSpan Span { get; }

    /// <summary>Name identifier span when known.</summary>
    public TextSpan? NameSpan { get; }

    /// <summary>Optional container name.</summary>
    public string? ContainerName { get; }

    /// <summary>Optional signature display.</summary>
    public string? Signature { get; }

    /// <summary>Optional 1-based start line.</summary>
    public int? StartLine { get; }

    /// <summary>Optional 1-based end line.</summary>
    public int? EndLine { get; }

    /// <summary>Creates a declaration symbol.</summary>
    public DeclSymbol(
        string name,
        string kind,
        TextSpan span,
        TextSpan? nameSpan = null,
        string? containerName = null,
        string? signature = null,
        int? startLine = null,
        int? endLine = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(kind);
        Name = name;
        Kind = kind;
        Span = span;
        NameSpan = nameSpan;
        ContainerName = containerName;
        Signature = signature;
        StartLine = startLine;
        EndLine = endLine;
    }
}

/// <summary>Name-level call site (same-file).</summary>
public sealed class CallSite
{
    /// <summary>Callee simple name / tail.</summary>
    public string CalleeName { get; }

    /// <summary>Call span.</summary>
    public TextSpan Span { get; }

    /// <summary>Enclosing declaration name when known.</summary>
    public string? EnclosingDeclName { get; }

    /// <summary>Optional 1-based line.</summary>
    public int? Line { get; }

    /// <summary>Creates a call site.</summary>
    public CallSite(string calleeName, TextSpan span, string? enclosingDeclName = null, int? line = null)
    {
        ArgumentNullException.ThrowIfNull(calleeName);
        CalleeName = calleeName;
        Span = span;
        EnclosingDeclName = enclosingDeclName;
        Line = line;
    }
}

/// <summary>Structural edge (project refs, etc.).</summary>
public sealed class ExtractEdge
{
    /// <summary>Edge kind.</summary>
    public string Kind { get; }

    /// <summary>From endpoint.</summary>
    public string From { get; }

    /// <summary>To endpoint.</summary>
    public string To { get; }

    /// <summary>Optional source span.</summary>
    public TextSpan? Span { get; }

    /// <summary>Optional properties.</summary>
    public IReadOnlyDictionary<string, string>? Properties { get; }

    /// <summary>Creates an extract edge.</summary>
    public ExtractEdge(
        string kind,
        string from,
        string to,
        TextSpan? span = null,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        Kind = kind;
        From = from;
        To = to;
        Span = span;
        Properties = properties;
    }
}

/// <summary>Result of structural extraction.</summary>
public sealed class StructuralExtract
{
    /// <summary>Language id.</summary>
    public string LanguageId { get; }

    /// <summary>Always syntax in v1.</summary>
    public SyntaxConfidence Confidence { get; }

    /// <summary>Quality grade.</summary>
    public SyntaxQuality Quality { get; }

    /// <summary>Declarations.</summary>
    public IReadOnlyList<DeclSymbol> Declarations { get; }

    /// <summary>Same-file name-level calls.</summary>
    public IReadOnlyList<CallSite> Calls { get; }

    /// <summary>Structural edges.</summary>
    public IReadOnlyList<ExtractEdge> Edges { get; }

    /// <summary>Creates an extract result.</summary>
    public StructuralExtract(
        string languageId,
        IReadOnlyList<DeclSymbol>? declarations = null,
        IReadOnlyList<CallSite>? calls = null,
        IReadOnlyList<ExtractEdge>? edges = null,
        SyntaxConfidence confidence = SyntaxConfidence.Syntax,
        SyntaxQuality quality = SyntaxQuality.Preview)
    {
        ArgumentNullException.ThrowIfNull(languageId);
        LanguageId = languageId;
        Declarations = declarations ?? Array.Empty<DeclSymbol>();
        Calls = calls ?? Array.Empty<CallSite>();
        Edges = edges ?? Array.Empty<ExtractEdge>();
        Confidence = confidence;
        Quality = quality;
    }
}

/// <summary>Optional structural extractor (P4+).</summary>
public interface IStructuralExtractor
{
    /// <summary>Language id this extractor serves.</summary>
    string LanguageId { get; }

    /// <summary>Extracts decls/calls/edges from a tree.</summary>
    StructuralExtract Extract(
        ConcreteSyntaxTree tree,
        CancellationToken cancellationToken = default);
}
