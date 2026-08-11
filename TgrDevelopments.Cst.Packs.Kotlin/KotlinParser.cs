using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Kotlin;

internal sealed class KotlinParser
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

    public KotlinParser(string text, IReadOnlyList<Token> tokens, LineMap map, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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
            TryParsePackage(children, outlines);
            SkipImports();
            ParseItems(children, outlines, 1, false);
        }
        catch (OperationCanceledException)
        {
            _cancelled = true;
        }
        return children;
    }

    private void TryParsePackage(List<SyntaxNode> children, List<OutlineNode> outlines)
    {
        if (!_r.Check((int)KotlinTokenKind.KwPackage))
            return;
        int start = _r.Current.Start;
        _r.Advance();
        string? name = ScanDottedName();
        if (name is null)
            return;
        TextSpan nameSpan = _r.Peek(-1).Span;
        TextSpan span = new(start, _r.Peek(-1).End - start);
        children.Add(new SyntaxNode("package_decl", span, name, nameSpan));
        outlines.Add(MakeOutline("package", name, span, 1));
        _nodeCount++;
    }

    private void SkipImports()
    {
        while (_r.Check((int)KotlinTokenKind.KwImport))
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

            if (_r.Check((int)KotlinTokenKind.RBrace))
            {
                if (stopAtBrace)
                    return;
                _r.Advance();
                continue;
            }
            if (_r.Check((int)KotlinTokenKind.Semicolon))
            {
                _r.Advance();
                continue;
            }

            SkipModifiersAndAnnotations();

            if (_r.Check((int)KotlinTokenKind.KwClass) || _r.Check((int)KotlinTokenKind.KwInterface)
                || _r.Check((int)KotlinTokenKind.KwObject))
            {
                TryParseType(parentChildren, parentOutlines, baseLevel, insideType);
                continue;
            }

            if (_r.Check((int)KotlinTokenKind.KwEnum))
            {
                _r.Advance();
                if (_r.Check((int)KotlinTokenKind.KwClass))
                    _r.Advance();
                TryParseTypeBody(parentChildren, parentOutlines, baseLevel, "enum");
                continue;
            }

            if (_r.Check((int)KotlinTokenKind.KwFun))
            {
                TryParseFun(parentChildren, parentOutlines, baseLevel, insideType);
                continue;
            }

            if (_r.Check((int)KotlinTokenKind.KwVal) || _r.Check((int)KotlinTokenKind.KwVar))
            {
                _r.Advance();
                if (_r.Check((int)KotlinTokenKind.Identifier))
                    _r.Advance();
                if (_r.Check((int)KotlinTokenKind.Colon))
                {
                    _r.Advance();
                    SkipType();
                }
                SkipToSemicolonOrBrace();
                continue;
            }

            if (_r.Check((int)KotlinTokenKind.KwTypeAlias))
            {
                _r.Advance();
                SkipToSemicolon();
                continue;
            }

            if (_r.Check((int)KotlinTokenKind.LBrace))
            {
                _r.TrySkipBalanced((int)KotlinTokenKind.LBrace, (int)KotlinTokenKind.RBrace);
                continue;
            }

            if (!SkipTrash())
                return;
        }
    }

    private void SkipModifiersAndAnnotations()
    {
        while (!_r.IsEof)
        {
            if (_r.Check((int)KotlinTokenKind.KwPublic) || _r.Check((int)KotlinTokenKind.KwPrivate)
                || _r.Check((int)KotlinTokenKind.KwProtected) || _r.Check((int)KotlinTokenKind.KwInternal)
                || _r.Check((int)KotlinTokenKind.KwOpen) || _r.Check((int)KotlinTokenKind.KwSealed)
                || _r.Check((int)KotlinTokenKind.KwAbstract) || _r.Check((int)KotlinTokenKind.KwData)
                || _r.Check((int)KotlinTokenKind.KwOverride) || _r.Check((int)KotlinTokenKind.KwInline)
                || _r.Check((int)KotlinTokenKind.KwSuspend) || _r.Check((int)KotlinTokenKind.KwOperator)
                || _r.Check((int)KotlinTokenKind.KwInfix) || _r.Check((int)KotlinTokenKind.KwTailRec)
                || _r.Check((int)KotlinTokenKind.KwExternal) || _r.Check((int)KotlinTokenKind.KwAnnotation)
                || _r.Check((int)KotlinTokenKind.KwInner) || _r.Check((int)KotlinTokenKind.KwLateInit)
                || _r.Check((int)KotlinTokenKind.KwCompanion))
            {
                _r.Advance();
                continue;
            }
            if (_r.Check((int)KotlinTokenKind.At))
            {
                _r.Advance();
                if (_r.Check((int)KotlinTokenKind.Identifier))
                {
                    _r.Advance();
                    if (_r.Check((int)KotlinTokenKind.LParen))
                        _r.TrySkipBalanced((int)KotlinTokenKind.LParen, (int)KotlinTokenKind.RParen);
                }
                continue;
            }
            break;
        }
    }

    private bool TryParseType(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel, bool insideType)
    {
        KotlinTokenKind tkind = (KotlinTokenKind)_r.Current.Kind;
        _r.Advance();
        return TryParseTypeBody(children, outlines, baseLevel, KindString(tkind));
    }

    private bool TryParseTypeBody(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel, string ok)
    {
        int start = _r.Current.Start;

        if (_r.Check((int)KotlinTokenKind.At))
        {
            _r.Advance();
            if (_r.Check((int)KotlinTokenKind.Identifier))
                _r.Advance();
            if (_r.Check((int)KotlinTokenKind.LParen))
                _r.TrySkipBalanced((int)KotlinTokenKind.LParen, (int)KotlinTokenKind.RParen);
        }

        if (!_r.Check((int)KotlinTokenKind.Identifier))
        {
            SkipToSemicolonOrBrace();
            return true;
        }
        string name = _r.Current.Text ?? "type";
        TextSpan nameSpan = _r.Current.Span;
        _r.Advance();

        if (_r.Check((int)KotlinTokenKind.LAngle))
            TrySkipGenerics();

        if (_r.Check((int)KotlinTokenKind.Colon))
        {
            _r.Advance();
            SkipTypeList();
        }

        if (_r.Check((int)KotlinTokenKind.KwBy))
        {
            _r.Advance();
            SkipType();
        }

        if (!_r.Check((int)KotlinTokenKind.LBrace))
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
            _diagnostics.Add(new ParseDiagnostic("KOTLIN005", "Max depth exceeded.", new TextSpan(start, _r.Current.End - start), DiagnosticSeverity.Warning));
            _r.TrySkipBalanced((int)KotlinTokenKind.LBrace, (int)KotlinTokenKind.RBrace);
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
        if (_r.Check((int)KotlinTokenKind.RBrace))
        {
            bodyEnd = _r.Current.End;
            _r.Advance();
        }
        else
        {
            bodyEnd = _r.Peek(-1).End;
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("KOTLIN001", "Unclosed type body.", new TextSpan(bodyOpen, 0), DiagnosticSeverity.Error));
        }

        TextSpan span = new(start, bodyEnd - start);
        children.Add(new SyntaxNode(ok + "_declaration", span, name, nameSpan, innerChildren.Count > 0 ? innerChildren : null));
        outlines.Add(MakeOutline(ok, name, span, baseLevel, innerOutlines.Count > 0 ? innerOutlines : null));
        _nodeCount++;
        _depth--;
        return true;
    }

    private bool TryParseFun(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel, bool insideType)
    {
        int start = _r.Current.Start;
        _r.Advance();

        if (_r.Check((int)KotlinTokenKind.LAngle))
            TrySkipGenerics();

        if (!_r.Check((int)KotlinTokenKind.Identifier))
        {
            SkipToSemicolonOrBrace();
            return true;
        }
        string name = _r.Current.Text ?? "fun";
        TextSpan nameSpan = _r.Current.Span;

        if (_r.Check((int)KotlinTokenKind.Period))
        {
            _r.Advance();
            if (_r.Check((int)KotlinTokenKind.Identifier))
            {
                name = _r.Current.Text ?? name;
                nameSpan = _r.Current.Span;
            }
        }
        _r.Advance();

        if (_r.Check((int)KotlinTokenKind.LParen))
        {
            if (!_r.TrySkipBalanced((int)KotlinTokenKind.LParen, (int)KotlinTokenKind.RParen))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("KOTLIN001", "Unclosed parameter list.", _r.Current.Span, DiagnosticSeverity.Error));
                SkipToSemicolonOrBrace();
                int e1 = _r.Peek(-1).End;
                CheckCaps(start, e1);
                TextSpan s1 = new(start, e1 - start);
                string nk1 = insideType ? "method_declaration" : "function_declaration";
                string ok1 = insideType ? "method" : "fun";
                children.Add(new SyntaxNode(nk1, s1, name, nameSpan));
                outlines.Add(MakeOutline(ok1, name, s1, baseLevel));
                _nodeCount++;
                return true;
            }
        }

        if (_r.Check((int)KotlinTokenKind.Colon))
        {
            _r.Advance();
            SkipType();
        }

        if (_r.Check((int)KotlinTokenKind.KwWhere))
        {
            _r.Advance();
            SkipTypeList();
        }

        if (!_r.Check((int)KotlinTokenKind.LBrace))
        {
            SkipToSemicolonOrBrace();
            int e2 = _r.Peek(-1).End;
            CheckCaps(start, e2);
            TextSpan s2 = new(start, e2 - start);
            string nk2 = insideType ? "method_declaration" : "function_declaration";
            string ok2 = insideType ? "method" : "fun";
            children.Add(new SyntaxNode(nk2, s2, name, nameSpan));
            outlines.Add(MakeOutline(ok2, name, s2, baseLevel));
            _nodeCount++;
            return true;
        }

        if (!_r.TrySkipBalanced((int)KotlinTokenKind.LBrace, (int)KotlinTokenKind.RBrace))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("KOTLIN001", "Unclosed function body.", _r.Current.Span, DiagnosticSeverity.Error));
        }
        int end = _r.Peek(-1).End;
        CheckCaps(start, end);
        TextSpan span = new(start, end - start);
        string nk = insideType ? "method_declaration" : "function_declaration";
        string ok = insideType ? "method" : "fun";
        children.Add(new SyntaxNode(nk, span, name, nameSpan));
        outlines.Add(MakeOutline(ok, name, span, baseLevel));
        _nodeCount++;
        return true;
    }

    private string? ScanDottedName()
    {
        if (!_r.Check((int)KotlinTokenKind.Identifier))
            return null;
        string result = _r.Current.Text ?? "";
        _r.Advance();
        while (_r.Check((int)KotlinTokenKind.Period))
        {
            _r.Advance();
            if (!_r.Check((int)KotlinTokenKind.Identifier))
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
            if (_r.Check((int)KotlinTokenKind.Semicolon) || _r.Check((int)KotlinTokenKind.LBrace)
                || _r.Check((int)KotlinTokenKind.RBrace) || _r.Check((int)KotlinTokenKind.Equal)
                || _r.Check((int)KotlinTokenKind.KwBy))
                return;
            if (_r.Check((int)KotlinTokenKind.LAngle))
                TrySkipGenerics();
            else if (_r.Check((int)KotlinTokenKind.LParen))
                _r.TrySkipBalanced((int)KotlinTokenKind.LParen, (int)KotlinTokenKind.RParen);
            else if (_r.Check((int)KotlinTokenKind.LBracket))
                _r.TrySkipBalanced((int)KotlinTokenKind.LBracket, (int)KotlinTokenKind.RBracket);
            else
                _r.Advance();
        }
    }

    private void SkipTypeList()
    {
        while (!_r.IsEof)
        {
            if (_r.Check((int)KotlinTokenKind.Semicolon) || _r.Check((int)KotlinTokenKind.LBrace)
                || _r.Check((int)KotlinTokenKind.RBrace) || _r.Check((int)KotlinTokenKind.Equal)
                || _r.Check((int)KotlinTokenKind.LBrace))
                return;
            if (_r.Check((int)KotlinTokenKind.LAngle))
                TrySkipGenerics();
            else if (_r.Check((int)KotlinTokenKind.Comma))
                _r.Advance();
            else
                _r.Advance();
        }
    }

    private bool TrySkipGenerics()
    {
        if (!_r.TrySkipBalanced((int)KotlinTokenKind.LAngle, (int)KotlinTokenKind.RAngle))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("KOTLIN001", "Unclosed generics.", _r.Current.Span, DiagnosticSeverity.Error));
            return false;
        }
        return true;
    }

    private void SkipToSemicolon()
    {
        while (!_r.IsEof)
        {
            if (_r.Check((int)KotlinTokenKind.Semicolon))
            {
                _r.Advance();
                return;
            }
            if (_r.Check((int)KotlinTokenKind.LBrace) || _r.Check((int)KotlinTokenKind.RBrace))
                return;
            KotlinTokenKind k = (KotlinTokenKind)_r.Current.Kind;
            if (k is KotlinTokenKind.KwClass or KotlinTokenKind.KwInterface or KotlinTokenKind.KwObject
                or KotlinTokenKind.KwEnum or KotlinTokenKind.KwFun or KotlinTokenKind.KwVal
                or KotlinTokenKind.KwVar or KotlinTokenKind.KwTypeAlias
                or KotlinTokenKind.KwPackage or KotlinTokenKind.KwAnnotation
                or KotlinTokenKind.KwPublic or KotlinTokenKind.KwPrivate or KotlinTokenKind.KwProtected
                or KotlinTokenKind.KwInternal)
                return;
            if (k == KotlinTokenKind.LParen && !_r.TrySkipBalanced((int)KotlinTokenKind.LParen, (int)KotlinTokenKind.RParen))
                return;
            if (k == KotlinTokenKind.LBracket && !_r.TrySkipBalanced((int)KotlinTokenKind.LBracket, (int)KotlinTokenKind.RBracket))
                return;
            if (k == KotlinTokenKind.LAngle && !TrySkipGenerics())
                return;
            _r.Advance();
        }
    }

    private void SkipToSemicolonOrBrace()
    {
        while (!_r.IsEof)
        {
            if (_r.Check((int)KotlinTokenKind.Semicolon))
            {
                _r.Advance();
                return;
            }
            if (_r.Check((int)KotlinTokenKind.LBrace) || _r.Check((int)KotlinTokenKind.RBrace))
                return;
            KotlinTokenKind k = (KotlinTokenKind)_r.Current.Kind;
            if (k is KotlinTokenKind.KwClass or KotlinTokenKind.KwInterface or KotlinTokenKind.KwObject
                or KotlinTokenKind.KwEnum or KotlinTokenKind.KwFun or KotlinTokenKind.KwVal
                or KotlinTokenKind.KwVar or KotlinTokenKind.KwTypeAlias
                or KotlinTokenKind.KwPackage or KotlinTokenKind.KwImport
                or KotlinTokenKind.KwPublic or KotlinTokenKind.KwPrivate or KotlinTokenKind.KwProtected
                or KotlinTokenKind.KwInternal or KotlinTokenKind.KwAnnotation
                or KotlinTokenKind.KwOpen or KotlinTokenKind.KwData or KotlinTokenKind.KwSealed
                or KotlinTokenKind.KwAbstract or KotlinTokenKind.KwOverride)
                return;
            if (k == KotlinTokenKind.LParen && !_r.TrySkipBalanced((int)KotlinTokenKind.LParen, (int)KotlinTokenKind.RParen))
                return;
            if (k == KotlinTokenKind.LBracket && !_r.TrySkipBalanced((int)KotlinTokenKind.LBracket, (int)KotlinTokenKind.RBracket))
                return;
            if (k == KotlinTokenKind.LAngle && !TrySkipGenerics())
                return;
            _r.Advance();
        }
    }

    private bool SkipTrash()
    {
        while (!_r.IsEof)
        {
            KotlinTokenKind k = (KotlinTokenKind)_r.Current.Kind;
            if (k is KotlinTokenKind.KwClass or KotlinTokenKind.KwInterface or KotlinTokenKind.KwObject
                or KotlinTokenKind.KwEnum or KotlinTokenKind.KwFun or KotlinTokenKind.KwVal
                or KotlinTokenKind.KwVar or KotlinTokenKind.KwTypeAlias
                or KotlinTokenKind.KwPublic or KotlinTokenKind.KwPrivate or KotlinTokenKind.KwProtected
                or KotlinTokenKind.KwInternal or KotlinTokenKind.At
                or KotlinTokenKind.LBrace or KotlinTokenKind.RBrace or KotlinTokenKind.Semicolon)
                return true;
            _r.Advance();
        }
        return false;
    }

    private void CheckCaps(int start, int end)
    {
        if (_depth >= MaxDepth)
            _diagnostics.Add(new ParseDiagnostic("KOTLIN005", "Max depth exceeded.", new TextSpan(start, end - start), DiagnosticSeverity.Warning));
        if (_nodeCount >= MaxOutlineNodes)
            _diagnostics.Add(new ParseDiagnostic("KOTLIN006", "Max outline nodes exceeded.", new TextSpan(start, end - start), DiagnosticSeverity.Warning));
    }

    private static string KindString(KotlinTokenKind kind) => kind switch
    {
        KotlinTokenKind.KwClass => "class",
        KotlinTokenKind.KwInterface => "interface",
        KotlinTokenKind.KwObject => "object",
        _ => "class",
    };

    private OutlineNode MakeOutline(string kind, string label, TextSpan span, int level, IReadOnlyList<OutlineNode>? children = null)
    {
        LinePositionSpan lp = _map.GetLinePositionSpan(span);
        LinePositionSpan nav = new(new LinePosition(lp.Start.Line, 1), new LinePosition(lp.End.Line, 1));
        return new OutlineNode(kind, label, span, nav, level, children);
    }
}
