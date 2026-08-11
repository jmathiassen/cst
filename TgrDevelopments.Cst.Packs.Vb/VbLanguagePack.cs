using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Vb;

/// <summary>Visual Basic language pack (line scanner, no regex ladders).</summary>
public sealed class VbLanguagePack : ILanguagePack, IOutlineProvider
{
    private static readonly string[] Extensions = [".vb"];
    private static readonly string[] NamePatterns = Array.Empty<string>();

    /// <inheritdoc />
    public string LanguageId => "vb";
    /// <inheritdoc />
    public string DisplayName => "Visual Basic";
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
            return TextUtil.EmptyTree(LanguageId, effective, "compilation_unit", diagnostics, DefaultQuality, isPartial);
        List<SyntaxNode> children = [];
        List<OutlineNode> outlines = [];
        ParseDocument(text, children, outlines, ref isPartial, cancellationToken);
        return TextUtil.Tree(LanguageId, effective, "compilation_unit", text, children, diagnostics, DefaultQuality, isPartial, outlines);
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
        ref bool isPartial,
        CancellationToken ct)
    {
        LineMap map = new(text);
        string[] lines = TextUtil.SplitLines(text);
        bool partialFlag = isPartial;
        int i = 0;
        while (i < lines.Length)
        {
            ct.ThrowIfCancellationRequested();
            string t = lines[i].Trim();
            int ln = i + 1;
            if (t.StartsWith('\'') || t.Length == 0)
            {
                i++;
                continue;
            }

            if (TryMatchKeyword(t, "Namespace", out string nsName))
            {
                int end = BlockScanners.FindEndKeyword(lines, i, ["end namespace"], BlockScanners.DefaultOpenPrefixes, ref partialFlag);
                List<OutlineNode> ch = ScanTypes(lines, map, i + 1, end, ref partialFlag, ct);
                children.Add(new SyntaxNode("namespace_block", map.GetSpanForLines(ln, end), nsName));
                outlines.Add(TextUtil.Outline("namespace", nsName, map, ln, end, 1, ch));
                i = end;
                continue;
            }

            if (TryMatchType(t, out string tk, out string tn))
            {
                int te = BlockScanners.FindEndKeyword(lines, i, ["end " + tk], BlockScanners.DefaultOpenPrefixes, ref partialFlag);
                List<OutlineNode> mch = ScanMembers(lines, map, i + 1, te, ref partialFlag, ct);
                string ok = tk == "structure" ? "structure" : tk;
                children.Add(new SyntaxNode(ok + "_block", map.GetSpanForLines(ln, te), tn));
                outlines.Add(TextUtil.Outline(ok, tn, map, ln, te, 1, mch));
                i = te;
                continue;
            }

            i++;
        }

        if (outlines.Count > 0)
        {
            List<OutlineNode> nested = OutlineNesting.NestByLineContainment(outlines);
            outlines.Clear();
            outlines.AddRange(nested);
        }

        isPartial = partialFlag;
    }

    private static List<OutlineNode> ScanTypes(
        string[] lines, LineMap map, int from, int to, ref bool partial, CancellationToken ct)
    {
        List<OutlineNode> ch = [];
        int j = from - 1;
        while (j < to && j < lines.Length)
        {
            ct.ThrowIfCancellationRequested();
            string tt = lines[j].Trim();
            if (TryMatchType(tt, out string tk, out string tn))
            {
                int te = BlockScanners.FindEndKeyword(lines, j, ["end " + tk], BlockScanners.DefaultOpenPrefixes, ref partial);
                List<OutlineNode> mch = ScanMembers(lines, map, j + 1, te, ref partial, ct);
                string ok = tk == "structure" ? "structure" : tk;
                ch.Add(TextUtil.Outline(ok, tn, map, j + 1, te, 2, mch));
                j = te;
                continue;
            }
            j++;
        }
        return ch;
    }

    private static List<OutlineNode> ScanMembers(
        string[] lines, LineMap map, int from, int to, ref bool partial, CancellationToken ct)
    {
        List<OutlineNode> mch = [];
        int k = from - 1;
        while (k < to && k < lines.Length)
        {
            ct.ThrowIfCancellationRequested();
            string line = lines[k].Trim();
            if (TryMatchMember(line, out string mk, out string mn))
            {
                string[] ends = mk switch
                {
                    "function" => ["end function"],
                    "property" => ["end property"],
                    _ => ["end sub"],
                };
                int me = BlockScanners.FindEndKeyword(lines, k, ends, BlockScanners.DefaultOpenPrefixes, ref partial);
                string outlineKind = mk == "sub" && mn.Equals("New", StringComparison.OrdinalIgnoreCase)
                    ? "constructor"
                    : mk;
                mch.Add(TextUtil.Outline(outlineKind, mn, map, k + 1, me, 3));
                k = me;
                continue;
            }
            k++;
        }
        return mch;
    }

    private static bool TryMatchKeyword(string t, string keyword, out string name)
    {
        name = "*";
        if (!t.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
            return false;
        if (t.Length > keyword.Length && char.IsLetterOrDigit(t[keyword.Length]))
            return false;
        string rest = t.Substring(keyword.Length).TrimStart();
        name = ReadIdent(rest);
        return name.Length > 0;
    }

    private static bool TryMatchType(string t, out string kind, out string name)
    {
        kind = "";
        name = "*";
        string s = SkipMods(t);
        string[] kinds = ["Class", "Structure", "Interface", "Module", "Enum"];
        foreach (string k in kinds)
        {
            if (s.StartsWith(k, StringComparison.OrdinalIgnoreCase)
                && (s.Length == k.Length || !char.IsLetterOrDigit(s[k.Length])))
            {
                kind = k.ToLowerInvariant();
                name = ReadIdent(s.Substring(k.Length).TrimStart());
                return name.Length > 0;
            }
        }
        return false;
    }

    private static bool TryMatchMember(string t, out string kind, out string name)
    {
        kind = "";
        name = "*";
        string s = SkipMods(t);
        string[] kinds = ["Sub", "Function", "Property"];
        foreach (string k in kinds)
        {
            if (s.StartsWith(k, StringComparison.OrdinalIgnoreCase)
                && (s.Length == k.Length || !char.IsLetterOrDigit(s[k.Length])))
            {
                kind = k.ToLowerInvariant();
                name = ReadIdent(s.Substring(k.Length).TrimStart());
                if (name.Length == 0 && kind == "sub")
                    name = "New";
                return name.Length > 0;
            }
        }
        return false;
    }

    private static string SkipMods(string t)
    {
        string[] mods =
        [
            "Public", "Private", "Protected", "Friend", "Partial", "Shared",
            "Overrides", "Overridable", "MustOverride", "NotOverridable",
            "Shadows", "Overloads", "ReadOnly", "WriteOnly", "Default",
        ];
        string s = t;
        bool progressed = true;
        while (progressed)
        {
            progressed = false;
            foreach (string m in mods)
            {
                if (s.StartsWith(m, StringComparison.OrdinalIgnoreCase)
                    && (s.Length == m.Length || !char.IsLetterOrDigit(s[m.Length])))
                {
                    s = s.Substring(m.Length).TrimStart();
                    progressed = true;
                    break;
                }
            }
        }
        return s;
    }

    private static string ReadIdent(string s)
    {
        if (s.Length == 0) return "";
        int i = 0;
        if (s.StartsWith("New", StringComparison.OrdinalIgnoreCase)
            && (s.Length == 3 || !char.IsLetterOrDigit(s[3])))
            return "New";
        if (!(char.IsLetter(s[0]) || s[0] == '_'))
            return "";
        i = 1;
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '.'))
            i++;
        // take first segment only for simple names; allow dotted for namespace
        string full = s.Substring(0, i);
        return full.TrimEnd('.');
    }
}
