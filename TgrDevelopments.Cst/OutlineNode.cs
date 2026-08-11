using System;
using System.Collections.Generic;

namespace TgrDevelopments.Cst;

/// <summary>Navigable outline entry with line anchors.</summary>
public sealed class OutlineNode
{
    /// <summary>Outline kind (e.g. <c>heading</c>, <c>function</c>).</summary>
    public string Kind { get; }

    /// <summary>UI label.</summary>
    public string Label { get; }

    /// <summary>UTF-16 offset span (source of truth).</summary>
    public TextSpan Span { get; }

    /// <summary>1-based inclusive from/to line span for navigation.</summary>
    public LinePositionSpan LineSpan { get; }

    /// <summary>Nesting or heading level.</summary>
    public int Level { get; }

    /// <summary>Child outline nodes.</summary>
    public IReadOnlyList<OutlineNode> Children { get; }

    /// <summary>Creates an outline node.</summary>
    public OutlineNode(
        string kind,
        string label,
        TextSpan span,
        LinePositionSpan lineSpan,
        int level,
        IReadOnlyList<OutlineNode>? children = null)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(label);
        Kind = kind;
        Label = label;
        Span = span;
        LineSpan = lineSpan;
        Level = level;
        Children = children ?? Array.Empty<OutlineNode>();
    }
}
