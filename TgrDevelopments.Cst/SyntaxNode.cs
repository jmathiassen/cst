using System;
using System.Collections.Generic;

namespace TgrDevelopments.Cst;

/// <summary>Single node in a concrete syntax tree.</summary>
public sealed class SyntaxNode
{
    /// <summary>Pack-defined stable kind string (e.g. <c>function_declaration</c>).</summary>
    public string Kind { get; }

    /// <summary>Optional display or extract name.</summary>
    public string? Name { get; }

    /// <summary>Full node span in UTF-16 offsets.</summary>
    public TextSpan Span { get; }

    /// <summary>Identifier span when applicable.</summary>
    public TextSpan? NameSpan { get; }

    /// <summary>Child nodes.</summary>
    public IReadOnlyList<SyntaxNode> Children { get; }

    /// <summary>Optional property bag.</summary>
    public IReadOnlyDictionary<string, string>? Properties { get; }

    /// <summary>Creates a syntax node.</summary>
    public SyntaxNode(
        string kind,
        TextSpan span,
        string? name = null,
        TextSpan? nameSpan = null,
        IReadOnlyList<SyntaxNode>? children = null,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        ArgumentNullException.ThrowIfNull(kind);
        Kind = kind;
        Span = span;
        Name = name;
        NameSpan = nameSpan;
        Children = children ?? Array.Empty<SyntaxNode>();
        Properties = properties;
    }
}
