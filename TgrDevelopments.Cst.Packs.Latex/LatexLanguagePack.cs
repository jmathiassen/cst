using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Latex;

/// <summary>LaTeX language pack (hand-scanned sectioning commands → outline).</summary>
public sealed class LatexLanguagePack : ILanguagePack, IOutlineProvider
{
    private static readonly string[] Extensions = [".tex", ".latex", ".ltx"];
    private static readonly string[] NamePatterns = Array.Empty<string>();

    private static readonly string[] SectionCmds =
    [
        "subparagraph", "subsubsection", "subsection", "section",
        "paragraph", "chapter", "part",
    ];

    private static readonly Dictionary<string, int> Ranks = new(StringComparer.Ordinal)
    {
        ["part"] = 1,
        ["chapter"] = 2,
        ["section"] = 3,
        ["subsection"] = 4,
        ["subsubsection"] = 5,
        ["paragraph"] = 6,
        ["subparagraph"] = 7,
    };

    /// <inheritdoc />
    public string LanguageId => "latex";
    /// <inheritdoc />
    public string DisplayName => "LaTeX";
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
        List<SyntaxNode> children = ParseDoc(text, diagnostics, cancellationToken, out List<OutlineNode> outlines, ref isPartial);
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

    private static List<SyntaxNode> ParseDoc(
        string text,
        List<ParseDiagnostic> diagnostics,
        CancellationToken ct,
        out List<OutlineNode> outlines,
        ref bool isPartial)
    {
        _ = diagnostics;
        LineMap map = new(text);
        List<SyntaxNode> children = [];
        outlines = [];
        List<(int Level, string Title, int Start)> heads = [];

        LexCursor c = new(text, ct);
        while (!c.IsEof)
        {
            ct.ThrowIfCancellationRequested();
            // skip comments
            if (c.Peek() == '%' && (c.Position == 0 || text[c.Position - 1] != '\\'))
            {
                c.SkipUntilNewline();
                if (c.Peek() is '\r' or '\n') c.Advance();
                continue;
            }

            if (c.Peek() != '\\')
            {
                c.Advance();
                continue;
            }

            int cmdStart = c.Position;
            c.Advance(); // \
            string? cmd = null;
            foreach (string candidate in SectionCmds)
            {
                if (c.StartsWith(candidate))
                {
                    // ensure word boundary
                    char after = c.Peek(candidate.Length);
                    bool boundary = after is '*' or '[' or '{'
                        || (!char.IsLetterOrDigit(after) && after != '_');
                    if (boundary)
                    {
                        cmd = candidate;
                        c.Advance(candidate.Length);
                        break;
                    }
                }
            }
            if (cmd is null)
                continue;

            if (c.Peek() == '*')
                c.Advance();
            // optional [short]
            if (c.Peek() == '[')
            {
                c.Advance();
                while (!c.IsEof && c.Peek() != ']')
                    c.Advance();
                if (c.Peek() == ']') c.Advance();
            }
            if (c.Peek() != '{')
                continue;
            c.Advance();
            int titleStart = c.Position;
            int depth = 1;
            while (!c.IsEof && depth > 0)
            {
                char ch = c.Peek();
                if (ch == '{') depth++;
                else if (ch == '}') depth--;
                if (depth == 0) break;
                c.Advance();
            }
            string title = text.Substring(titleStart, Math.Max(0, c.Position - titleStart));
            if (c.Peek() == '}') c.Advance();

            int level = Ranks[cmd];
            int line = map.GetLineNumber(cmdStart);
            heads.Add((level, title, line));
            children.Add(new SyntaxNode("sectioning", map.GetSpanForLines(line, line), title));
        }

        if (heads.Count == 0 && !string.IsNullOrWhiteSpace(text))
            isPartial = true;

        for (int h = 0; h < heads.Count; h++)
        {
            int end = map.LineCount;
            for (int n = h + 1; n < heads.Count; n++)
            {
                if (heads[n].Level <= heads[h].Level)
                {
                    end = heads[n].Start - 1;
                    break;
                }
            }
            if (end < heads[h].Start)
                end = heads[h].Start;
            outlines.Add(TextUtil.Outline("section", heads[h].Title, map, heads[h].Start, end, heads[h].Level));
        }

        return children;
    }
}
