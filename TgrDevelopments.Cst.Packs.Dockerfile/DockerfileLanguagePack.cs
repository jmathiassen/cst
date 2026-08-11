using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Dockerfile;

/// <summary>Dockerfile language pack.</summary>
public sealed class DockerfileLanguagePack : ILanguagePack, IOutlineProvider
{
    private static readonly string[] Extensions = [".dockerfile"];
    private static readonly string[] NamePatterns = ["Dockerfile","Dockerfile.*"];

    /// <inheritdoc />

    public string LanguageId => "dockerfile";
    /// <inheritdoc />

    public string DisplayName => "Dockerfile";
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
        return name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".Dockerfile", StringComparison.OrdinalIgnoreCase);

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
            return TextUtil.EmptyTree(LanguageId, effective, "dockerfile", diagnostics, DefaultQuality, isPartial);
        }
        LineMap map = new(text);
        string[] lines = TextUtil.SplitLines(text);
        List<SyntaxNode> children = [];
        List<OutlineNode> outlines = [];
        ParseDocument(text, lines, map, children, outlines, diagnostics, ref isPartial, cancellationToken);
        return TextUtil.Tree(LanguageId, effective, "dockerfile", text, children, diagnostics, DefaultQuality, isPartial, outlines);
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

        string stageLabel = "";
        int stageStart = 1;
        List<OutlineNode> stageChildren = [];

        void Flush(int end)
        {
            if (stageLabel.Length == 0) return;
            outlines.Add(TextUtil.Outline("stage", stageLabel, map, stageStart, end, 1, stageChildren));
            stageChildren = []; stageLabel = "";
        }
        int i = 0;
        while (i < lines.Length)
        {
            ct.ThrowIfCancellationRequested();
            string t = lines[i].Trim();
            if (t.Length == 0 || t.StartsWith('#')) { i++; continue; }
            int startLn = i + 1;
            while (t.EndsWith("\\") && i + 1 < lines.Length) { i++; t = t.TrimEnd('\\').TrimEnd() + " " + lines[i].Trim(); }
            int endLn = i + 1;
            string[] parts = t.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) { i++; continue; }
            string kw = parts[0].ToUpperInvariant();
            string rest = parts.Length > 1 ? parts[1] : "";
            if (kw == "FROM")
            {
                Flush(startLn - 1 > 0 ? startLn - 1 : startLn);
                stageLabel = TextUtil.Truncate(rest.Length > 0 ? rest : "stage", 60);
                stageStart = startLn;
                children.Add(new SyntaxNode("instruction", map.GetSpanForLines(startLn, endLn), "FROM"));
            }
            else
            {
                string label = TextUtil.Truncate(rest.Length > 0 ? kw + " " + rest : kw, 40);
                OutlineNode n = TextUtil.Outline("instruction", label, map, startLn, endLn, stageLabel.Length > 0 ? 2 : 1);
                if (stageLabel.Length > 0) stageChildren.Add(n); else outlines.Add(n);
                children.Add(new SyntaxNode("instruction", map.GetSpanForLines(startLn, endLn), kw));
            }
            i++;
        }
        Flush(lines.Length);

        if (outlines.Count == 0 && !string.IsNullOrWhiteSpace(text))
            partialFlag = true;
        isPartial = partialFlag;

    }
}
