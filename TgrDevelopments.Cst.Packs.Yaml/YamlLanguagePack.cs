using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Yaml;

/// <summary>YAML language pack.</summary>
public sealed class YamlLanguagePack : ILanguagePack, IOutlineProvider
{
    private static readonly string[] Extensions = [".yaml", ".yml"];
    private static readonly string[] NamePatterns = Array.Empty<string>();

    /// <inheritdoc />
    public string LanguageId => "yaml";

    /// <inheritdoc />
    public string DisplayName => "YAML";

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
    public ConcreteSyntaxTree Parse(
        SourceText source,
        ParseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        List<ParseDiagnostic> diagnostics = [];
        SourceText effective = ParseHelpers.ApplyMaxChars(
            source, options, out string text, diagnostics, out bool isPartial);

        if (ParseHelpers.IsNullOrWhiteSpace(text))
        {
            return TextUtil.EmptyTree(LanguageId, effective, "document", diagnostics, DefaultQuality, isPartial);
        }

        LineMap map = new(text);
        string[] lines = TextUtil.SplitLines(text);
        List<SyntaxNode> children = [];
        List<OutlineNode> outlines = [];
        ParseDocument(text, lines, map, children, outlines, diagnostics, ref isPartial, cancellationToken);
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

    private void ParseDocument(
        string text,
        string[] lines,
        LineMap map,
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        List<ParseDiagnostic> diagnostics,
        ref bool isPartial,
        CancellationToken ct)
    {
        bool partialFlag = isPartial;

        List<(int Indent, OutlineNode Node, List<OutlineNode> Children)> stack = [];
        List<OutlineNode> roots = [];

        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            string line = lines[i];
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#'))
                continue;
            int indent = 0;
            while (indent < line.Length && line[indent] == ' ') indent++;
            if (indent < line.Length && line[indent] == '\t') indent += 2;
            string t = line.Trim();
            if (t.StartsWith("- "))
            {
                string item = t.Substring(2).Trim();
                string label = item.Length > 0 ? TextUtil.Truncate(item, 40) : "-";
                int ln = i + 1;
                OutlineNode n = TextUtil.Outline("item", label, map, ln, ln, indent / 2 + 1);
                children.Add(new SyntaxNode("sequence_item", map.GetSpanForLines(ln, ln), label));
                while (stack.Count > 0 && stack[^1].Indent >= indent)
                    stack.RemoveAt(stack.Count - 1);
                if (stack.Count == 0) roots.Add(n);
                else stack[^1].Children.Add(n);
                continue;
            }
            int colon = t.IndexOf(':');
            if (colon > 0)
            {
                string key = t.Substring(0, colon).Trim();
                int ln = i + 1;
                // section end = next key at same or less indent
                int endLn = ln;
                for (int j = i + 1; j < lines.Length; j++)
                {
                    string L = lines[j];
                    if (L.Trim().Length == 0 || L.TrimStart().StartsWith('#')) { endLn = j + 1; continue; }
                    int ind = 0;
                    while (ind < L.Length && L[ind] == ' ') ind++;
                    string tt = L.Trim();
                    if (ind <= indent && tt.Contains(':') && !tt.StartsWith("- "))
                        break;
                    endLn = j + 1;
                }
                List<OutlineNode> ch = [];
                OutlineNode n = TextUtil.Outline("key", key, map, ln, endLn, indent / 2 + 1, ch);
                children.Add(new SyntaxNode("key", map.GetSpanForLines(ln, endLn), key));
                while (stack.Count > 0 && stack[^1].Indent >= indent)
                {
                    (int Indent, OutlineNode Node, List<OutlineNode> Children) finished = stack[^1];
                    stack.RemoveAt(stack.Count - 1);
                    // rebuild parent with children already attached via list ref
                }
                if (stack.Count == 0) roots.Add(n);
                else stack[^1].Children.Add(n);
                stack.Add((indent, n, ch));
            }
        }
        // rebuild tree properly — children lists are shared refs so OK
        outlines.AddRange(roots);

        if (!partialFlag)
        {
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string lastLine = lines[i].Trim();
                if (lastLine.Length == 0)
                {
                    if (i == 0) break;
                    continue;
                }
                if (lastLine.EndsWith(':') && !lastLine.StartsWith('#'))
                {
                    partialFlag = true;
                    diagnostics.Add(new ParseDiagnostic("YAML001", "Key with no value at end of input.", map.GetSpanForLines(i + 1, i + 1), DiagnosticSeverity.Error));
                }
                break;
            }
        }

        isPartial = partialFlag;

    }
}
