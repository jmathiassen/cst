using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.M4;

/// <summary>
/// Recursive-descent structure parser for m4 / Autoconf:
/// definitions, includes, high-value and top-level calls, nested calls in args.
/// </summary>
internal sealed class M4Parser
{
    public const int M4MaxDepth = 64;
    public const int M4OutlineMaxNodes = 500;
    public const int M4FirstArgMaxChars = 80;
    public const int M4MaxArgumentsRecorded = 32;
    /// <summary>Skip nested re-lex when the arg body has no <c>(</c> (no call form).</summary>
    public const int M4NestedScanMinChars = 2;

    private static readonly HashSet<string> HighValueNames = new(StringComparer.Ordinal)
    {
        "AC_INIT",
        "AC_CONFIG_SRCDIR",
        "AC_CONFIG_HEADERS",
        "AC_CONFIG_FILES",
        "AC_CONFIG_COMMANDS",
        "AC_CONFIG_COMMANDS_PRE",
        "AC_CONFIG_COMMANDS_POST",
        "AC_CONFIG_MACRO_DIRS",
        "AC_CONFIG_MACRO_DIR",
        "AC_SUBST",
        "AC_REQUIRE",
        "AC_PROG_CC",
        "AC_PROG_CXX",
        "AC_PROG_LIBTOOL",
        "AM_INIT_AUTOMAKE",
        "LT_INIT",
        "m4_include",
        "m4_sinclude",
        "include",
        "sinclude",
    };

    private static readonly HashSet<string> DefunNames = new(StringComparer.Ordinal)
    {
        "AC_DEFUN",
        "AU_DEFUN",
        "m4_defun",
        "m4_define",
        "define",
    };

    private static readonly HashSet<string> IncludeNames = new(StringComparer.Ordinal)
    {
        "m4_include",
        "m4_sinclude",
        "include",
        "sinclude",
    };

    private static readonly HashSet<string> ConfigCommandsNames = new(StringComparer.Ordinal)
    {
        "AC_CONFIG_COMMANDS",
        "AC_CONFIG_COMMANDS_PRE",
        "AC_CONFIG_COMMANDS_POST",
    };

    private readonly string _text;
    private readonly LineMap _map;
    private readonly TokenReader _r;
    private readonly List<ParseDiagnostic> _diagnostics;
    private readonly CancellationToken _ct;
    private bool _partial;
    private int _outlineCount;
    private bool _outlineCapReported;
    private bool _depthCapReported;

    public M4Parser(string text, IReadOnlyList<Token> tokens, List<ParseDiagnostic> diagnostics, CancellationToken ct)
        : this(text, tokens, diagnostics, ct, map: null)
    {
    }

    private M4Parser(
        string text,
        IReadOnlyList<Token> tokens,
        List<ParseDiagnostic> diagnostics,
        CancellationToken ct,
        LineMap? map)
    {
        _text = text;
        _map = map ?? new LineMap(text);
        _r = new TokenReader(tokens, ct);
        _diagnostics = diagnostics;
        _ct = ct;
    }

    public bool IsPartial => _partial;

    public List<SyntaxNode> Parse(out List<OutlineNode> outlines)
    {
        outlines = [];
        List<SyntaxNode> children = [];
        ParseSequence(children, outlines, level: 1, depth: 0, topLevel: true);
        return children;
    }

