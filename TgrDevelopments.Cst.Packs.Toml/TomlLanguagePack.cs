using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Toml;

/// <summary>TOML language pack.</summary>
public sealed class TomlLanguagePack : ILanguagePack, IOutlineProvider
{
    private static readonly string[] Extensions = [".toml"];
    private static readonly string[] NamePatterns = Array.Empty<string>();

    /// <inheritdoc />
    public string LanguageId => "toml";

    /// <inheritdoc />
    public string DisplayName => "TOML";

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

        string name = Path.GetFileName(relativeOrFileName);
        return name.Equals("Pipfile", StringComparison.OrdinalIgnoreCase);

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

        string tableName = "";
        int tableStart = 1;
        List<OutlineNode> tableChildren = [];
        List<OutlineNode> top = [];

        void Flush(int endLn)
        {
            if (tableName.Length == 0) return;
            outlines.Add(TextUtil.Outline("table", tableName, map, tableStart, endLn, 1, tableChildren));
            tableChildren = [];
            tableName = "";
        }

        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            string t = lines[i].Trim();
            int ln = i + 1;
            if (t.Length == 0 || t.StartsWith('#')) continue;
            if (t.StartsWith('[') && t.EndsWith(']'))
            {
                Flush(ln - 1 > 0 ? ln - 1 : ln);
                tableName = t.Trim('[', ']');
                bool isArray = tableName.StartsWith('[');
                tableName = tableName.Trim('[', ']');
                tableStart = ln;
                children.Add(new SyntaxNode(isArray ? "array_table" : "table", map.GetSpanForLines(ln, ln), tableName));
                continue;
            }
            int eq = t.IndexOf('=');
            if (eq > 0)
            {
                string key = t.Substring(0, eq).Trim();
                OutlineNode kn = TextUtil.Outline("key", key, map, ln, ln, tableName.Length > 0 ? 2 : 1);
                children.Add(new SyntaxNode("key", map.GetSpanForLines(ln, ln), key));
                if (tableName.Length > 0) tableChildren.Add(kn);
                else top.Add(kn);
            }
        }
        Flush(lines.Length);
        if (top.Count > 0)
            outlines.InsertRange(0, top);

        if (!partialFlag)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string check = lines[i].Trim();
                if (check.StartsWith('[') && !check.EndsWith(']'))
                {
                    partialFlag = true;
                    diagnostics.Add(new ParseDiagnostic("TOML001", "Unclosed table header.", map.GetSpanForLines(i + 1, i + 1), DiagnosticSeverity.Error));
                    break;
                }
            }
        }

        isPartial = partialFlag;

    }
}
