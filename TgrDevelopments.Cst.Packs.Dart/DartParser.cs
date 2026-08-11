using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Dart;

internal sealed class DartParser
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

    public DartParser(string text, IReadOnlyList<Token> tokens, LineMap map, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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
            SkipLibrary();
            SkipImports();
            ParseItems(children, outlines, 1, false);
        }
        catch (OperationCanceledException)
        {
            _cancelled = true;
        }
        return children;
    }

    private void SkipLibrary()
    {
        if (_r.Check((int)DartTokenKind.KwLibrary))
        {
            _r.Advance();
            ScanDottedName();
            if (_r.Check((int)DartTokenKind.Semicolon))
                _r.Advance();
        }
    }

    private void SkipImports()
    {
        while (_r.Check((int)DartTokenKind.KwImport) || _r.Check((int)DartTokenKind.KwExport)
            || _r.Check((int)DartTokenKind.KwPart))
        {
            _r.Advance();
            SkipToSemicolon();
        }
    }

    private void ParseItems(List<SyntaxNode> parentChildren, List<OutlineNode> parentOutlines, int baseLevel, bool insideType, bool stopAtBrace = false)
    {
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();

            if (_r.Check((int)DartTokenKind.RBrace))
            {
                if (stopAtBrace)
                    return;
                _r.Advance();
                continue;
            }
            if (_r.Check((int)DartTokenKind.Semicolon))
            {
                _r.Advance();
                continue;
            }

            SkipModifiersAndAnnotations();

            if (_r.Check((int)DartTokenKind.KwClass) || _r.Check((int)DartTokenKind.KwMixin)
                || _r.Check((int)DartTokenKind.KwEnum))
            {
                DartTokenKind tkind = (DartTokenKind)_r.Current.Kind;
                _r.Advance();
                TryParseTypeBody(parentChildren, parentOutlines, baseLevel, KindString(tkind));
                continue;
            }

            if (_r.Check((int)DartTokenKind.KwTypedef))
            {
                _r.Advance();
                SkipToSemicolon();
                continue;
            }

            TryParseMember(parentChildren, parentOutlines, baseLevel);
        }
    }

    private void SkipModifiersAndAnnotations()
    {
        while (!_r.IsEof)
        {
            if (_r.Check((int)DartTokenKind.KwAbstract) || _r.Check((int)DartTokenKind.KwBase)
                || _r.Check((int)DartTokenKind.KwSealed) || _r.Check((int)DartTokenKind.KwFinal)
                || _r.Check((int)DartTokenKind.KwConst) || _r.Check((int)DartTokenKind.KwFactory)
                || _r.Check((int)DartTokenKind.KwLate) || _r.Check((int)DartTokenKind.KwRequired)
                || _r.Check((int)DartTokenKind.KwVar))
            {
                _r.Advance();
                continue;
            }
            if (_r.Check((int)DartTokenKind.At))
            {
                _r.Advance();
                if (_r.Check((int)DartTokenKind.Identifier))
                {
                    _r.Advance();
                    if (_r.Check((int)DartTokenKind.LParen))
                        _r.TrySkipBalanced((int)DartTokenKind.LParen, (int)DartTokenKind.RParen);
                }
                continue;
            }
            break;
        }
    }

    private bool TryParseTypeBody(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel, string ok)
    {
        int start = _r.Current.Start;

        if (_r.Check((int)DartTokenKind.At))
        {
            _r.Advance();
            if (_r.Check((int)DartTokenKind.Identifier))
            {
                _r.Advance();
                if (_r.Check((int)DartTokenKind.LParen))
                    _r.TrySkipBalanced((int)DartTokenKind.LParen, (int)DartTokenKind.RParen);
            }
        }

        if (!_r.Check((int)DartTokenKind.Identifier))
        {
            SkipToSemicolonOrBrace();
            return true;
        }
        string name = _r.Current.Text ?? "type";
        TextSpan nameSpan = _r.Current.Span;
        _r.Advance();

        if (_r.Check((int)DartTokenKind.LAngle))
            TrySkipGenerics();

        if (_r.Check((int)DartTokenKind.KwExtends))
        {
            _r.Advance();
            SkipType();
        }

        if (_r.Check((int)DartTokenKind.KwWith))
        {
            _r.Advance();
            SkipTypeList();
        }

        if (_r.Check((int)DartTokenKind.KwImplements))
        {
            _r.Advance();
            SkipTypeList();
        }

        if (!_r.Check((int)DartTokenKind.LBrace))
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
            _diagnostics.Add(new ParseDiagnostic("DART005", "Max depth exceeded.", new TextSpan(start, _r.Current.End - start), DiagnosticSeverity.Warning));
            _r.TrySkipBalanced((int)DartTokenKind.LBrace, (int)DartTokenKind.RBrace);
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
        if (_r.Check((int)DartTokenKind.RBrace))
        {
            bodyEnd = _r.Current.End;
            _r.Advance();
        }
        else
        {
            bodyEnd = _r.Peek(-1).End;
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("DART001", "Unclosed type body.", new TextSpan(bodyOpen, 0), DiagnosticSeverity.Error));
        }

        TextSpan span = new(start, bodyEnd - start);
        children.Add(new SyntaxNode(ok + "_declaration", span, name, nameSpan, innerChildren.Count > 0 ? innerChildren : null));
        outlines.Add(MakeOutline(ok, name, span, baseLevel, innerOutlines.Count > 0 ? innerOutlines : null));
        _nodeCount++;
        _depth--;
        return true;
    }

    private void TryParseMember(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel)
    {
        int start = _r.Current.Start;
        string? lastName = null;
        TextSpan lastNameSpan = default;

        while (!_r.IsEof)
        {
            if (_r.Check((int)DartTokenKind.RBrace))
                return;
            if (_r.Check((int)DartTokenKind.LParen))
            {
                if (lastName is null)
                {
                    _r.TrySkipBalanced((int)DartTokenKind.LParen, (int)DartTokenKind.RParen);
                    SkipToSemicolonOrBrace();
                    return;
                }
                ParseMethodOrConstructor(children, outlines, lastName, lastNameSpan, start, baseLevel);
                return;
            }
            if (_r.Check((int)DartTokenKind.Semicolon))
            {
                _r.Advance();
                return;
            }
            if (_r.Check((int)DartTokenKind.LBrace))
            {
                _r.TrySkipBalanced((int)DartTokenKind.LBrace, (int)DartTokenKind.RBrace);
                return;
            }
            if (_r.Check((int)DartTokenKind.LAngle))
            {
                TrySkipGenerics();
                continue;
            }
            if (_r.Check((int)DartTokenKind.Identifier) || _r.Check((int)DartTokenKind.KwVoid))
            {
                lastName = _r.Current.Text ?? "member";
                lastNameSpan = _r.Current.Span;
                _r.Advance();
                continue;
            }
            if (_r.Check((int)DartTokenKind.LBracket))
            {
                _r.TrySkipBalanced((int)DartTokenKind.LBracket, (int)DartTokenKind.RBracket);
                continue;
            }
            if (_r.Check((int)DartTokenKind.Question))
            {
                _r.Advance();
                continue;
            }
            _r.Advance();
        }
    }

    private void ParseMethodOrConstructor(List<SyntaxNode> children, List<OutlineNode> outlines, string name, TextSpan nameSpan, int start, int baseLevel)
    {
        _r.TrySkipBalanced((int)DartTokenKind.LParen, (int)DartTokenKind.RParen);

        if (_r.Check((int)DartTokenKind.Colon))
        {
            _r.Advance();
            SkipToSemicolonOrBrace();
        }

        if (_r.Check((int)DartTokenKind.Arrow))
        {
            _r.Advance();
            SkipToSemicolonOrBrace();
        }

        if (_r.Check((int)DartTokenKind.LBrace))
        {
            if (!_r.TrySkipBalanced((int)DartTokenKind.LBrace, (int)DartTokenKind.RBrace))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("DART001", "Unclosed method body.", _r.Current.Span, DiagnosticSeverity.Error));
            }
        }
        else
        {
            SkipToSemicolonOrBrace();
        }

        int end = _r.Peek(-1).End;
        CheckCaps(start, end);
        TextSpan span = new(start, end - start);
        children.Add(new SyntaxNode("method_declaration", span, name, nameSpan));
        outlines.Add(MakeOutline("method", name, span, baseLevel));
        _nodeCount++;
    }

    private string? ScanDottedName()
    {
        if (!_r.Check((int)DartTokenKind.Identifier))
            return null;
        string result = _r.Current.Text ?? "";
        _r.Advance();
        while (_r.Check((int)DartTokenKind.Period))
        {
            _r.Advance();
            if (!_r.Check((int)DartTokenKind.Identifier))
                break;
            result += "." + (_r.Current.Text ?? "");
            _r.Advance();
        }
        return result;
    }

    private void SkipType()
    {
        while (!_r.IsEof)
        {
            DartTokenKind k = (DartTokenKind)_r.Current.Kind;
            if (k is DartTokenKind.Semicolon or DartTokenKind.LBrace or DartTokenKind.RBrace
                or DartTokenKind.KwWith or DartTokenKind.KwImplements or DartTokenKind.KwExtends)
                return;
            if (k == DartTokenKind.LAngle) TrySkipGenerics();
            else if (k == DartTokenKind.LParen) _r.TrySkipBalanced((int)DartTokenKind.LParen, (int)DartTokenKind.RParen);
            else if (k == DartTokenKind.LBracket) _r.TrySkipBalanced((int)DartTokenKind.LBracket, (int)DartTokenKind.RBracket);
            else if (k == DartTokenKind.Question) _r.Advance();
            else _r.Advance();
        }
    }

    private void SkipTypeList()
    {
        while (!_r.IsEof)
        {
            DartTokenKind k = (DartTokenKind)_r.Current.Kind;
            if (k is DartTokenKind.Semicolon or DartTokenKind.LBrace or DartTokenKind.RBrace)
                return;
            if (k == DartTokenKind.LAngle) TrySkipGenerics();
            else if (k == DartTokenKind.Comma) _r.Advance();
            else if (k == DartTokenKind.LParen) _r.TrySkipBalanced((int)DartTokenKind.LParen, (int)DartTokenKind.RParen);
            else _r.Advance();
        }
    }

    private bool TrySkipGenerics()
    {
        if (!_r.TrySkipBalanced((int)DartTokenKind.LAngle, (int)DartTokenKind.RAngle))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("DART001", "Unclosed generics.", _r.Current.Span, DiagnosticSeverity.Error));
            return false;
        }
        return true;
    }

    private void SkipToSemicolon()
    {
        while (!_r.IsEof)
        {
            DartTokenKind k = (DartTokenKind)_r.Current.Kind;
            if (k == DartTokenKind.Semicolon) { _r.Advance(); return; }
            if (k is DartTokenKind.LBrace or DartTokenKind.RBrace) return;
            if (k is DartTokenKind.KwClass or DartTokenKind.KwMixin or DartTokenKind.KwEnum
                or DartTokenKind.KwAbstract or DartTokenKind.KwBase or DartTokenKind.KwSealed
                or DartTokenKind.KwFinal or DartTokenKind.KwTypedef) return;
            if (k == DartTokenKind.LParen) _r.TrySkipBalanced((int)DartTokenKind.LParen, (int)DartTokenKind.RParen);
            else if (k == DartTokenKind.LBracket) _r.TrySkipBalanced((int)DartTokenKind.LBracket, (int)DartTokenKind.RBracket);
            else if (k == DartTokenKind.LAngle) TrySkipGenerics();
            else _r.Advance();
        }
    }

    private void SkipToSemicolonOrBrace()
    {
        while (!_r.IsEof)
        {
            DartTokenKind k = (DartTokenKind)_r.Current.Kind;
            if (k == DartTokenKind.Semicolon) { _r.Advance(); return; }
            if (k is DartTokenKind.LBrace or DartTokenKind.RBrace) return;
            if (k is DartTokenKind.KwClass or DartTokenKind.KwMixin or DartTokenKind.KwEnum
                or DartTokenKind.KwAbstract or DartTokenKind.KwBase or DartTokenKind.KwSealed
                or DartTokenKind.KwFinal or DartTokenKind.KwTypedef) return;
            if (k == DartTokenKind.LParen && !_r.TrySkipBalanced((int)DartTokenKind.LParen, (int)DartTokenKind.RParen)) return;
            if (k == DartTokenKind.LBracket && !_r.TrySkipBalanced((int)DartTokenKind.LBracket, (int)DartTokenKind.RBracket)) return;
            if (k == DartTokenKind.LAngle && !TrySkipGenerics()) return;
            _r.Advance();
        }
    }

    private bool SkipTrash()
    {
        while (!_r.IsEof)
        {
            DartTokenKind k = (DartTokenKind)_r.Current.Kind;
            if (k is DartTokenKind.KwClass or DartTokenKind.KwMixin or DartTokenKind.KwEnum
                or DartTokenKind.KwAbstract or DartTokenKind.KwBase or DartTokenKind.KwSealed
                or DartTokenKind.KwFinal or DartTokenKind.KwTypedef
                or DartTokenKind.KwConst or DartTokenKind.KwFactory
                or DartTokenKind.At or DartTokenKind.LBrace
                or DartTokenKind.RBrace or DartTokenKind.Semicolon
                or DartTokenKind.KwImport or DartTokenKind.KwPart or DartTokenKind.KwExport)
                return true;
            _r.Advance();
        }
        return false;
    }

    private void CheckCaps(int start, int end)
    {
        if (_depth >= MaxDepth)
            _diagnostics.Add(new ParseDiagnostic("DART005", "Max depth exceeded.", new TextSpan(start, end - start), DiagnosticSeverity.Warning));
        if (_nodeCount >= MaxOutlineNodes)
            _diagnostics.Add(new ParseDiagnostic("DART006", "Max outline nodes exceeded.", new TextSpan(start, end - start), DiagnosticSeverity.Warning));
    }

    private static string KindString(DartTokenKind kind) => kind switch
    {
        DartTokenKind.KwClass => "class",
        DartTokenKind.KwMixin => "mixin",
        DartTokenKind.KwEnum => "enum",
        _ => "class",
    };

    private OutlineNode MakeOutline(string kind, string label, TextSpan span, int level, IReadOnlyList<OutlineNode>? children = null)
    {
        LinePositionSpan lp = _map.GetLinePositionSpan(span);
        LinePositionSpan nav = new(new LinePosition(lp.Start.Line, 1), new LinePosition(lp.End.Line, 1));
        return new OutlineNode(kind, label, span, nav, level, children);
    }
}
