using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Html;

/// <summary>HTML language pack (hand tag scanner → nested outline).</summary>
public sealed class HtmlLanguagePack : ILanguagePack, IOutlineProvider
{
    private static readonly string[] Extensions = [".html", ".htm", ".xhtml"];
    private static readonly string[] NamePatterns = Array.Empty<string>();

    private static readonly HashSet<string> Landmarks = new(StringComparer.OrdinalIgnoreCase)
    {
        "html", "head", "body", "header", "nav", "main", "section", "article",
        "aside", "footer", "form", "table", "div", "h1", "h2", "h3", "h4", "h5", "h6",
    };

    private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "br", "img", "hr", "meta", "link", "input", "area", "base", "col",
        "embed", "source", "track", "wbr",
    };

    /// <inheritdoc />
    public string LanguageId => "html";
    /// <inheritdoc />
    public string DisplayName => "HTML";
    /// <inheritdoc />
    public IReadOnlyList<string> FileExtensions => Extensions;
    /// <inheritdoc />
    public IReadOnlyList<string> FileNamePatterns => NamePatterns;
    /// <inheritdoc />
    public SyntaxQuality DefaultQuality => SyntaxQuality.Structural;

    /// <inheritdoc />
    public bool OwnsPath(string relativeOrFileName)
    {
        ArgumentNullException.ThrowIfNull(relativeOrFileName);
        return false;
    }

    /// <inheritdoc />
    public ConcreteSyntaxTree Parse(SourceText source, ParseOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        List<ParseDiagnostic> diagnostics = [];
        SourceText effective = ParseHelpers.ApplyMaxChars(source, options, out string text, diagnostics, out bool isPartial);
        if (ParseHelpers.IsNullOrWhiteSpace(text))
            return TextUtil.EmptyTree(LanguageId, effective, "document", diagnostics, DefaultQuality, isPartial);
        List<SyntaxNode> children = [];
        List<OutlineNode> outlines = [];
        ParseDocument(text, children, outlines, ref isPartial, cancellationToken);
        return TextUtil.Tree(LanguageId, effective, "document", text, children, diagnostics, DefaultQuality, isPartial, outlines);
    }

    /// <inheritdoc />
    public IReadOnlyList<OutlineNode> GetOutline(
        ConcreteSyntaxTree tree,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        cancellationToken.ThrowIfCancellationRequested();
        if (tree.CachedOutline is not null)
            return tree.CachedOutline;
        return Array.Empty<OutlineNode>();
    }

    private static void ParseDocument(
        string text,
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        ref bool isPartial,
        CancellationToken ct)
    {
        LineMap map = new(text);
        bool partialFlag = isPartial;
        Stack<(string Name, int Start, List<OutlineNode> Ch)> stack = new();
        stack.Push(("(root)", 1, []));

        int i = 0;
        while (i < text.Length)
        {
            ct.ThrowIfCancellationRequested();
            if (text[i] != '<')
            {
                i++;
                continue;
            }

            // comment
            if (i + 3 < text.Length && text.AsSpan(i, 4).SequenceEqual("<!--"))
            {
                int endc = text.IndexOf("-->", i + 4, StringComparison.Ordinal);
                i = endc >= 0 ? endc + 3 : text.Length;
                continue;
            }
            // doctype / declaration
            if (i + 1 < text.Length && (text[i + 1] == '!' || text[i + 1] == '?'))
            {
                int gt = text.IndexOf('>', i + 2);
                i = gt >= 0 ? gt + 1 : text.Length;
                continue;
            }

            bool closing = i + 1 < text.Length && text[i + 1] == '/';
            int nameStart = closing ? i + 2 : i + 1;
            if (nameStart >= text.Length || !char.IsLetter(text[nameStart]))
            {
                i++;
                continue;
            }
            int nameEnd = nameStart;
            while (nameEnd < text.Length && (char.IsLetterOrDigit(text[nameEnd]) || text[nameEnd] is '-' or ':' or '_'))
                nameEnd++;
            string tag = text.Substring(nameStart, nameEnd - nameStart);
            string local = tag.Contains(':') ? tag.Split(':')[^1] : tag;

            int gt2 = text.IndexOf('>', nameEnd);
            if (gt2 < 0)
            {
                partialFlag = true;
                break;
            }
            bool self = text[gt2 - 1] == '/' || VoidTags.Contains(local);
            int ln = map.GetLineNumber(i);

            if (closing)
            {
                while (stack.Count > 1)
                {
                    (string Name, int Start, List<OutlineNode> Ch) top = stack.Pop();
                    OutlineNode n = TextUtil.Outline("element", top.Name, map, top.Start, ln, stack.Count, top.Ch);
                    stack.Peek().Ch.Add(n);
                    if (string.Equals(top.Name, local, StringComparison.OrdinalIgnoreCase))
                        break;
                }
            }
            else if (Landmarks.Contains(local))
            {
                if (self)
                {
                    OutlineNode n = TextUtil.Outline("element", local, map, ln, ln, stack.Count);
                    stack.Peek().Ch.Add(n);
                }
                else
                    stack.Push((local, ln, []));
                children.Add(new SyntaxNode("element", map.GetSpanForLines(ln, ln), local));
            }

            i = gt2 + 1;
        }

        while (stack.Count > 1)
        {
            (string Name, int Start, List<OutlineNode> Ch) top = stack.Pop();
            OutlineNode n = TextUtil.Outline("element", top.Name, map, top.Start, map.LineCount, stack.Count, top.Ch);
            stack.Peek().Ch.Add(n);
            partialFlag = true;
        }
        outlines.AddRange(stack.Peek().Ch);
        isPartial = partialFlag;
    }
}
