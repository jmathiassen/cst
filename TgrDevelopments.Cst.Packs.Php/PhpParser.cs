using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Php;

internal sealed class PhpParser
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

    public PhpParser(string text, IReadOnlyList<Token> tokens, LineMap map, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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
            TryParseNamespace(children, outlines);
            ParseItems(children, outlines, 1, false);
        }
        catch (OperationCanceledException)
        {
            _cancelled = true;
        }
        return children;
    }

    private void TryParseNamespace(List<SyntaxNode> children, List<OutlineNode> outlines)
    {
        if (!_r.Check((int)PhpTokenKind.KwNamespace))
            return;
        int start = _r.Current.Start;
        _r.Advance();
        if (_r.Check((int)PhpTokenKind.Identifier))
        {
            ScanDottedName();
            if (_r.Check((int)PhpTokenKind.Semicolon))
                _r.Advance();
            TextSpan span = new(start, _r.Peek(-1).End - start);
            children.Add(new SyntaxNode("namespace", span, ""));
        }
        else if (_r.Check((int)PhpTokenKind.LBrace))
        {
            _r.TrySkipBalanced((int)PhpTokenKind.LBrace, (int)PhpTokenKind.RBrace);
        }
    }

    private void ParseItems(List<SyntaxNode> parentChildren, List<OutlineNode> parentOutlines, int baseLevel, bool insideType, bool stopAtBrace = false)
    {
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();

            if (_r.Check((int)PhpTokenKind.RBrace))
            {
                if (stopAtBrace) return;
                _r.Advance(); continue;
            }
            if (_r.Check((int)PhpTokenKind.Semicolon))
            {
                _r.Advance(); continue;
            }

            SkipModifiersAndAttributes();

            if (_r.Check((int)PhpTokenKind.KwClass) || _r.Check((int)PhpTokenKind.KwTrait)
                || _r.Check((int)PhpTokenKind.KwInterface) || _r.Check((int)PhpTokenKind.KwEnum))
            {
                PhpTokenKind tkind = (PhpTokenKind)_r.Current.Kind;
                _r.Advance();
                TryParseTypeBody(parentChildren, parentOutlines, baseLevel, KindString(tkind));
                continue;
            }

            if (_r.Check((int)PhpTokenKind.KwFunction))
            {
                TryParseFunction(parentChildren, parentOutlines, baseLevel, insideType);
                continue;
            }

            if (_r.Check((int)PhpTokenKind.KwUse))
            {
                _r.Advance();
                SkipToSemicolonOrBrace();
                if (_r.Check((int)PhpTokenKind.LBrace))
                    _r.TrySkipBalanced((int)PhpTokenKind.LBrace, (int)PhpTokenKind.RBrace);
                continue;
            }

            if (!SkipTrash()) return;
        }
    }

    private void SkipModifiersAndAttributes()
    {
        while (!_r.IsEof)
        {
            if (_r.Check((int)PhpTokenKind.KwPublic) || _r.Check((int)PhpTokenKind.KwPrivate)
                || _r.Check((int)PhpTokenKind.KwProtected) || _r.Check((int)PhpTokenKind.KwStatic)
                || _r.Check((int)PhpTokenKind.KwAbstract) || _r.Check((int)PhpTokenKind.KwFinal)
                || _r.Check((int)PhpTokenKind.KwReadonly))
            {
                _r.Advance(); continue;
            }
            if (_r.Check((int)PhpTokenKind.Hash) && _r.Peek(1).Kind == (int)PhpTokenKind.LBracket)
            {
                _r.Advance();
                if (!_r.TrySkipBalanced((int)PhpTokenKind.LBracket, (int)PhpTokenKind.RBracket))
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic("PHP001", "Unclosed attribute.", _r.Current.Span, DiagnosticSeverity.Error));
                    return;
                }
                continue;
            }
            break;
        }
    }

    private bool TryParseTypeBody(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel, string ok)
    {
        int start = _r.Current.Start;
        if (!_r.Check((int)PhpTokenKind.Identifier))
        {
            SkipToSemicolonOrBrace();
            return true;
        }
        string name = _r.Current.Text ?? "type";
        TextSpan nameSpan = _r.Current.Span;
        _r.Advance();

        if (_r.Check((int)PhpTokenKind.KwExtends))
        {
            _r.Advance();
            SkipType();
        }
        if (_r.Check((int)PhpTokenKind.KwImplements))
        {
            _r.Advance();
            SkipTypeList();
        }

        if (!_r.Check((int)PhpTokenKind.LBrace))
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
            _diagnostics.Add(new ParseDiagnostic("PHP005", "Max depth exceeded.", new TextSpan(start, _r.Current.End - start), DiagnosticSeverity.Warning));
            _r.TrySkipBalanced((int)PhpTokenKind.LBrace, (int)PhpTokenKind.RBrace);
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
        if (_r.Check((int)PhpTokenKind.RBrace))
        {
            bodyEnd = _r.Current.End;
            _r.Advance();
        }
        else
        {
            bodyEnd = _r.Peek(-1).End;
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("PHP001", "Unclosed type body.", new TextSpan(bodyOpen, 0), DiagnosticSeverity.Error));
        }

        TextSpan span = new(start, bodyEnd - start);
        children.Add(new SyntaxNode(ok + "_declaration", span, name, nameSpan, innerChildren.Count > 0 ? innerChildren : null));
        outlines.Add(MakeOutline(ok, name, span, baseLevel, innerOutlines.Count > 0 ? innerOutlines : null));
        _nodeCount++;
        _depth--;
        return true;
    }

    private bool TryParseFunction(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel, bool insideType)
    {
        int start = _r.Current.Start;
        _r.Advance();

        if (!_r.Check((int)PhpTokenKind.Identifier))
        {
            SkipToSemicolonOrBrace();
            return true;
        }
        string name = _r.Current.Text ?? "function";
        TextSpan nameSpan = _r.Current.Span;
        _r.Advance();

        if (_r.Check((int)PhpTokenKind.LParen))
        {
            if (!_r.TrySkipBalanced((int)PhpTokenKind.LParen, (int)PhpTokenKind.RParen))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("PHP001", "Unclosed parameter list.", _r.Current.Span, DiagnosticSeverity.Error));
                SkipToSemicolonOrBrace();
                AddFunction(children, outlines, name, nameSpan, start, insideType);
                return true;
            }
        }

        if (_r.Check((int)PhpTokenKind.Colon))
        {
            _r.Advance();
            SkipType();
        }

        if (_r.Check((int)PhpTokenKind.LBrace))
        {
            if (!_r.TrySkipBalanced((int)PhpTokenKind.LBrace, (int)PhpTokenKind.RBrace))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("PHP001", "Unclosed function body.", _r.Current.Span, DiagnosticSeverity.Error));
            }
        }
        else
        {
            SkipToSemicolonOrBrace();
        }

        AddFunction(children, outlines, name, nameSpan, start, insideType);
        return true;
    }

    private void AddFunction(List<SyntaxNode> children, List<OutlineNode> outlines, string name, TextSpan nameSpan, int start, bool insideType)
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

    private void ScanDottedName()
    {
        while (_r.Check((int)PhpTokenKind.Identifier))
        {
            _r.Advance();
            if (_r.Check((int)PhpTokenKind.Backslash))
                _r.Advance();
            else break;
        }
    }

    private void SkipType()
    {
        while (!_r.IsEof)
        {
            PhpTokenKind k = (PhpTokenKind)_r.Current.Kind;
            if (k is PhpTokenKind.Semicolon or PhpTokenKind.LBrace or PhpTokenKind.RBrace
                or PhpTokenKind.KwExtends or PhpTokenKind.KwImplements)
                return;
            if (k == PhpTokenKind.LBracket) _r.TrySkipBalanced((int)PhpTokenKind.LBracket, (int)PhpTokenKind.RBracket);
            else _r.Advance();
        }
    }

    private void SkipTypeList()
    {
        while (!_r.IsEof)
        {
            PhpTokenKind k = (PhpTokenKind)_r.Current.Kind;
            if (k is PhpTokenKind.Semicolon or PhpTokenKind.LBrace or PhpTokenKind.RBrace) return;
            if (k == PhpTokenKind.Comma) _r.Advance();
            else _r.Advance();
        }
    }

    private void SkipToSemicolonOrBrace()
    {
        while (!_r.IsEof)
        {
            PhpTokenKind k = (PhpTokenKind)_r.Current.Kind;
            if (k == PhpTokenKind.Semicolon) { _r.Advance(); return; }
            if (k is PhpTokenKind.LBrace or PhpTokenKind.RBrace) return;
            if (k is PhpTokenKind.KwClass or PhpTokenKind.KwTrait or PhpTokenKind.KwInterface
                or PhpTokenKind.KwEnum or PhpTokenKind.KwFunction
                or PhpTokenKind.KwPublic or PhpTokenKind.KwPrivate or PhpTokenKind.KwProtected
                or PhpTokenKind.KwAbstract or PhpTokenKind.KwFinal)
                return;
            if (k == PhpTokenKind.LParen) _r.TrySkipBalanced((int)PhpTokenKind.LParen, (int)PhpTokenKind.RParen);
            else if (k == PhpTokenKind.LBracket) _r.TrySkipBalanced((int)PhpTokenKind.LBracket, (int)PhpTokenKind.RBracket);
            else _r.Advance();
        }
    }

    private bool SkipTrash()
    {
        while (!_r.IsEof)
        {
            PhpTokenKind k = (PhpTokenKind)_r.Current.Kind;
            if (k is PhpTokenKind.KwClass or PhpTokenKind.KwTrait or PhpTokenKind.KwInterface
                or PhpTokenKind.KwEnum or PhpTokenKind.KwFunction
                or PhpTokenKind.KwPublic or PhpTokenKind.KwPrivate or PhpTokenKind.KwProtected
                or PhpTokenKind.KwStatic or PhpTokenKind.KwAbstract or PhpTokenKind.KwFinal
                or PhpTokenKind.Hash or PhpTokenKind.KwUse
                or PhpTokenKind.LBrace or PhpTokenKind.RBrace or PhpTokenKind.Semicolon)
                return true;
            _r.Advance();
        }
        return false;
    }

    private void CheckCaps(int start, int end)
    {
        if (_depth >= MaxDepth)
            _diagnostics.Add(new ParseDiagnostic("PHP005", "Max depth exceeded.", new TextSpan(start, end - start), DiagnosticSeverity.Warning));
        if (_nodeCount >= MaxOutlineNodes)
            _diagnostics.Add(new ParseDiagnostic("PHP006", "Max outline nodes exceeded.", new TextSpan(start, end - start), DiagnosticSeverity.Warning));
    }

    private static string KindString(PhpTokenKind kind) => kind switch
    {
        PhpTokenKind.KwClass => "class",
        PhpTokenKind.KwTrait => "trait",
        PhpTokenKind.KwInterface => "interface",
        PhpTokenKind.KwEnum => "enum",
        _ => "class",
    };

    private OutlineNode MakeOutline(string kind, string label, TextSpan span, int level, IReadOnlyList<OutlineNode>? children = null)
    {
        LinePositionSpan lp = _map.GetLinePositionSpan(span);
        LinePositionSpan nav = new(new LinePosition(lp.Start.Line, 1), new LinePosition(lp.End.Line, 1));
        return new OutlineNode(kind, label, span, nav, level, children);
    }
}
