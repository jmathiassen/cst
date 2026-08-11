using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Swift;

internal sealed class SwiftParser
{
    private const int MaxDepth = 64;
    private const int MaxOutlineNodes = 500;

    private readonly string _text;
    private readonly TokenReader _r;
    private readonly LineMap _map;
    private readonly List<ParseDiagnostic> _diagnostics;
    private readonly CancellationToken _ct;
    private int _depth;
    private int _nodeCount;
    private bool _partial;
    private bool _cancelled;

    public SwiftParser(string text, IReadOnlyList<Token> tokens, LineMap map, List<ParseDiagnostic> diagnostics, CancellationToken ct)
    {
        _text = text;
        _r = new TokenReader(tokens, ct);
        _map = map;
        _diagnostics = diagnostics;
        _ct = ct;
    }

    public bool IsPartial => _partial;
    public bool IsCancelled => _cancelled;

    public List<SyntaxNode> Parse(out List<OutlineNode> outlines)
    {
        List<SyntaxNode> children = [];
        outlines = [];
        if (_r.Count <= 1)
            return children;
        try
        {
            ParseItems(children, outlines, 1, false);
        }
        catch (OperationCanceledException)
        {
            _cancelled = true;
        }
        return children;
    }

    private void ParseItems(List<SyntaxNode> parentChildren, List<OutlineNode> parentOutlines, int baseLevel, bool insideType, bool stopAtBrace = false)
    {
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();

            if (_r.Check((int)SwiftTokenKind.RBrace))
            {
                if (stopAtBrace) return;
                _r.Advance(); continue;
            }
            if (_r.Check((int)SwiftTokenKind.Semicolon))
            {
                _r.Advance(); continue;
            }

            SkipModifiersAndAttributes();

            if (_r.Check((int)SwiftTokenKind.KwClass) || _r.Check((int)SwiftTokenKind.KwStruct)
                || _r.Check((int)SwiftTokenKind.KwEnum) || _r.Check((int)SwiftTokenKind.KwProtocol)
                || _r.Check((int)SwiftTokenKind.KwExtension))
            {
                SwiftTokenKind tkind = (SwiftTokenKind)_r.Current.Kind;
                bool isExtension = tkind == SwiftTokenKind.KwExtension;
                _r.Advance();
                TryParseTypeBody(parentChildren, parentOutlines, baseLevel, KindString(tkind));
                continue;
            }

            if (_r.Check((int)SwiftTokenKind.KwFunc) || _r.Check((int)SwiftTokenKind.KwInit))
            {
                TryParseFunc(parentChildren, parentOutlines, baseLevel, insideType);
                continue;
            }

            if (_r.Check((int)SwiftTokenKind.KwLet) || _r.Check((int)SwiftTokenKind.KwVar))
            {
                _r.Advance();
                if (_r.Check((int)SwiftTokenKind.Identifier))
                    _r.Advance();
                SkipToSemicolonOrBrace();
                continue;
            }

            if (_r.Check((int)SwiftTokenKind.KwTypeAlias))
            {
                _r.Advance();
                SkipToSemicolonOrBrace();
                continue;
            }

            if (!SkipTrash()) return;
        }
    }

    private void SkipModifiersAndAttributes()
    {
        while (!_r.IsEof)
        {
            if (_r.Check((int)SwiftTokenKind.KwPublic) || _r.Check((int)SwiftTokenKind.KwPrivate)
                || _r.Check((int)SwiftTokenKind.KwInternal) || _r.Check((int)SwiftTokenKind.KwFilePrivate)
                || _r.Check((int)SwiftTokenKind.KwOpen) || _r.Check((int)SwiftTokenKind.KwStatic)
                || _r.Check((int)SwiftTokenKind.KwOverride) || _r.Check((int)SwiftTokenKind.KwFinal)
                || _r.Check((int)SwiftTokenKind.KwMutating) || _r.Check((int)SwiftTokenKind.KwNonMutating)
                || _r.Check((int)SwiftTokenKind.KwConvenience) || _r.Check((int)SwiftTokenKind.KwRequired)
                || _r.Check((int)SwiftTokenKind.KwDynamic) || _r.Check((int)SwiftTokenKind.KwLazy)
                || _r.Check((int)SwiftTokenKind.KwWeak) || _r.Check((int)SwiftTokenKind.KwUnowned))
            {
                _r.Advance(); continue;
            }
            if (_r.Check((int)SwiftTokenKind.At))
            {
                _r.Advance();
                if (_r.Check((int)SwiftTokenKind.Identifier))
                {
                    _r.Advance();
                    if (_r.Check((int)SwiftTokenKind.LParen))
                        _r.TrySkipBalanced((int)SwiftTokenKind.LParen, (int)SwiftTokenKind.RParen);
                }
                continue;
            }
            break;
        }
    }

    private bool TryParseTypeBody(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel, string ok)
    {
        int start = _r.Current.Start;
        string name = ok;
        TextSpan nameSpan = default;

        if (_r.Check((int)SwiftTokenKind.Identifier))
        {
            name = _r.Current.Text ?? ok;
            nameSpan = _r.Current.Span;
            _r.Advance();

            if (_r.Check((int)SwiftTokenKind.Period))
            {
                _r.Advance();
                if (_r.Check((int)SwiftTokenKind.Identifier))
                {
                    name = _r.Current.Text ?? name;
                    _r.Advance();
                }
            }
        }

        if (_r.Check((int)SwiftTokenKind.LAngle))
            TrySkipGenerics();

        if (_r.Check((int)SwiftTokenKind.Colon))
        {
            _r.Advance();
            SkipTypeList();
        }

        if (_r.Check((int)SwiftTokenKind.KwWhere))
        {
            _r.Advance();
            SkipTypeList();
        }

        if (!_r.Check((int)SwiftTokenKind.LBrace))
        {
            SkipToSemicolonOrBrace();
            int e1 = _r.Peek(-1).End;
            CheckCaps(start, e1);
            TextSpan s1 = new(start, e1 - start);
            children.Add(new SyntaxNode(ok + "_declaration", s1, name, nameSpan));
            outlines.Add(MakeOutline(ok, name, s1, baseLevel));
            _nodeCount++;
            return true;
        }

        if (baseLevel + 1 > MaxDepth)
        {
            _diagnostics.Add(new ParseDiagnostic("SWIFT005", "Max depth exceeded.", new TextSpan(start, _r.Current.End - start), DiagnosticSeverity.Warning));
            _r.TrySkipBalanced((int)SwiftTokenKind.LBrace, (int)SwiftTokenKind.RBrace);
            int e2 = _r.Peek(-1).End;
            TextSpan s2 = new(start, e2 - start);
            children.Add(new SyntaxNode(ok + "_declaration", s2, name, nameSpan));
            outlines.Add(MakeOutline(ok, name, s2, baseLevel));
            _nodeCount++;
            return true;
        }

        _depth++;
        int bodyOpen = _r.Current.Start;
        _r.Advance();

        List<SyntaxNode> innerChildren = [];
        List<OutlineNode> innerOutlines = [];
        if (_nodeCount < MaxOutlineNodes)
            ParseItems(innerChildren, innerOutlines, baseLevel + 1, true, stopAtBrace: true);

        int bodyEnd;
        if (_r.Check((int)SwiftTokenKind.RBrace))
        {
            bodyEnd = _r.Current.End;
            _r.Advance();
        }
        else
        {
            bodyEnd = _r.Peek(-1).End;
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("SWIFT001", "Unclosed type body.", new TextSpan(bodyOpen, 0), DiagnosticSeverity.Error));
        }

        TextSpan span = new(start, bodyEnd - start);
        children.Add(new SyntaxNode(ok + "_declaration", span, name, nameSpan, innerChildren.Count > 0 ? innerChildren : null));
        outlines.Add(MakeOutline(ok, name, span, baseLevel, innerOutlines.Count > 0 ? innerOutlines : null));
        _nodeCount++;
        _depth--;
        return true;
    }

    private bool TryParseFunc(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel, bool insideType)
    {
        int start = _r.Current.Start;
        bool isInit = _r.Check((int)SwiftTokenKind.KwInit);
        _r.Advance();

        string name = isInit ? "init" : "func";
        TextSpan nameSpan = default;

        if (isInit)
        {
            name = "init";
            nameSpan = _r.Peek(-1).Span;
        }
        else if (_r.Check((int)SwiftTokenKind.Identifier))
        {
            name = _r.Current.Text ?? "func";
            nameSpan = _r.Current.Span;
            _r.Advance();
        }

        if (_r.Check((int)SwiftTokenKind.LAngle))
            TrySkipGenerics();

        if (_r.Check((int)SwiftTokenKind.LParen))
        {
            if (!_r.TrySkipBalanced((int)SwiftTokenKind.LParen, (int)SwiftTokenKind.RParen))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("SWIFT001", "Unclosed parameter list.", _r.Current.Span, DiagnosticSeverity.Error));
                SkipToSemicolonOrBrace();
                AddFunc(children, outlines, name, nameSpan, start, insideType);
                return true;
            }
        }

        if (_r.Check((int)SwiftTokenKind.KwThrows) || _r.Check((int)SwiftTokenKind.KwRethrows))
            _r.Advance();

        if (_r.Check((int)SwiftTokenKind.Arrow))
        {
            _r.Advance();
            SkipType();
        }

        if (_r.Check((int)SwiftTokenKind.KwWhere))
        {
            _r.Advance();
            SkipTypeList();
        }

        if (_r.Check((int)SwiftTokenKind.LBrace))
        {
            if (!_r.TrySkipBalanced((int)SwiftTokenKind.LBrace, (int)SwiftTokenKind.RBrace))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("SWIFT001", "Unclosed function body.", _r.Current.Span, DiagnosticSeverity.Error));
            }
        }
        else
        {
            SkipToSemicolonOrBrace();
        }

        AddFunc(children, outlines, name, nameSpan, start, insideType);
        return true;
    }

    private void AddFunc(List<SyntaxNode> children, List<OutlineNode> outlines, string name, TextSpan nameSpan, int start, bool insideType)
    {
        int end = _r.Peek(-1).End;
        CheckCaps(start, end);
        TextSpan span = new(start, end - start);
        string nk = insideType ? "method_declaration" : "function_declaration";
        string ok = insideType ? "method" : "function";
        children.Add(new SyntaxNode(nk, span, name, nameSpan));
        outlines.Add(MakeOutline(ok, name, span, _depth));
        _nodeCount++;
    }

    private void SkipType()
    {
        while (!_r.IsEof)
        {
            SwiftTokenKind k = (SwiftTokenKind)_r.Current.Kind;
            if (k is SwiftTokenKind.Semicolon or SwiftTokenKind.LBrace or SwiftTokenKind.RBrace
                or SwiftTokenKind.KwWhere)
                return;
            if (k == SwiftTokenKind.LAngle) TrySkipGenerics();
            else if (k == SwiftTokenKind.LBracket) _r.TrySkipBalanced((int)SwiftTokenKind.LBracket, (int)SwiftTokenKind.RBracket);
            else if (k == SwiftTokenKind.LParen) _r.TrySkipBalanced((int)SwiftTokenKind.LParen, (int)SwiftTokenKind.RParen);
            else _r.Advance();
        }
    }

    private void SkipTypeList()
    {
        while (!_r.IsEof)
        {
            SwiftTokenKind k = (SwiftTokenKind)_r.Current.Kind;
            if (k is SwiftTokenKind.Semicolon or SwiftTokenKind.LBrace or SwiftTokenKind.RBrace) return;
            if (k == SwiftTokenKind.Comma) _r.Advance();
            else if (k == SwiftTokenKind.LAngle) TrySkipGenerics();
            else _r.Advance();
        }
    }

    private bool TrySkipGenerics()
    {
        if (!_r.TrySkipBalanced((int)SwiftTokenKind.LAngle, (int)SwiftTokenKind.RAngle))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("SWIFT001", "Unclosed generics.", _r.Current.Span, DiagnosticSeverity.Error));
            return false;
        }
        return true;
    }

    private void SkipToSemicolonOrBrace()
    {
        while (!_r.IsEof)
        {
            SwiftTokenKind k = (SwiftTokenKind)_r.Current.Kind;
            if (k == SwiftTokenKind.Semicolon) { _r.Advance(); return; }
            if (k is SwiftTokenKind.LBrace or SwiftTokenKind.RBrace) return;
            if (k is SwiftTokenKind.KwClass or SwiftTokenKind.KwStruct or SwiftTokenKind.KwEnum
                or SwiftTokenKind.KwProtocol or SwiftTokenKind.KwExtension or SwiftTokenKind.KwFunc
                or SwiftTokenKind.KwInit or SwiftTokenKind.KwLet or SwiftTokenKind.KwVar
                or SwiftTokenKind.KwTypeAlias or SwiftTokenKind.KwPublic or SwiftTokenKind.KwPrivate
                or SwiftTokenKind.KwInternal or SwiftTokenKind.KwFilePrivate or SwiftTokenKind.KwOpen)
                return;
            if (k == SwiftTokenKind.LParen && !_r.TrySkipBalanced((int)SwiftTokenKind.LParen, (int)SwiftTokenKind.RParen)) return;
            if (k == SwiftTokenKind.LBracket && !_r.TrySkipBalanced((int)SwiftTokenKind.LBracket, (int)SwiftTokenKind.RBracket)) return;
            if (k == SwiftTokenKind.LAngle && !TrySkipGenerics()) return;
            _r.Advance();
        }
    }

    private bool SkipTrash()
    {
        while (!_r.IsEof)
        {
            SwiftTokenKind k = (SwiftTokenKind)_r.Current.Kind;
            if (k is SwiftTokenKind.KwClass or SwiftTokenKind.KwStruct or SwiftTokenKind.KwEnum
                or SwiftTokenKind.KwProtocol or SwiftTokenKind.KwExtension or SwiftTokenKind.KwFunc
                or SwiftTokenKind.KwInit or SwiftTokenKind.KwLet or SwiftTokenKind.KwVar
                or SwiftTokenKind.KwTypeAlias or SwiftTokenKind.KwPublic or SwiftTokenKind.KwPrivate
                or SwiftTokenKind.KwInternal or SwiftTokenKind.KwFilePrivate or SwiftTokenKind.KwOpen
                or SwiftTokenKind.KwStatic or SwiftTokenKind.KwOverride or SwiftTokenKind.KwFinal
                or SwiftTokenKind.At or SwiftTokenKind.LBrace
                or SwiftTokenKind.RBrace or SwiftTokenKind.Semicolon)
                return true;
            _r.Advance();
        }
        return false;
    }

    private void CheckCaps(int start, int end)
    {
        if (_depth >= MaxDepth)
            _diagnostics.Add(new ParseDiagnostic("SWIFT005", "Max depth exceeded.", new TextSpan(start, end - start), DiagnosticSeverity.Warning));
        if (_nodeCount >= MaxOutlineNodes)
            _diagnostics.Add(new ParseDiagnostic("SWIFT006", "Max outline nodes exceeded.", new TextSpan(start, end - start), DiagnosticSeverity.Warning));
    }

    private static string KindString(SwiftTokenKind kind) => kind switch
    {
        SwiftTokenKind.KwClass => "class",
        SwiftTokenKind.KwStruct => "struct",
        SwiftTokenKind.KwEnum => "enum",
        SwiftTokenKind.KwProtocol => "protocol",
        SwiftTokenKind.KwExtension => "extension",
        _ => "class",
    };

    private OutlineNode MakeOutline(string kind, string label, TextSpan span, int level, IReadOnlyList<OutlineNode>? children = null)
    {
        LinePositionSpan lp = _map.GetLinePositionSpan(span);
        LinePositionSpan nav = new(new LinePosition(lp.Start.Line, 1), new LinePosition(lp.End.Line, 1));
        return new OutlineNode(kind, label, span, nav, level, children);
    }
}
