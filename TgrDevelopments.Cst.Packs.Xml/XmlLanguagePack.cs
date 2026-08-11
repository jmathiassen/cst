using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Xml;

/// <summary>XML language pack (hand tag scanner → nested outline).</summary>
public sealed class XmlLanguagePack : ILanguagePack, IOutlineProvider
{
    private static readonly string[] Extensions = [".xml", ".xsl", ".xslt", ".csproj", ".fsproj", ".vbproj", ".props", ".targets", ".config"];
    private static readonly string[] NamePatterns = Array.Empty<string>();

    /// <inheritdoc />
    public string LanguageId => "xml";
    /// <inheritdoc />
    public string DisplayName => "XML";
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
        ParseDocument(text, children, outlines, diagnostics, ref isPartial, cancellationToken);
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
        List<ParseDiagnostic> diagnostics,
        ref bool isPartial,
        CancellationToken ct)
    {
        LineMap map = new(text);
        bool partialFlag = isPartial;
        // MatchName = local tag for close matching; Label = display (may include Include=).
        Stack<(string MatchName, string Label, int Start, List<OutlineNode> Children)> stack = new();
        stack.Push(("", "", 1, []));

        int i = 0;
        while (i < text.Length)
        {
            ct.ThrowIfCancellationRequested();
            if (text[i] != '<')
            {
                i++;
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == '?')
            {
                int endDecl = text.IndexOf("?>", i, StringComparison.Ordinal);
                i = endDecl >= 0 ? endDecl + 2 : text.Length;
                continue;
            }
            if (i + 3 < text.Length && text.AsSpan(i, 4).SequenceEqual("<!--"))
            {
                int endc = text.IndexOf("-->", i + 4, StringComparison.Ordinal);
                i = endc >= 0 ? endc + 3 : text.Length;
                continue;
            }
            if (i + 1 < text.Length && text[i + 1] == '!')
            {
                int gt0 = text.IndexOf('>', i + 2);
                i = gt0 >= 0 ? gt0 + 1 : text.Length;
                continue;
            }

            bool closing = i + 1 < text.Length && text[i + 1] == '/';
            int nameStart = closing ? i + 2 : i + 1;
            if (nameStart >= text.Length || !(char.IsLetter(text[nameStart]) || text[nameStart] == '_'))
            {
                i++;
                continue;
            }
            int nameEnd = nameStart;
            while (nameEnd < text.Length && (char.IsLetterOrDigit(text[nameEnd]) || text[nameEnd] is '.' or '-' or '_' or ':'))
                nameEnd++;
            string namePart = text.Substring(nameStart, nameEnd - nameStart);
            int colon = namePart.IndexOf(':');
            string local = colon >= 0 ? namePart.Substring(colon + 1) : namePart;

            int gt = text.IndexOf('>', nameEnd);
            if (gt < 0)
            {
                partialFlag = true;
                break;
            }
            string attrs = text.Substring(nameEnd, gt - nameEnd);
            bool selfClose = attrs.TrimEnd().EndsWith('/') || (gt > 0 && text[gt - 1] == '/');
            int ln = map.GetLineNumber(i);

            if (closing)
            {
                List<OutlineNode>? finishedChildren = null;
                int startLn = ln;
                string poppedLabel = local;
                while (stack.Count > 1)
                {
                    (string MatchName, string Label, int Start, List<OutlineNode> Children) top = stack.Pop();
                    startLn = top.Start;
                    finishedChildren = top.Children;
                    poppedLabel = top.Label.Length > 0 ? top.Label : top.MatchName;
                    if (string.Equals(top.MatchName, local, StringComparison.OrdinalIgnoreCase))
                        break;
                }
                OutlineNode node = TextUtil.Outline("element", poppedLabel, map, startLn, ln, stack.Count, finishedChildren ?? []);
                stack.Peek().Children.Add(node);
                children.Add(new SyntaxNode("element", map.GetSpanForLines(startLn, ln), poppedLabel));
            }
            else
            {
                string label = local;
                string? include = FindAttr(attrs, "Include");
                if (include is not null)
                    label = TextUtil.Truncate(local + " " + include, 40);

                if (selfClose)
                {
                    OutlineNode node = TextUtil.Outline("element", label, map, ln, ln, stack.Count);
                    stack.Peek().Children.Add(node);
                    children.Add(new SyntaxNode("element", map.GetSpanForLines(ln, ln), local));
                }
                else
                    stack.Push((local, label, ln, []));
            }

            i = gt + 1;
        }

        while (stack.Count > 1)
        {
            partialFlag = true;
            (string MatchName, string Label, int Start, List<OutlineNode> Children) top = stack.Pop();
            string display = top.Label.Length > 0 ? top.Label : top.MatchName;
            OutlineNode node = TextUtil.Outline("element", display, map, top.Start, map.LineCount, stack.Count, top.Children);
            stack.Peek().Children.Add(node);
            diagnostics.Add(new ParseDiagnostic("XML001", "Unclosed element.", map.GetSpanForLines(top.Start, top.Start), DiagnosticSeverity.Error));
        }
        outlines.AddRange(stack.Peek().Children);
        isPartial = partialFlag;
    }

    private static string? FindAttr(string attrs, string name)
    {
        // hand-scan name="value" or name='value'
        int i = 0;
        while (i < attrs.Length)
        {
            while (i < attrs.Length && char.IsWhiteSpace(attrs[i]))
                i++;
            if (i >= attrs.Length)
                break;

            if (!(char.IsLetter(attrs[i]) || attrs[i] == '_' || attrs[i] == ':'))
            {
                i++; // skip junk (e.g. trailing /)
                continue;
            }

            int ns = i;
            while (i < attrs.Length && (char.IsLetterOrDigit(attrs[i]) || attrs[i] is ':' or '_' or '-'))
                i++;
            string an = attrs.Substring(ns, i - ns);
            while (i < attrs.Length && char.IsWhiteSpace(attrs[i]))
                i++;
            if (i >= attrs.Length || attrs[i] != '=')
                continue;
            i++;
            while (i < attrs.Length && char.IsWhiteSpace(attrs[i]))
                i++;
            if (i >= attrs.Length)
                break;
            char q = attrs[i];
            if (q is not '"' and not '\'')
            {
                i++;
                continue;
            }
            i++;
            int vs = i;
            while (i < attrs.Length && attrs[i] != q)
                i++;
            string val = attrs.Substring(vs, Math.Max(0, i - vs));
            if (i < attrs.Length)
                i++;
            if (an.Equals(name, StringComparison.OrdinalIgnoreCase))
                return val;
        }
        return null;
    }
}
