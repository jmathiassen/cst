using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Rst;

/// <summary>reStructuredText language pack.</summary>
public sealed class RstLanguagePack : ILanguagePack, IOutlineProvider
{
    private static readonly string[] Extensions = [".rst",".rest"];
    private static readonly string[] NamePatterns = Array.Empty<string>();

    /// <inheritdoc />

    public string LanguageId => "rst";
    /// <inheritdoc />

    public string DisplayName => "reStructuredText";
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

    private void ParseDocument(string text, string[] lines, LineMap map, List<SyntaxNode> children, List<OutlineNode> outlines, List<ParseDiagnostic> diagnostics, ref bool isPartial, CancellationToken ct)
    {
        bool partialFlag = isPartial;

        Dictionary<char, int> styleLevel = new();
        int nextLevel = 1;
        List<(int Level, string Title, int Start)> heads = [];

        for (int i = 0; i < lines.Length - 1; i++)
        {
            ct.ThrowIfCancellationRequested();
            string title = lines[i];
            string under = lines[i+1];
            if (title.Trim().Length == 0) continue;
            string u = under.TrimEnd();
            if (u.Length < 2) continue;
            char ch = u[0];
            if ("=-`~:.'\"^_*+#<>".IndexOf(ch) < 0) continue;
            bool all = true;
            for (int k = 0; k < u.Length; k++) if (u[k] != ch) { all = false; break; }
            if (!all) continue;
            if (u.Length < title.Trim().Length) {
                diagnostics.Add(new ParseDiagnostic("RST001", "Short underline.", map.GetSpanForLines(i+2,i+2), DiagnosticSeverity.Warning));
            }
            if (!styleLevel.ContainsKey(ch)) styleLevel[ch] = nextLevel++;
            int level = styleLevel[ch];
            heads.Add((level, title.Trim(), i + 1));
            children.Add(new SyntaxNode("heading", map.GetSpanForLines(i+1, i+2), title.Trim()));
            i++; // skip underline
        }
        for (int h = 0; h < heads.Count; h++)
        {
            int end = lines.Length;
            for (int n = h + 1; n < heads.Count; n++)
                if (heads[n].Level <= heads[h].Level) { end = heads[n].Start - 1; break; }
            if (end < heads[h].Start) end = heads[h].Start;
            outlines.Add(TextUtil.Outline("heading", heads[h].Title, map, heads[h].Start, end, heads[h].Level));
        }

        if (heads.Count == 0 && !string.IsNullOrWhiteSpace(text))
            partialFlag = true;

        isPartial = partialFlag;

    }
}
