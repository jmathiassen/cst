using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Sln;

/// <summary>Visual Studio Solution language pack.</summary>
public sealed class SlnLanguagePack : ILanguagePack, IOutlineProvider
{
    private static readonly string[] Extensions = [".sln", ".slnx"];
    private static readonly string[] NamePatterns = Array.Empty<string>();

    /// <inheritdoc />
    public string LanguageId => "sln";

    /// <inheritdoc />
    public string DisplayName => "Visual Studio Solution";

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
            return TextUtil.EmptyTree(LanguageId, effective, "solution", diagnostics, DefaultQuality, isPartial);
        }

        LineMap map = new(text);
        bool xml = IsSlnx(source, text);
        string rootKind = xml ? "document" : "solution";
        List<SyntaxNode> children = [];
        List<OutlineNode> outlines = [];
        if (xml)
        {
            SlnxXmlParser parser = new(text, map, diagnostics, cancellationToken);
            parser.Parse(children, outlines, ref isPartial);
        }
        else
        {
            string[] lines = TextUtil.SplitLines(text);
            ParseDocument(text, lines, map, children, outlines, diagnostics, ref isPartial, cancellationToken);
        }
        return TextUtil.Tree(LanguageId, effective, rootKind, text, children, diagnostics, DefaultQuality, isPartial, outlines);
    }

    /// <summary>
    /// Detects the <c>.slnx</c> XML format: by file extension, or by content sniffing
    /// (XML declaration or <c>Solution</c> root) when no extension is available.
    /// </summary>
    private static bool IsSlnx(SourceText source, string text)
    {
        string? filePath = source.FilePath;
        if (filePath is not null)
            return filePath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

        int i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;
        if (i >= text.Length || text[i] != '<')
            return false;

        string head = text.Substring(i, Math.Min(200, text.Length - i));
        return head.Contains("<?xml", StringComparison.OrdinalIgnoreCase)
            || head.Contains("<Solution", StringComparison.OrdinalIgnoreCase);
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

        Regex proj = new(@"^Project\(""\{[^}]+\}""\)\s*=\s*""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*""\{([^}]+)\}""", RegexOptions.Compiled);
        bool inNested = false;
        bool inCfgs = false;
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            string t = lines[i].Trim();
            int ln = i + 1;
            if (t.StartsWith("Project(", StringComparison.Ordinal))
            {
                Match m = proj.Match(t);
                if (m.Success)
                {
                    string name = m.Groups[1].Value;
                    children.Add(new SyntaxNode("project", map.GetSpanForLines(ln, ln), name));
                    outlines.Add(TextUtil.Outline("project", name, map, ln, ln, 1));
                }
                continue;
            }
            if (t.StartsWith("GlobalSection(NestedProjects)", StringComparison.OrdinalIgnoreCase))
            { inNested = true; continue; }
            if (t.StartsWith("GlobalSection(ProjectConfigurationPlatforms)", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("GlobalSection(SolutionConfigurationPlatforms)", StringComparison.OrdinalIgnoreCase))
            { inCfgs = true; continue; }
            if (t.StartsWith("EndGlobalSection", StringComparison.OrdinalIgnoreCase))
            { inNested = false; inCfgs = false; continue; }
            if (inNested && t.Contains('='))
            {
                outlines.Add(TextUtil.Outline("folder", TextUtil.Truncate(t, 40), map, ln, ln, 2));
            }
            if (inCfgs && t.Contains('=') && outlines.Count < 50)
            {
                // optional config nodes - skip noise; only first few solution configs
            }
        }
        // also solution folders as projects with type folder - already covered
        if (outlines.Count == 0)
        {
            // still try Project lines without full match
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("Project("))
                {
                    Match m2 = Regex.Match(lines[i], @"""([^""]+)""\s*,");
                    if (m2.Success)
                        outlines.Add(TextUtil.Outline("project", m2.Groups[1].Value, map, i+1, i+1, 1));
                }
            }
        }

        isPartial = partialFlag;

    }
}
