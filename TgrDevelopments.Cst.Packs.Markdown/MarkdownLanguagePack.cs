using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Markdown;

/// <summary>Markdown language pack (ATX/setext headings + fenced code).</summary>
public sealed class MarkdownLanguagePack : ILanguagePack, IOutlineProvider
{
    private static readonly string[] Extensions = [".md", ".markdown", ".mdx"];

    /// <inheritdoc />
    public string LanguageId => "markdown";

    /// <inheritdoc />
    public string DisplayName => "Markdown";

    /// <inheritdoc />
    public IReadOnlyList<string> FileExtensions => Extensions;

    /// <inheritdoc />
    public IReadOnlyList<string> FileNamePatterns => Array.Empty<string>();

    /// <inheritdoc />
    public SyntaxQuality DefaultQuality => SyntaxQuality.Structural;

    /// <inheritdoc />
    public bool OwnsPath(string relativeOrFileName) => false;

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
            SyntaxNode emptyRoot = new("document", new TextSpan(0, 0));
            return new ConcreteSyntaxTree(
                LanguageId, effective, emptyRoot, diagnostics,
                SyntaxConfidence.Syntax, DefaultQuality, isPartial,
                cachedOutline: Array.Empty<OutlineNode>());
        }

        LineMap map = new(text);
        List<SyntaxNode> children = [];
        string[] lines = SplitLines(text);

        int i = 0;
        while (i < lines.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string line = lines[i];
            int lineNumber = i + 1;

            if (TryMatchAtx(line, out int level, out string title))
            {
                TextSpan span = map.GetSpanForLines(lineNumber, lineNumber);
                string trimmedLine = line.TrimStart();
                int afterHashes = level;
                if (afterHashes < trimmedLine.Length && (trimmedLine[afterHashes] == ' ' || trimmedLine[afterHashes] == '\t'))
                    afterHashes++;
                while (afterHashes < trimmedLine.Length
                       && (trimmedLine[afterHashes] == ' ' || trimmedLine[afterHashes] == '\t'))
                    afterHashes++;
                int nameOffsetInLine = LeadingSpaces(line) + afterHashes;
                TextSpan nameSpan = new(
                    map.GetLineStartOffset(lineNumber) + nameOffsetInLine,
                    Math.Max(0, title.Length));

                Dictionary<string, string> props = new() { ["level"] = level.ToString() };
                children.Add(new SyntaxNode("heading", span, title, nameSpan, properties: props));
                i++;
                continue;
            }

            if (i + 1 < lines.Length
                && !IsBlank(line)
                && TryMatchSetext(lines[i + 1], out int setextLevel))
            {
                string setextTitle = line.Trim();
                if (setextTitle.Length > 0)
                {
                    TextSpan span = map.GetSpanForLines(lineNumber, lineNumber + 1);
                    TextSpan nameSpan = new(
                        map.GetLineStartOffset(lineNumber) + LeadingSpaces(line),
                        setextTitle.Length);
                    Dictionary<string, string> props = new() { ["level"] = setextLevel.ToString() };
                    children.Add(new SyntaxNode(
                        "heading", span, setextTitle, nameSpan, properties: props));
                    i += 2;
                    continue;
                }
            }

            if (TryMatchFenceOpen(line, out char fenceChar, out int fenceLen, out string info))
            {
                int startLine = lineNumber;
                int endLine = startLine;
                bool closed = false;
                i++;
                while (i < lines.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (TryMatchFenceClose(lines[i], fenceChar, fenceLen))
                    {
                        endLine = i + 1;
                        closed = true;
                        i++;
                        break;
                    }

                    endLine = i + 1;
                    i++;
                }

                if (!closed)
                {
                    isPartial = true;
                    diagnostics.Add(new ParseDiagnostic(
                        "MD001",
                        "Unclosed fenced code block.",
                        map.GetSpanForLines(startLine, startLine),
                        DiagnosticSeverity.Warning));
                    endLine = lines.Length;
                }

                TextSpan fenceSpan = map.GetSpanForLines(startLine, endLine);
                Dictionary<string, string> props = new()
                {
                    ["info"] = info,
                    ["fence"] = fenceChar == '`' ? "`" : "~"
                };
                string? name = info.Length > 0 ? info : null;
                children.Add(new SyntaxNode("code_fence", fenceSpan, name, properties: props));
                continue;
            }

            if (!IsBlank(line))
            {
                int pStart = lineNumber;
                int pEnd = lineNumber;
                i++;
                while (i < lines.Length)
                {
                    string next = lines[i];
                    if (IsBlank(next)
                        || TryMatchAtx(next, out _, out _)
                        || TryMatchFenceOpen(next, out _, out _, out _)
                        || (i + 1 < lines.Length
                            && !IsBlank(next)
                            && TryMatchSetext(lines[i + 1], out _)))
                        break;
                    pEnd = i + 1;
                    i++;
                }

                children.Add(new SyntaxNode("paragraph", map.GetSpanForLines(pStart, pEnd)));
                continue;
            }

            i++;
        }

        SyntaxNode root = new("document", new TextSpan(0, text.Length), children: children);
        ConcreteSyntaxTree provisional = new(
            LanguageId, effective, root, diagnostics,
            SyntaxConfidence.Syntax, DefaultQuality, isPartial);
        IReadOnlyList<OutlineNode> outline = BuildOutlineFromTree(provisional, cancellationToken);
        return new ConcreteSyntaxTree(
            LanguageId, effective, root, diagnostics,
            SyntaxConfidence.Syntax, DefaultQuality, isPartial, outline);
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
        return BuildOutlineFromTree(tree, cancellationToken);
    }

    private static IReadOnlyList<OutlineNode> BuildOutlineFromTree(
        ConcreteSyntaxTree tree,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tree);
        cancellationToken.ThrowIfCancellationRequested();

        LineMap map = new(tree.Source.Text);
        int lastLine = map.LineCount;

        List<(SyntaxNode Node, int Level, int StartLine)> headings = [];
        List<(SyntaxNode Node, int StartLine, int EndLine)> fences = [];

        foreach (SyntaxNode child in tree.Root.Children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int startLine = map.GetLineNumber(child.Span.Start);
            int endOffset = child.Span.Length > 0 ? child.Span.End - 1 : child.Span.Start;
            int endLine = map.GetLineNumber(endOffset);

            if (child.Kind == "heading")
            {
                int level = 1;
                if (child.Properties is not null
                    && child.Properties.TryGetValue("level", out string? levelText)
                    && int.TryParse(levelText, out int parsed))
                    level = parsed;
                headings.Add((child, level, startLine));
            }
            else if (child.Kind == "code_fence")
            {
                fences.Add((child, startLine, endLine));
            }
        }

        if (headings.Count == 0 && fences.Count == 0)
            return Array.Empty<OutlineNode>();

        int[] sectionEnds = new int[headings.Count];
        for (int h = 0; h < headings.Count; h++)
        {
            int level = headings[h].Level;
            int end = lastLine;
            for (int n = h + 1; n < headings.Count; n++)
            {
                if (headings[n].Level <= level)
                {
                    end = headings[n].StartLine - 1;
                    break;
                }
            }

            if (end < headings[h].StartLine)
                end = headings[h].StartLine;
            sectionEnds[h] = end;
        }

        if (headings.Count == 0)
        {
            List<OutlineNode> onlyFences = [];
            foreach ((SyntaxNode Node, int StartLine, int EndLine) fence in fences)
            {
                onlyFences.Add(CreateFenceOutline(fence, map, 1));
            }

            return onlyFences;
        }

        OutlineNode BuildHeading(int index)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (SyntaxNode Node, int Level, int StartLine) h = headings[index];
            int sectionEnd = sectionEnds[index];
            List<OutlineNode> children = [];

            int next = index + 1;
            while (next < headings.Count
                   && headings[next].StartLine <= sectionEnd
                   && headings[next].Level > h.Level)
            {
                children.Add(BuildHeading(next));
                int childEnd = sectionEnds[next];
                int skip = next + 1;
                while (skip < headings.Count && headings[skip].StartLine <= childEnd)
                    skip++;
                next = skip;
            }

            foreach ((SyntaxNode Node, int StartLine, int EndLine) fence in fences)
            {
                if (fence.StartLine < h.StartLine || fence.StartLine > sectionEnd)
                    continue;

                bool inChild = false;
                foreach (OutlineNode child in children)
                {
                    if (child.Kind != "heading")
                        continue;
                    if (fence.StartLine >= child.LineSpan.Start.Line
                        && fence.StartLine <= child.LineSpan.End.Line)
                    {
                        inChild = true;
                        break;
                    }
                }

                if (inChild)
                    continue;

                children.Add(CreateFenceOutline(fence, map, h.Level + 1));
            }

            children.Sort(static (a, b) => a.LineSpan.Start.Line.CompareTo(b.LineSpan.Start.Line));

            string headingLabel = h.Node.Name ?? "";
            TextSpan span = map.GetSpanForLines(h.StartLine, sectionEnd);
            LinePositionSpan lineSpan = map.GetLineSpan(h.StartLine, sectionEnd);
            return new OutlineNode("heading", headingLabel, span, lineSpan, h.Level, children);
        }

        List<OutlineNode> roots = [];
        int i = 0;
        while (i < headings.Count)
        {
            roots.Add(BuildHeading(i));
            int end = sectionEnds[i];
            int skip = i + 1;
            while (skip < headings.Count && headings[skip].StartLine <= end)
                skip++;
            i = skip;
        }

        List<OutlineNode> leading = [];
        foreach ((SyntaxNode Node, int StartLine, int EndLine) fence in fences)
        {
            if (fence.StartLine >= headings[0].StartLine)
                break;
            leading.Add(CreateFenceOutline(fence, map, 1));
        }

        if (leading.Count == 0)
            return roots;

        leading.AddRange(roots);
        return leading;
    }

    private static OutlineNode CreateFenceOutline(
        (SyntaxNode Node, int StartLine, int EndLine) fence,
        LineMap map,
        int level)
    {
        string label = fence.Node.Name is { Length: > 0 } info ? info : "`code`";
        return new OutlineNode(
            "code_fence",
            label,
            fence.Node.Span,
            map.GetLineSpan(fence.StartLine, fence.EndLine),
            level);
    }

    private static string[] SplitLines(string text)
    {
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

    private static bool IsBlank(string line) => string.IsNullOrWhiteSpace(line);

    private static int LeadingSpaces(string line)
    {
        int n = 0;
        while (n < line.Length && line[n] == ' ')
            n++;
        return n;
    }

    private static bool TryMatchAtx(string line, out int level, out string title)
    {
        level = 0;
        title = "";
        int i = 0;
        while (i < line.Length && line[i] == ' ' && i < 3)
            i++;

        while (i < line.Length && line[i] == '#' && level < 6)
        {
            level++;
            i++;
        }

        if (level == 0)
            return false;
        if (i < line.Length && line[i] != ' ' && line[i] != '\t')
            return false;

        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            i++;

        title = line.Substring(i).TrimEnd();
        while (title.Length > 0 && title[^1] == '#')
        {
            title = title[..^1].TrimEnd();
        }

        return title.Length > 0;
    }

    private static bool TryMatchSetext(string underline, out int level)
    {
        level = 0;
        string t = underline.TrimEnd();
        int i = 0;
        while (i < t.Length && t[i] == ' ' && i < 3)
            i++;
        if (i >= t.Length)
            return false;

        char ch = t[i];
        if (ch != '=' && ch != '-')
            return false;

        int start = i;
        while (i < t.Length && t[i] == ch)
            i++;
        if (i - start < 1)
            return false;

        while (i < t.Length && t[i] == ' ')
            i++;
        if (i != t.Length)
            return false;

        level = ch == '=' ? 1 : 2;
        return true;
    }

    private static bool TryMatchFenceOpen(string line, out char fenceChar, out int fenceLen, out string info)
    {
        fenceChar = '\0';
        fenceLen = 0;
        info = "";
        int i = 0;
        while (i < line.Length && line[i] == ' ' && i < 3)
            i++;
        if (i >= line.Length)
            return false;

        char ch = line[i];
        if (ch != '`' && ch != '~')
            return false;

        int start = i;
        while (i < line.Length && line[i] == ch)
            i++;
        fenceLen = i - start;
        if (fenceLen < 3)
            return false;

        fenceChar = ch;
        info = line.Substring(i).Trim();
        if (ch == '`' && info.Contains('`', StringComparison.Ordinal))
            return false;
        return true;
    }

    private static bool TryMatchFenceClose(string line, char fenceChar, int fenceLen)
    {
        int i = 0;
        while (i < line.Length && line[i] == ' ' && i < 3)
            i++;
        int start = i;
        while (i < line.Length && line[i] == fenceChar)
            i++;
        if (i - start < fenceLen)
            return false;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            i++;
        return i == line.Length;
    }
}