    private void ParseSequence(
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        int level,
        int depth,
        bool topLevel)
    {
        // Bare ')' (e.g. shell `case pat)`) is junk at sequence scope, not a frame closer.
        // Argument lists stop on ')' inside CollectArgumentTokens / TryParseInvocation only.
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();

            if (_r.Check((int)M4TokenKind.Identifier))
            {
                if (TryParseInvocation(children, outlines, level, depth, topLevel))
                    continue;
            }

            _r.Advance();
        }
    }

    private bool TryParseInvocation(
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        int level,
        int depth,
        bool topLevel)
    {
        if (!_r.Check((int)M4TokenKind.Identifier))
            return false;

        // Require IDENT ( without consuming IDENT when no call
        if ((M4TokenKind)_r.Peek(1).Kind != M4TokenKind.LParen)
            return false;

        Token nameTok = _r.Current;
        string callee = nameTok.Text ?? "";
        int start = nameTok.Start;
        TextSpan nameSpan = nameTok.Span;
        _r.Advance();

        if (depth >= M4MaxDepth)
        {
            ReportDepthCap(nameTok.Span);
            if (!_r.TrySkipBalanced((int)M4TokenKind.LParen, (int)M4TokenKind.RParen))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic(
                    "M4001",
                    "Unclosed macro argument list.",
                    nameTok.Span,
                    DiagnosticSeverity.Error));
            }
            return true;
        }

        Token openParen = _r.Current;
        _r.Advance();

        List<SyntaxNode> argNodes = [];
        List<OutlineNode> nestedOutlines = [];
        int argCount = 0;
        string? firstArgRaw = null;
        TextSpan? defNameSpan = null;
        string? defName = null;
        string? includePath = null;
        bool closed = false;
        int end = openParen.End;

        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();

            if (_r.Check((int)M4TokenKind.RParen))
            {
                end = _r.Current.End;
                _r.Advance();
                closed = true;
                break;
            }

            if (_r.Check((int)M4TokenKind.Comma))
            {
                _r.Advance();
                continue;
            }

            int argStart = _r.Current.Start;
            CollectArgumentTokens(out int argEnd, out bool hitTerminator);

            TextSpan argSpan = new(argStart, Math.Max(0, argEnd - argStart));

            if (argCount == 0)
            {
                firstArgRaw = SliceTrimmed(argSpan);
                if (DefunNames.Contains(callee))
                    ExtractQuotedName(argSpan, out defName, out defNameSpan);
                if (IncludeNames.Contains(callee))
                    includePath = StripOuterQuotes(firstArgRaw ?? "");
            }

            List<SyntaxNode> argNested = [];
            List<OutlineNode> argOutlines = [];
            // Nested call scan on argument source (covers opaque [quoted] bodies)
            ScanNestedInSpan(argSpan, argNested, argOutlines, level + 1, depth + 1);

            if (argCount < M4MaxArgumentsRecorded)
            {
                IReadOnlyList<SyntaxNode>? argKids = argNested.Count > 0 ? argNested : null;
                argNodes.Add(new SyntaxNode("argument", argSpan, children: argKids));
            }

            if (argOutlines.Count > 0)
                nestedOutlines.AddRange(argOutlines);

            argCount++;
            end = Math.Max(end, argEnd);

            if (!hitTerminator && _r.IsEof)
                break;

            if (_r.Check((int)M4TokenKind.Comma))
            {
                _r.Advance();
                continue;
            }

            if (_r.Check((int)M4TokenKind.RParen))
            {
                end = _r.Current.End;
                _r.Advance();
                closed = true;
                break;
            }
        }

        if (!closed)
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic(
                "M4001",
                "Unclosed macro argument list.",
                openParen.Span,
                DiagnosticSeverity.Error));
            end = _text.Length;
        }

        TextSpan span = new(start, Math.Max(0, end - start));
        Dictionary<string, string> props = new(StringComparer.Ordinal)
        {
            ["callee"] = callee,
            ["arg_count"] = argCount.ToString(),
        };
        if (firstArgRaw is not null)
        {
            string flat = firstArgRaw.Replace('\n', ' ').Replace('\r', ' ').Trim();
            props["first_arg"] = TextUtil.Truncate(flat, M4FirstArgMaxChars);
        }

        bool isDefun = DefunNames.Contains(callee);
        bool isInclude = IncludeNames.Contains(callee);
        bool isHighValue = HighValueNames.Contains(callee);
        bool isConfigCommands = ConfigCommandsNames.Contains(callee);

        if (isHighValue)
            props["high_value"] = "true";

        string role = isDefun
            ? (callee is "m4_define" or "define" ? "define" : "defun")
            : isInclude
                ? "include"
                : isConfigCommands
                    ? "config_commands"
                    : "other";
        props["role"] = role;

        string treeKind;
        string? nodeName;
        TextSpan? nodeNameSpan;
        string outlineKind;
        string outlineLabel;

        if (isDefun)
        {
            treeKind = "macro_definition";
            if (defName is null || defName.Length == 0)
            {
                _diagnostics.Add(new ParseDiagnostic(
                    "M4002",
                    "Macro definition missing usable name argument.",
                    nameSpan,
                    DiagnosticSeverity.Warning));
                nodeName = callee;
                nodeNameSpan = nameSpan;
                outlineLabel = callee;
            }
            else
            {
                nodeName = defName;
                nodeNameSpan = defNameSpan ?? nameSpan;
                outlineLabel = defName;
            }
            outlineKind = "macro";
        }
        else if (isInclude)
        {
            treeKind = "include";
            nodeName = includePath is { Length: > 0 } ? includePath : callee;
            nodeNameSpan = nameSpan;
            outlineKind = "include";
            outlineLabel = includePath is { Length: > 0 }
                ? TextUtil.Truncate(includePath, 60)
                : callee;
        }
        else
        {
            treeKind = "macro_call";
            nodeName = callee;
            nodeNameSpan = nameSpan;
            outlineKind = "call";
            outlineLabel = callee;
        }

        children.Add(new SyntaxNode(treeKind, span, nodeName, nodeNameSpan, argNodes, props));

        bool outlineThis = isDefun || isInclude || isHighValue || topLevel;
        if (outlineThis)
        {
            OutlineNode? outline = TryMakeOutline(outlineKind, outlineLabel, span, level, nestedOutlines);
            if (outline is not null)
                outlines.Add(outline);
        }
        else if (nestedOutlines.Count > 0)
        {
            outlines.AddRange(nestedOutlines);
        }

        return true;
    }

    /// <summary>
    /// Consume tokens of one argument until comma or <c>)</c> at arg-list depth 0.
    /// Nested <c>(…)</c> inside the arg raise depth.
    /// </summary>
    private void CollectArgumentTokens(out int argEnd, out bool hitTerminator)
    {
        hitTerminator = false;
        argEnd = _r.Current.IsEof ? _text.Length : _r.Current.Start;
        int parenDepth = 0;

        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();
            M4TokenKind k = (M4TokenKind)_r.Current.Kind;

            if (k == M4TokenKind.Comma && parenDepth == 0)
            {
                hitTerminator = true;
                argEnd = _r.Current.Start;
                return;
            }

            if (k == M4TokenKind.RParen && parenDepth == 0)
            {
                hitTerminator = true;
                argEnd = _r.Current.Start;
                return;
            }

            if (k == M4TokenKind.LParen)
                parenDepth++;
            else if (k == M4TokenKind.RParen)
                parenDepth--;

            argEnd = _r.Current.End;
            _r.Advance();
        }

        argEnd = _text.Length;
    }

    /// <summary>
    /// Nested call scan over an argument body. Lexes a window of the original
    /// source (no substring / no token offset copy) and reuses the parent LineMap.
    /// </summary>
    private void ScanNestedInSpan(
        TextSpan span,
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        int level,
        int depth)
    {
        if (span.Length < M4NestedScanMinChars || depth >= M4MaxDepth)
        {
            if (depth >= M4MaxDepth)
                ReportDepthCap(span);
            return;
        }

        if (!TryGetNestedScanWindow(span, out int scanStart, out int scanLength))
            return;

        // No '(' => no IDENT( call form; skip re-lex entirely
        if (!WindowContainsChar(scanStart, scanLength, '('))
            return;

        List<ParseDiagnostic> nestedDiags = [];
        M4Lexer lexer = new(_text, scanStart, scanLength, nestedDiags, _ct);
        IReadOnlyList<Token> tokens = lexer.Tokenize();
        if (lexer.IsPartial)
            _partial = true;

        if (tokens.Count <= 1)
            return;

        M4Parser nested = new(_text, tokens, _diagnostics, _ct, _map)
        {
            _outlineCount = _outlineCount,
            _outlineCapReported = _outlineCapReported,
            _depthCapReported = _depthCapReported,
            _partial = _partial,
        };
        nested.ParseSequence(children, outlines, level, depth, topLevel: false);
        _outlineCount = nested._outlineCount;
        _outlineCapReported = nested._outlineCapReported;
        _depthCapReported = nested._depthCapReported;
        if (nested._partial)
            _partial = true;
    }

    private bool TryGetNestedScanWindow(TextSpan span, out int scanStart, out int scanLength)
    {
        scanStart = span.Start;
        scanLength = span.Length;
        if (span.Start < 0 || span.Length <= 0 || span.End > _text.Length)
            return false;

        // Trim whitespace without allocating
        int start = span.Start;
        int end = span.End;
        while (start < end && char.IsWhiteSpace(_text[start]))
            start++;
        while (end > start && char.IsWhiteSpace(_text[end - 1]))
            end--;

        // Strip one outer [ ] layer so nested IDENT( is visible as structure
        if (end - start >= 2 && _text[start] == '[' && _text[end - 1] == ']')
        {
            start++;
            end--;
            while (start < end && char.IsWhiteSpace(_text[start]))
                start++;
            while (end > start && char.IsWhiteSpace(_text[end - 1]))
                end--;
        }

        if (end <= start)
            return false;

        scanStart = start;
        scanLength = end - start;
        return true;
    }

    private bool WindowContainsChar(int start, int length, char ch)
    {
        int end = start + length;
        if (end > _text.Length)
            end = _text.Length;
        for (int i = start; i < end; i++)
        {
            if (_text[i] == ch)
                return true;
        }
        return false;
    }

    private OutlineNode? TryMakeOutline(
        string kind,
        string label,
        TextSpan span,
        int level,
        List<OutlineNode> children)
    {
        if (_outlineCount >= M4OutlineMaxNodes)
        {
            if (!_outlineCapReported)
            {
                _outlineCapReported = true;
                _diagnostics.Add(new ParseDiagnostic(
                    "M4006",
                    "Outline node cap reached.",
                    span,
                    DiagnosticSeverity.Info));
            }
            return null;
        }

        _outlineCount++;
        LinePositionSpan lp = _map.GetLinePositionSpan(span);
        LinePositionSpan nav = new(
            new LinePosition(lp.Start.Line, 1),
            new LinePosition(lp.End.Line, 1));
        IReadOnlyList<OutlineNode>? kids = children.Count > 0 ? children : null;
        return new OutlineNode(kind, label, span, nav, level, kids);
    }

    private void ReportDepthCap(TextSpan span)
    {
        _partial = true;
        if (_depthCapReported)
            return;
        _depthCapReported = true;
        _diagnostics.Add(new ParseDiagnostic(
            "M4005",
            "Max nesting depth exceeded.",
            span,
            DiagnosticSeverity.Warning));
    }

    private string Slice(TextSpan span)
    {
        if (span.Start < 0 || span.Length < 0 || span.End > _text.Length)
            return string.Empty;
        return _text.Substring(span.Start, span.Length);
    }

    private string? SliceTrimmed(TextSpan span)
    {
        if (span.Start < 0 || span.Length <= 0 || span.End > _text.Length)
            return null;
        int start = span.Start;
        int end = span.End;
        while (start < end && char.IsWhiteSpace(_text[start]))
            start++;
        while (end > start && char.IsWhiteSpace(_text[end - 1]))
            end--;
        if (end <= start)
            return null;
        return _text.Substring(start, end - start);
    }

    private void ExtractQuotedName(TextSpan argSpan, out string? name, out TextSpan? nameSpan)
    {
        name = null;
        nameSpan = null;
        if (argSpan.Start < 0 || argSpan.Length <= 0 || argSpan.End > _text.Length)
            return;

        int start = argSpan.Start;
        int end = argSpan.End;
        while (start < end && char.IsWhiteSpace(_text[start]))
            start++;
        while (end > start && char.IsWhiteSpace(_text[end - 1]))
            end--;
        if (end <= start)
            return;

        if (_text[start] == '[' && _text[end - 1] == ']')
        {
            start++;
            end--;
            while (start < end && char.IsWhiteSpace(_text[start]))
                start++;
            while (end > start && char.IsWhiteSpace(_text[end - 1]))
                end--;
        }

        if (end <= start)
            return;
        if (!IsIdentifierRange(start, end))
            return;

        name = _text.Substring(start, end - start);
        nameSpan = new TextSpan(start, end - start);
    }

    private static string? StripOuterQuotes(string argText)
    {
        string t = argText.Trim();
        if (t.Length >= 2 && t[0] == '[' && t[^1] == ']')
            return t.Substring(1, t.Length - 2).Trim();
        if (t.Length >= 2 && ((t[0] == '"' && t[^1] == '"') || (t[0] == '\'' && t[^1] == '\'')))
            return t.Substring(1, t.Length - 2);
        return t.Length > 0 ? t : null;
    }

    private bool IsIdentifierRange(int start, int end)
    {
        if (end <= start)
            return false;
        char c0 = _text[start];
        if (!(char.IsLetter(c0) || c0 == '_'))
            return false;
        for (int i = start + 1; i < end; i++)
        {
            char c = _text[i];
            if (!(char.IsLetterOrDigit(c) || c == '_'))
                return false;
        }
        return true;
    }
}
