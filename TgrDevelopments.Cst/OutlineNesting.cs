using System;
using System.Collections.Generic;
using System.Linq;

namespace TgrDevelopments.Cst;

/// <summary>Helpers for building nested outline trees from containment or explicit children.</summary>
public static class OutlineNesting
{
    /// <summary>Returns a copy of <paramref name="parent"/> with <paramref name="child"/> appended (level forced to parent+1).</summary>
    public static OutlineNode WithChild(OutlineNode parent, OutlineNode child)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);
        OutlineNode nested = WithLevel(child, parent.Level + 1);
        List<OutlineNode> children = [.. parent.Children, nested];
        return new OutlineNode(parent.Kind, parent.Label, parent.Span, parent.LineSpan, parent.Level, children);
    }

    /// <summary>Returns a copy of <paramref name="node"/> with replaced children and optional level.</summary>
    public static OutlineNode WithChildren(OutlineNode node, IReadOnlyList<OutlineNode> children, int? level = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(children);
        int lvl = level ?? node.Level;
        List<OutlineNode> fixedChildren = children.Select(c => WithLevel(c, lvl + 1)).ToList();
        return new OutlineNode(node.Kind, node.Label, node.Span, node.LineSpan, lvl, fixedChildren);
    }

    /// <summary>Rebuilds <paramref name="node"/> at <paramref name="level"/>, recursively fixing child levels.</summary>
    public static OutlineNode WithLevel(OutlineNode node, int level)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Level == level && node.Children.Count == 0)
            return node;
        List<OutlineNode> children = node.Children.Select(c => WithLevel(c, level + 1)).ToList();
        return new OutlineNode(node.Kind, node.Label, node.Span, node.LineSpan, level, children);
    }

    /// <summary>
    /// Nests a flat outline list by line-span containment.
    /// Parents are wider spans; a node becomes a child when its [From,To] lies strictly inside another node.
    /// </summary>
    public static List<OutlineNode> NestByLineContainment(IReadOnlyList<OutlineNode> flat)
    {
        ArgumentNullException.ThrowIfNull(flat);
        if (flat.Count == 0)
            return [];

        List<OutlineNode> ordered = flat
            .OrderBy(static n => n.LineSpan.Start.Line)
            .ThenByDescending(static n => n.LineSpan.End.Line)
            .ThenBy(static n => n.Span.Start)
            .ToList();

        List<OutlineNode> roots = [];
        // stack of (node being built, mutable children)
        List<(OutlineNode Original, List<OutlineNode> Children)> stack = [];

        foreach (OutlineNode node in ordered)
        {
            while (stack.Count > 0 && !Contains(stack[^1].Original, node))
            {
                (OutlineNode original, List<OutlineNode> children) = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                OutlineNode built = WithChildren(original, children, stack.Count + 1);
                if (stack.Count == 0)
                    roots.Add(built);
                else
                    stack[^1].Children.Add(built);
            }

            // Preserve members already attached (e.g. methods scanned inside a class body).
            List<OutlineNode> seed = node.Children.Count > 0 ? [.. node.Children] : [];
            stack.Add((node, seed));
        }

        while (stack.Count > 0)
        {
            (OutlineNode original, List<OutlineNode> children) = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            OutlineNode built = WithChildren(original, children, stack.Count + 1);
            if (stack.Count == 0)
                roots.Add(built);
            else
                stack[^1].Children.Add(built);
        }

        return roots;
    }

    /// <summary>True when <paramref name="inner"/>'s line span is strictly inside <paramref name="outer"/>.</summary>
    public static bool Contains(OutlineNode outer, OutlineNode inner)
    {
        ArgumentNullException.ThrowIfNull(outer);
        ArgumentNullException.ThrowIfNull(inner);
        int oStart = outer.LineSpan.Start.Line;
        int oEnd = outer.LineSpan.End.Line;
        int iStart = inner.LineSpan.Start.Line;
        int iEnd = inner.LineSpan.End.Line;
        if (iStart < oStart || iEnd > oEnd)
            return false;
        // strict: not the same span
        return iStart > oStart || iEnd < oEnd;
    }
}
