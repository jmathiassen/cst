using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Env;

/// <summary>Dotenv language pack.</summary>
public sealed class EnvLanguagePack : ILanguagePack, IOutlineProvider
{
    private static readonly string[] Extensions = [".env"];
    private static readonly string[] NamePatterns = [".env",".env.*"];

    /// <inheritdoc />

    public string LanguageId => "env";
    /// <inheritdoc />

    public string DisplayName => "Dotenv";
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
        return name.Equals(".env", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".env", StringComparison.OrdinalIgnoreCase);

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

        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            string t = lines[i].Trim();
            if (t.Length == 0 || t.StartsWith('#')) continue;
            if (t.StartsWith("export ", StringComparison.Ordinal)) t = t.Substring(7).TrimStart();
            int eq = t.IndexOf('=');
            if (eq <= 0) {
                partialFlag = true;
                diagnostics.Add(new ParseDiagnostic("ENV001", "Malformed env line.", map.GetSpanForLines(i+1,i+1), DiagnosticSeverity.Warning));
                continue;
            }
            string key = t.Substring(0, eq).Trim();
            int ln = i + 1;
            children.Add(new SyntaxNode("entry", map.GetSpanForLines(ln, ln), key));
            outlines.Add(TextUtil.Outline("key", key, map, ln, ln, 1));
        }

        isPartial = partialFlag;

    }
}
