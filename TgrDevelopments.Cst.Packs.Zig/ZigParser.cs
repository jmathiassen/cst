using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Zig;

internal sealed class ZigParser
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

    public ZigParser(string text, IReadOnlyList<Token> tokens, LineMap map, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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

    private void ParseItems(List<SyntaxNode> parentChildren, List<OutlineNode> parentOutlines, int baseLevel, bool insideContainer, bool stopAtBrace = false)
    {
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();
            if (_r.Check((int)ZigTokenKind.RBrace))
            {
                if (stopAtBrace)
                    return;
                _r.Advance();
                continue;
            }

            SkipModifiers();

            if (_r.Check((int)ZigTokenKind.KwTest))
            {
                TryParseTest(parentChildren, parentOutlines, baseLevel);
                continue;
            }

            if (_r.Check((int)ZigTokenKind.KwComptime) || _r.Check((int)ZigTokenKind.KwUsingNamespace))
            {
                _r.Advance();
                SkipToSemicolonOrBrace();
                continue;
            }

            if (_r.Check((int)ZigTokenKind.KwFn))
            {
                string ok = insideContainer ? "method" : "fn";
                string nk = insideContainer ? "method_item" : "fn_decl";
                TryParseFn(parentChildren, parentOutlines, baseLevel, ok, nk);
                continue;
            }

            if (_r.Check((int)ZigTokenKind.KwConst) || _r.Check((int)ZigTokenKind.KwVar))
            {
                TryParseConstOrVar(parentChildren, parentOutlines, baseLevel);
                continue;
            }

            if (_r.Check((int)ZigTokenKind.Semicolon))
            {
                _r.Advance();
                continue;
            }

            if (!SkipTrash())
                return;
        }
    }

    private void SkipModifiers()
    {
        if (_r.Check((int)ZigTokenKind.KwPub) || _r.Check((int)ZigTokenKind.KwExport))
            _r.Advance();
    }

    private bool TryParseFn(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel, string outlineKind, string nodeKind)
    {
        int start = _r.Current.Start;
        _r.Advance();

        if (!_r.Check((int)ZigTokenKind.Identifier))
        {
            SkipToSemicolonOrBrace();
            return true;
        }
        string name = _r.Current.Text ?? "fn";
        TextSpan nameSpan = _r.Current.Span;
        _r.Advance();

        if (_r.Check((int)ZigTokenKind.LParen))
        {
            if (!_r.TrySkipBalanced((int)ZigTokenKind.LParen, (int)ZigTokenKind.RParen))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("ZIG001", "Unclosed parameter list.", _r.Current.Span, DiagnosticSeverity.Error));
                SkipToSemicolonOrBrace();
                int errEnd = _r.Peek(-1).End;
                CheckCaps(start, errEnd);
                TextSpan sp = new(start, errEnd - start);
                children.Add(new SyntaxNode(nodeKind, sp, name, nameSpan));
                outlines.Add(MakeOutline(outlineKind, name, sp, baseLevel));
                _nodeCount++;
                return true;
            }
        }

        if (_r.Check((int)ZigTokenKind.Exclamation))
            _r.Advance();

        SkipToSemicolonOrBrace();
        if (!_r.TrySkipBalanced((int)ZigTokenKind.LBrace, (int)ZigTokenKind.RBrace))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("ZIG001", "Unclosed fn body.", _r.Current.Span, DiagnosticSeverity.Error));
        }

        int end = _r.Peek(-1).End;
        CheckCaps(start, end);
        TextSpan span = new(start, end - start);
        children.Add(new SyntaxNode(nodeKind, span, name, nameSpan));
        outlines.Add(MakeOutline(outlineKind, name, span, baseLevel));
        _nodeCount++;
        return true;
    }

    private bool TryParseTest(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel)
    {
        int start = _r.Current.Start;
        _r.Advance();
        string name = "test";
        TextSpan nameSpan = default;
        if (_r.Check((int)ZigTokenKind.StringLiteral))
        {
            name = _r.Current.Text ?? "test";
            nameSpan = _r.Current.Span;
            _r.Advance();
        }
        else if (_r.Check((int)ZigTokenKind.Identifier))
        {
            name = _r.Current.Text ?? "test";
            nameSpan = _r.Current.Span;
            _r.Advance();
        }

        if (_r.Check((int)ZigTokenKind.LBrace))
        {
            if (!_r.TrySkipBalanced((int)ZigTokenKind.LBrace, (int)ZigTokenKind.RBrace))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("ZIG001", "Unclosed test body.", _r.Current.Span, DiagnosticSeverity.Error));
            }
        }
        else
        {
            SkipToSemicolonOrBrace();
        }
        int end = _r.Peek(-1).End;
        CheckCaps(start, end);
        TextSpan span = new(start, end - start);
        children.Add(new SyntaxNode("test_decl", span, name, nameSpan));
        outlines.Add(MakeOutline("test", name, span, baseLevel));
        _nodeCount++;
        return true;
    }

    private bool TryParseConstOrVar(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel)
    {
        int start = _r.Current.Start;
        bool isConst = _r.Check((int)ZigTokenKind.KwConst);
        _r.Advance();

        if (!_r.Check((int)ZigTokenKind.Identifier))
        {
            SkipToSemicolonOrBrace();
            return true;
        }
        string name = _r.Current.Text ?? "item";
        TextSpan nameSpan = _r.Current.Span;
        _r.Advance();

        if (_r.Check((int)ZigTokenKind.Colon))
        {
            _r.Advance();
            SkipType();
        }

        if (!_r.Check((int)ZigTokenKind.Equal))
        {
            SkipToSemicolonOrBrace();
            int end2 = _r.Peek(-1).End;
            CheckCaps(start, end2);
            TextSpan sp2 = new(start, end2 - start);
            children.Add(new SyntaxNode("var_decl", sp2, name, nameSpan));
            outlines.Add(MakeOutline(isConst ? "const" : "var", name, sp2, baseLevel));
            _nodeCount++;
            return true;
        }
        _r.Advance();

        if (_r.Check((int)ZigTokenKind.KwStruct) || _r.Check((int)ZigTokenKind.KwEnum) || _r.Check((int)ZigTokenKind.KwUnion))
        {
            ZigTokenKind ckind = (ZigTokenKind)_r.Current.Kind;
            _r.Advance();
            string ok = ckind == ZigTokenKind.KwStruct ? "struct"
                : ckind == ZigTokenKind.KwEnum ? "enum" : "union";
            string nk = "container_decl";

            if (baseLevel + 1 > MaxDepth)
            {
                _diagnostics.Add(new ParseDiagnostic("ZIG005", "Max depth exceeded.", new TextSpan(start, _r.Current.End - start), DiagnosticSeverity.Warning));
                SkipToSemicolonOrBrace();
                int e2 = _r.Peek(-1).End;
                TextSpan s2 = new(start, e2 - start);
                children.Add(new SyntaxNode(nk, s2, name, nameSpan));
                outlines.Add(MakeOutline(ok, name, s2, baseLevel));
                _nodeCount++;
                return true;
            }

            if (!_r.Check((int)ZigTokenKind.LBrace))
            {
                SkipToSemicolonOrBrace();
                int e3 = _r.Peek(-1).End;
                TextSpan s3 = new(start, e3 - start);
                children.Add(new SyntaxNode(nk, s3, name, nameSpan));
                outlines.Add(MakeOutline(ok, name, s3, baseLevel));
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
            if (_r.Check((int)ZigTokenKind.RBrace))
            {
                bodyEnd = _r.Current.End;
                _r.Advance();
            }
            else
            {
                bodyEnd = _r.Peek(-1).End;
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("ZIG001", "Unclosed container body.", new TextSpan(bodyOpen, 0), DiagnosticSeverity.Error));
            }

            TextSpan span = new(start, bodyEnd - start);
            children.Add(new SyntaxNode(nk, span, name, nameSpan, innerChildren.Count > 0 ? innerChildren : null));
            outlines.Add(MakeOutline(ok, name, span, baseLevel, innerOutlines.Count > 0 ? innerOutlines : null));
            _nodeCount++;
            _depth--;
            return true;
        }

        if (_r.Check((int)ZigTokenKind.LBrace))
        {
            if (!_r.TrySkipBalanced((int)ZigTokenKind.LBrace, (int)ZigTokenKind.RBrace))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("ZIG001", "Unclosed init body.", _r.Current.Span, DiagnosticSeverity.Error));
            }
        }
        else
        {
            SkipToSemicolonOrBrace();
        }
        int end = _r.Peek(-1).End;
        CheckCaps(start, end);
        TextSpan sp = new(start, end - start);
        children.Add(new SyntaxNode("var_decl", sp, name, nameSpan));
        outlines.Add(MakeOutline(isConst ? "const" : "var", name, sp, baseLevel));
        _nodeCount++;
        return true;
    }

    private void SkipType()
    {
        while (!_r.IsEof)
        {
            ZigTokenKind k = (ZigTokenKind)_r.Current.Kind;
            if (k is ZigTokenKind.Semicolon or ZigTokenKind.LBrace or ZigTokenKind.RBrace or ZigTokenKind.Equal)
                return;
            if (k == ZigTokenKind.LParen && !_r.TrySkipBalanced((int)ZigTokenKind.LParen, (int)ZigTokenKind.RParen))
                return;
            if (k == ZigTokenKind.LBracket && !_r.TrySkipBalanced((int)ZigTokenKind.LBracket, (int)ZigTokenKind.RBracket))
                return;
            _r.Advance();
        }
    }

    private void SkipToSemicolonOrBrace()
    {
        while (!_r.IsEof)
        {
            ZigTokenKind k = (ZigTokenKind)_r.Current.Kind;
            if (k is ZigTokenKind.Semicolon)
            {
                _r.Advance();
                return;
            }
            if (k is ZigTokenKind.LBrace or ZigTokenKind.RBrace)
                return;
            if (k == ZigTokenKind.LParen && !_r.TrySkipBalanced((int)ZigTokenKind.LParen, (int)ZigTokenKind.RParen))
                return;
            if (k == ZigTokenKind.LBracket && !_r.TrySkipBalanced((int)ZigTokenKind.LBracket, (int)ZigTokenKind.RBracket))
                return;
            _r.Advance();
        }
    }

    private bool SkipTrash()
    {
        while (!_r.IsEof)
        {
            ZigTokenKind k = (ZigTokenKind)_r.Current.Kind;
            if (k is ZigTokenKind.KwFn or ZigTokenKind.KwConst or ZigTokenKind.KwVar
                or ZigTokenKind.KwTest or ZigTokenKind.KwComptime or ZigTokenKind.KwUsingNamespace
                or ZigTokenKind.KwPub or ZigTokenKind.KwExport
                or ZigTokenKind.LBrace or ZigTokenKind.RBrace or ZigTokenKind.Semicolon)
                return true;
            _r.Advance();
        }
        return false;
    }

    private void CheckCaps(int start, int end)
    {
        if (_depth >= MaxDepth)
            _diagnostics.Add(new ParseDiagnostic("ZIG005", "Max depth exceeded.", new TextSpan(start, end - start), DiagnosticSeverity.Warning));
        if (_nodeCount >= MaxOutlineNodes)
            _diagnostics.Add(new ParseDiagnostic("ZIG006", "Max outline nodes exceeded.", new TextSpan(start, end - start), DiagnosticSeverity.Warning));
    }

    private OutlineNode MakeOutline(string kind, string label, TextSpan span, int level, IReadOnlyList<OutlineNode>? children = null)
    {
        LinePositionSpan lp = _map.GetLinePositionSpan(span);
        LinePositionSpan nav = new(new LinePosition(lp.Start.Line, 1), new LinePosition(lp.End.Line, 1));
        return new OutlineNode(kind, label, span, nav, level, children);
    }
}
