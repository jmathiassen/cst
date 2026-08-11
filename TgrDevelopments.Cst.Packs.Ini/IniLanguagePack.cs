using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Ini;

/// <summary>INI language pack.</summary>
public sealed class IniLanguagePack : ILanguagePack, IOutlineProvider
{
    private static readonly string[] Extensions = [".ini", ".cfg", ".conf", ".properties", ".editorconfig"];
    private static readonly string[] NamePatterns = Array.Empty<string>();

    /// <inheritdoc />
    public string LanguageId => "ini";

    /// <inheritdoc />
    public string DisplayName => "INI";

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
        return name.Equals(".gitconfig", StringComparison.OrdinalIgnoreCase)
            || name.Equals("setup.cfg", StringComparison.OrdinalIgnoreCase)
            || name.Equals("tox.ini", StringComparison.OrdinalIgnoreCase);

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
            return TextUtil.EmptyTree(LanguageId, effective, "document", diagnostics, DefaultQuality, isPartial);

        LineMap map = new(text);
        string[] lines = TextUtil.SplitLines(text);
        List<SyntaxNode> children = [];
        List<OutlineNode> outlines = [];
        ParseDocument(text, lines, map, children, outlines, diagnostics, ref isPartial, cancellationToken);

        return TextUtil.Tree(
            LanguageId, effective, "document", text, children, diagnostics, DefaultQuality, isPartial, outlines);
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

        int i = 0;
        OutlineNode? currentSection = null;
        List<OutlineNode> sectionChildren = [];
        string? sectionName = null;
        int sectionStart = 1;
        List<OutlineNode> topKeys = [];

        void FlushSection(int endLine)
        {
            if (sectionName is null) return;
            outlines.Add(TextUtil.Outline("section", sectionName, map, sectionStart, endLine, 1, sectionChildren));
            sectionChildren = [];
            sectionName = null;
        }

        while (i < lines.Length)
        {
            ct.ThrowIfCancellationRequested();
            string line = lines[i];
            int ln = i + 1;
            string t = line.Trim();
            if (t.Length == 0 || t.StartsWith('#') || t.StartsWith(';'))
            {
                i++;
                continue;
            }
            if (t.StartsWith('['))
            {
                if (!t.Contains(']'))
                {
                    partialFlag = true;
                    diagnostics.Add(new ParseDiagnostic("INI001", "Unclosed section header.", map.GetSpanForLines(ln, ln), DiagnosticSeverity.Error));
                    i++;
                    continue;
                }
                FlushSection(ln - 1 > 0 ? ln - 1 : ln);
                int end = t.IndexOf(']');
                sectionName = t.Substring(1, end - 1).Trim();
                sectionStart = ln;
                TextSpan span = map.GetSpanForLines(ln, ln);
                children.Add(new SyntaxNode("section", span, sectionName));
                i++;
                continue;
            }
            int eq = t.IndexOf('=');
            int colon = t.IndexOf(':');
            int sep = eq >= 0 ? eq : colon;
            if (sep > 0)
            {
                string key = t.Substring(0, sep).Trim();
                TextSpan span = map.GetSpanForLines(ln, ln);
                children.Add(new SyntaxNode("key", span, key));
                OutlineNode keyNode = TextUtil.Outline("key", key, map, ln, ln, sectionName is null ? 1 : 2);
                if (sectionName is null) topKeys.Add(keyNode);
                else sectionChildren.Add(keyNode);
            }
            i++;
        }
        if (sectionName is not null)
            FlushSection(lines.Length);
        else
            outlines.AddRange(topKeys);

        isPartial = partialFlag;

    }
}
