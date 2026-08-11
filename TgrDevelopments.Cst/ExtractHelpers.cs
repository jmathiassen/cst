using System;
using System.Collections.Generic;

namespace TgrDevelopments.Cst;

/// <summary>Shared helpers for structural extractors.</summary>
public static class ExtractHelpers
{
    /// <summary>
    /// Scans source for name-level call sites, skipping strings and comments.
    /// CalleeName is the simple tail identifier before <c>(</c>.
    /// </summary>
    public static List<CallSite> ScanCalls(
        string text,
        LineMap map,
        Func<int, string?>? enclosingDeclAtOffset = null,
        bool allowNew = true)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(map);

        List<CallSite> calls = [];
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];

            // line comment
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                i += 2;
                while (i < text.Length && text[i] is not ('\n' or '\r'))
                    i++;
                continue;
            }

            // block comment
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                    i++;
                i = Math.Min(text.Length, i + 2);
                continue;
            }

            // hash line comment (python/shell/ruby)
            if (c == '#')
            {
                while (i < text.Length && text[i] is not ('\n' or '\r'))
                    i++;
                continue;
            }

            // strings
            if (c is '"' or '\'' or '`')
            {
                char q = c;
                i++;
                while (i < text.Length)
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        i += 2;
                        continue;
                    }
                    if (text[i] == q)
                    {
                        i++;
                        break;
                    }
                    // template ${ } skip nested roughly
                    if (q == '`' && text[i] == '$' && i + 1 < text.Length && text[i + 1] == '{')
                    {
                        i += 2;
                        int depth = 1;
                        while (i < text.Length && depth > 0)
                        {
                            if (text[i] == '{')
                                depth++;
                            else if (text[i] == '}')
                                depth--;
                            i++;
                        }
                        continue;
                    }
                    i++;
                }
                continue;
            }

            // new Name(
            if (allowNew && IsIdentStart(c) && MatchesKeywordAt(text, i, "new"))
            {
                int afterNew = i + 3;
                while (afterNew < text.Length && char.IsWhiteSpace(text[afterNew]))
                    afterNew++;
                if (afterNew < text.Length && IsIdentStart(text[afterNew]))
                {
                    int nameStart = afterNew;
                    int nameEnd = ScanIdent(text, nameStart);
                    // skip generics/type args best-effort
                    int p = nameEnd;
                    while (p < text.Length && char.IsWhiteSpace(text[p]))
                        p++;
                    if (p < text.Length && text[p] == '<')
                        p = SkipBalanced(text, p, '<', '>');
                    while (p < text.Length && char.IsWhiteSpace(text[p]))
                        p++;
                    if (p < text.Length && text[p] == '(')
                    {
                        string name = text.Substring(nameStart, nameEnd - nameStart);
                        if (!IsReservedCallName(name))
                        {
                            string? enclosing = enclosingDeclAtOffset?.Invoke(nameStart);
                            int line = map.GetLineNumber(nameStart);
                            calls.Add(new CallSite(
                                name,
                                new TextSpan(nameStart, nameEnd - nameStart),
                                enclosing,
                                line));
                        }
                        i = p + 1;
                        continue;
                    }
                }
            }

            // ident or dotted.tail(
            if (IsIdentStart(c))
            {
                int start = i;
                int end = ScanIdent(text, start);
                // optional .ident chain — keep last tail
                int tailStart = start;
                int tailEnd = end;
                int p = end;
                while (true)
                {
                    while (p < text.Length && char.IsWhiteSpace(text[p]))
                        p++;
                    if (p < text.Length && text[p] == '.')
                    {
                        p++;
                        while (p < text.Length && char.IsWhiteSpace(text[p]))
                            p++;
                        if (p < text.Length && IsIdentStart(text[p]))
                        {
                            tailStart = p;
                            tailEnd = ScanIdent(text, tailStart);
                            p = tailEnd;
                            continue;
                        }
                        break;
                    }
                    break;
                }

                while (p < text.Length && char.IsWhiteSpace(text[p]))
                    p++;
                // skip type args Type<T>(
                if (p < text.Length && text[p] == '<')
                {
                    int after = SkipBalanced(text, p, '<', '>');
                    if (after > p)
                    {
                        p = after;
                        while (p < text.Length && char.IsWhiteSpace(text[p]))
                            p++;
                    }
                }

                if (p < text.Length && text[p] == '(')
                {
                    string name = text.Substring(tailStart, tailEnd - tailStart);
                    // exclude control keywords and declarations
                    if (!IsReservedCallName(name)
                        && !IsDeclKeywordBefore(text, start))
                    {
                        string? enclosing = enclosingDeclAtOffset?.Invoke(tailStart);
                        int line = map.GetLineNumber(tailStart);
                        calls.Add(new CallSite(
                            name,
                            new TextSpan(tailStart, tailEnd - tailStart),
                            enclosing,
                            line));
                    }
                    i = p + 1;
                    continue;
                }

                i = end;
                continue;
            }

            i++;
        }

        return calls;
    }

    /// <summary>Builds DeclSymbol list from outline-like named nodes with line spans.</summary>

    /// <summary>
    /// Flattens an outline tree into declaration symbols (container = parent label).
    /// Prefer this over regex ladders when the pack already built a quality outline.
    /// </summary>
    public static List<DeclSymbol> DeclsFromOutline(
        IReadOnlyList<OutlineNode> outline,
        LineMap map,
        string? containerName = null)
    {
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentNullException.ThrowIfNull(map);
        List<DeclSymbol> decls = [];
        CollectOutlineDecls(outline, map, containerName, decls);
        return decls;
    }

    private static void CollectOutlineDecls(
        IReadOnlyList<OutlineNode> nodes,
        LineMap map,
        string? containerName,
        List<DeclSymbol> decls)
    {
        foreach (OutlineNode node in nodes)
        {
            decls.Add(Decl(node.Label, node.Kind, node.Span, map, containerName));
            if (node.Children.Count > 0)
                CollectOutlineDecls(node.Children, map, node.Label, decls);
        }
    }

    /// <summary>Builds enclosing-name lookup ranges from decls (innermost span wins).</summary>
    public static List<(int Start, int End, string Name)> DeclRanges(IReadOnlyList<DeclSymbol> decls)
    {
        ArgumentNullException.ThrowIfNull(decls);
        List<(int Start, int End, string Name)> ranges = [];
        foreach (DeclSymbol d in decls)
            ranges.Add((d.Span.Start, d.Span.End, d.Name));
        return ranges;
    }

    /// <summary>Innermost enclosing decl name for offset.</summary>
    public static string? EnclosingName(IReadOnlyList<(int Start, int End, string Name)> ranges, int offset)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        string? best = null;
        int bestSpan = int.MaxValue;
        for (int i = 0; i < ranges.Count; i++)
        {
            (int Start, int End, string Name) r = ranges[i];
            if (offset >= r.Start && offset < r.End)
            {
                int size = r.End - r.Start;
                if (size < bestSpan)
                {
                    bestSpan = size;
                    best = r.Name;
                }
            }
        }
        return best;
    }

        /// <summary>Builds a declaration symbol with 1-based line anchors from span.</summary>
    public static DeclSymbol Decl(
        string name,
        string kind,
        TextSpan span,
        LineMap map,
        string? containerName = null,
        TextSpan? nameSpan = null,
        string? signature = null)
    {
        int startLine = map.GetLineNumber(span.Start);
        int endLine = map.GetLineNumber(span.Length > 0 ? span.End - 1 : span.Start);
        return new DeclSymbol(
            name,
            kind,
            span,
            nameSpan,
            containerName,
            signature,
            startLine,
            endLine);
    }

    private static bool IsIdentStart(char c)
        => char.IsLetter(c) || c == '_' || c == '$';

    private static bool IsIdentPart(char c)
        => char.IsLetterOrDigit(c) || c == '_' || c == '$';

    private static int ScanIdent(string text, int start)
    {
        int i = start;
        if (i >= text.Length || !IsIdentStart(text[i]))
            return start;
        i++;
        while (i < text.Length && IsIdentPart(text[i]))
            i++;
        return i;
    }

    private static bool MatchesKeywordAt(string text, int i, string keyword)
    {
        if (i + keyword.Length > text.Length)
            return false;
        for (int k = 0; k < keyword.Length; k++)
        {
            if (text[i + k] != keyword[k])
                return false;
        }
        int after = i + keyword.Length;
        if (after < text.Length && IsIdentPart(text[after]))
            return false;
        if (i > 0 && IsIdentPart(text[i - 1]))
            return false;
        return true;
    }

    private static int SkipBalanced(string text, int openIdx, char open, char close)
    {
        if (openIdx >= text.Length || text[openIdx] != open)
            return openIdx;
        int depth = 0;
        for (int i = openIdx; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"' || c == '\'' || c == '`')
            {
                char q = c;
                i++;
                while (i < text.Length)
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        i += 2;
                        continue;
                    }
                    if (text[i] == q)
                        break;
                    i++;
                }
                continue;
            }
            if (c == open)
                depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0)
                    return i + 1;
            }
        }
        return text.Length;
    }

    private static bool IsReservedCallName(string name)
    {
        return name is "if" or "for" or "while" or "switch" or "catch" or "function"
            or "class" or "return" or "throw" or "new" or "typeof" or "instanceof"
            or "void" or "delete" or "await" or "yield" or "import" or "export"
            or "using" or "lock" or "fixed" or "checked" or "unchecked" or "sizeof"
            or "nameof" or "when" or "match" or "case" or "else" or "do" or "try"
            or "with" or "from" or "where" or "select" or "let" or "const" or "var";
    }

    private static bool IsDeclKeywordBefore(string text, int identStart)
    {
        // look back over whitespace for function/class/def etc.
        int i = identStart - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i]))
            i--;
        if (i < 0)
            return false;

        // skip async before function name is handled if keyword is function
        string[] keywords =
        [
            "function", "class", "def", "fn", "func", "proc", "sub", "method",
            "interface", "struct", "enum", "type", "namespace", "module",
            "trait", "impl", "constructor"
        ];

        foreach (string kw in keywords)
        {
            int start = i - kw.Length + 1;
            if (start < 0)
                continue;
            bool match = true;
            for (int k = 0; k < kw.Length; k++)
            {
                if (char.ToLowerInvariant(text[start + k]) != kw[k])
                {
                    match = false;
                    break;
                }
            }
            if (!match)
                continue;
            if (start > 0 && IsIdentPart(text[start - 1]))
                continue;
            return true;
        }

        return false;
    }
}
