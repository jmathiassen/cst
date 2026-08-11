using System;
using System.Collections.Generic;

namespace TgrDevelopments.Cst;

/// <summary>Shared text utilities for language packs.</summary>
public static class TextUtil
{
    /// <summary>Splits text into lines without losing final empty line semantics; strips CR.</summary>
    public static string[] SplitLines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<string> lines = [];
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                lines.Add(text.Substring(start, i - start));
                start = i + 1;
            }
            else if (c == '\r')
            {
                lines.Add(text.Substring(start, i - start));
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                start = i + 1;
            }
        }

        if (start <= text.Length)
            lines.Add(text.Substring(start));
        return lines.ToArray();
    }

    /// <summary>Truncates a display label.</summary>
    public static string Truncate(string value, int max)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length <= max)
            return value;
        if (max <= 1)
            return value.Substring(0, max);
        return value.Substring(0, max - 1) + "…";
    }

    /// <summary>Creates an outline node covering inclusive 1-based lines.</summary>
    public static OutlineNode Outline(
        string kind,
        string label,
        LineMap map,
        int fromLine,
        int toLine,
        int level,
        IReadOnlyList<OutlineNode>? children = null)
    {
        TextSpan span = map.GetSpanForLines(fromLine, toLine);
        LinePositionSpan lineSpan = map.GetLineSpan(fromLine, toLine);
        return new OutlineNode(kind, label, span, lineSpan, level, children);
    }

    /// <summary>Builds a simple document tree result.</summary>
    public static ConcreteSyntaxTree Tree(
        string languageId,
        SourceText effective,
        string rootKind,
        string text,
        List<SyntaxNode> children,
        List<ParseDiagnostic> diagnostics,
        SyntaxQuality quality,
        bool isPartial,
        IReadOnlyList<OutlineNode>? outline = null)
    {
        SyntaxNode root = new(rootKind, new TextSpan(0, text.Length), children: children);
        return new ConcreteSyntaxTree(
            languageId, effective, root, diagnostics,
            SyntaxConfidence.Syntax, quality, isPartial, outline);
    }

    /// <summary>Empty-file tree.</summary>
    public static ConcreteSyntaxTree EmptyTree(
        string languageId,
        SourceText effective,
        string rootKind,
        List<ParseDiagnostic> diagnostics,
        SyntaxQuality quality,
        bool isPartial,
        IReadOnlyList<OutlineNode>? outline = null)
    {
        SyntaxNode root = new(rootKind, new TextSpan(0, 0));
        return new ConcreteSyntaxTree(
            languageId, effective, root, diagnostics,
            SyntaxConfidence.Syntax, quality, isPartial, outline ?? Array.Empty<OutlineNode>());
    }
}
