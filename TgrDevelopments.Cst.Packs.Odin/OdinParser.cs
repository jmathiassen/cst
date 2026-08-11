using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Odin;

internal sealed class OdinParser
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

    public OdinParser(string text, IReadOnlyList<Token> tokens, LineMap map, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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
            if (TryParsePackage(children, outlines))
                ParseItems(children, outlines);
            else
                ParseItems(children, outlines);
        }
        catch (OperationCanceledException)
        {
            _cancelled = true;
        }
        return children;
    }

    private void ParseItems(List<SyntaxNode> children, List<OutlineNode> outlines)
    {
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();

            if (_r.Check((int)OdinTokenKind.RBrace))
                return;

            if (_r.Check((int)OdinTokenKind.Hash))
            {
                _r.Advance();
                SkipToSemicolonOrBrace();
                continue;
            }

            if (_r.Check((int)OdinTokenKind.KwImport) || _r.Check((int)OdinTokenKind.KwForeign))
            {
                SkipImportOrForeign();
                continue;
            }

            if (_r.Check((int)OdinTokenKind.Identifier) && _r.Peek(1).Kind == (int)OdinTokenKind.ColonColon)
            {
                TryParseNamedDecl(children, outlines);
                continue;
            }

            if (_r.Check((int)OdinTokenKind.Identifier) && _r.Peek(1).Kind == (int)OdinTokenKind.Colon)
            {
                TryParseVarDecl(children, outlines);
                continue;
            }

            if (!SkipTrash())
                return;
        }
    }

    private bool TryParsePackage(List<SyntaxNode> children, List<OutlineNode> outlines)
    {
        if (!_r.Check((int)OdinTokenKind.KwPackage))
            return false;
        int start = _r.Current.Start;
        _r.Advance();
        if (!_r.Check((int)OdinTokenKind.Identifier))
            return true;
        string name = _r.Current.Text ?? "package";
        TextSpan nameSpan = _r.Current.Span;
        _r.Advance();

        TextSpan span = new(start, _r.Current.End - start);
        children.Add(new SyntaxNode("package_clause", span, name, nameSpan));
        outlines.Add(MakeOutline("package", name, span, 1));
        _nodeCount++;
        return true;
    }

    private bool TryParseNamedDecl(List<SyntaxNode> children, List<OutlineNode> outlines)
    {
        int start = _r.Current.Start;
        string name = _r.Current.Text ?? "decl";
        TextSpan nameSpan = _r.Current.Span;
        _r.Advance();
        _r.Advance();

        if (_r.Check((int)OdinTokenKind.KwProc))
        {
            ParseProcBody(children, outlines, name, nameSpan, start);
            return true;
        }
        if (_r.Check((int)OdinTokenKind.KwStruct) || _r.Check((int)OdinTokenKind.KwEnum) || _r.Check((int)OdinTokenKind.KwUnion))
        {
            OdinTokenKind kind = (OdinTokenKind)_r.Current.Kind;
            _r.Advance();
            string nodeKind = kind == OdinTokenKind.KwStruct ? "struct_declaration"
                : kind == OdinTokenKind.KwEnum ? "enum_declaration" : "union_declaration";
            string outlineKind = kind == OdinTokenKind.KwStruct ? "struct"
                : kind == OdinTokenKind.KwEnum ? "enum" : "union";

            SkipToOpeningBrace();
            if (_r.Check((int)OdinTokenKind.LBrace))
            {
                if (!_r.TrySkipBalanced((int)OdinTokenKind.LBrace, (int)OdinTokenKind.RBrace))
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic("ODIN001", "Unclosed type body.", _r.Current.Span, DiagnosticSeverity.Error));
                }
            }
            int end = _r.Peek(-1).End;
            CheckCaps(start, end);
            TextSpan span = new(start, end - start);
            children.Add(new SyntaxNode(nodeKind, span, name, nameSpan));
            outlines.Add(MakeOutline(outlineKind, name, span, 1));
            _nodeCount++;
            return true;
        }

        SkipToSemicolonOrBrace();
        int cend = _r.Peek(-1).End;
        CheckCaps(start, cend);
        TextSpan cspan = new(start, cend - start);
        children.Add(new SyntaxNode("const_declaration", cspan, name, nameSpan));
        outlines.Add(MakeOutline("const", name, cspan, 1));
        _nodeCount++;
        return true;
    }

    private bool TryParseVarDecl(List<SyntaxNode> children, List<OutlineNode> outlines)
    {
        int start = _r.Current.Start;
        string name = _r.Current.Text ?? "var";
        TextSpan nameSpan = _r.Current.Span;
        _r.Advance();
        _r.Advance();
        SkipToSemicolonOrBrace();
        int end = _r.Peek(-1).End;
        CheckCaps(start, end);
        TextSpan span = new(start, end - start);
        children.Add(new SyntaxNode("const_declaration", span, name, nameSpan));
        outlines.Add(MakeOutline("const", name, span, 1));
        _nodeCount++;
        return true;
    }

    private void ParseProcBody(List<SyntaxNode> children, List<OutlineNode> outlines, string name, TextSpan nameSpan, int start)
    {
        _r.Advance();

        if (_r.Check((int)OdinTokenKind.StringLiteral))
            _r.Advance();

        SkipGenerics();

        if (_r.Check((int)OdinTokenKind.LParen))
        {
            if (!_r.TrySkipBalanced((int)OdinTokenKind.LParen, (int)OdinTokenKind.RParen))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("ODIN001", "Unclosed parameter list.", _r.Current.Span, DiagnosticSeverity.Error));
                SkipToSemicolonOrBrace();
                AddProcNode(children, outlines, name, nameSpan, start);
                return;
            }
        }

        if (_r.Check((int)OdinTokenKind.Arrow))
        {
            _r.Advance();
            SkipType();
        }

        if (_r.Check((int)OdinTokenKind.Hash))
        {
            _r.Advance();
            SkipToSemicolonOrBrace();
        }

        if (!_r.Check((int)OdinTokenKind.LBrace))
        {
            AddProcNode(children, outlines, name, nameSpan, start);
            return;
        }
        if (!_r.TrySkipBalanced((int)OdinTokenKind.LBrace, (int)OdinTokenKind.RBrace))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("ODIN001", "Unclosed proc body.", _r.Current.Span, DiagnosticSeverity.Error));
        }
        AddProcNode(children, outlines, name, nameSpan, start);
    }

    private void AddProcNode(List<SyntaxNode> children, List<OutlineNode> outlines, string name, TextSpan nameSpan, int start)
    {
        int end = _r.Peek(-1).End;
        CheckCaps(start, end);
        TextSpan span = new(start, end - start);
        children.Add(new SyntaxNode("proc_declaration", span, name, nameSpan));
        outlines.Add(MakeOutline("proc", name, span, 1));
        _nodeCount++;
    }

    private void SkipImportOrForeign()
    {
        _r.Advance();
        if (_r.Check((int)OdinTokenKind.KwImport))
            _r.Advance();
        SkipToSemicolonOrBrace();
        if (_r.Check((int)OdinTokenKind.LBrace))
        {
            if (!_r.TrySkipBalanced((int)OdinTokenKind.LBrace, (int)OdinTokenKind.RBrace))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("ODIN001", "Unclosed foreign block.", _r.Current.Span, DiagnosticSeverity.Error));
            }
        }
    }

    private void SkipGenerics()
    {
        if (_r.Check((int)OdinTokenKind.LParen) || _r.Check((int)OdinTokenKind.LBracket))
            return;
    }

    private void SkipType()
    {
        while (!_r.IsEof)
        {
            OdinTokenKind k = (OdinTokenKind)_r.Current.Kind;
            if (k is OdinTokenKind.Semicolon or OdinTokenKind.LBrace or OdinTokenKind.RBrace)
                return;
            if (k == OdinTokenKind.LParen && !_r.TrySkipBalanced((int)OdinTokenKind.LParen, (int)OdinTokenKind.RParen))
                return;
            _r.Advance();
        }
    }

    private void SkipToOpeningBrace()
    {
        while (!_r.IsEof)
        {
            OdinTokenKind k = (OdinTokenKind)_r.Current.Kind;
            if (k is OdinTokenKind.LBrace or OdinTokenKind.Semicolon)
                return;
            if (k == OdinTokenKind.Identifier)
            {
                int nk = _r.Peek(1).Kind;
                if (nk == (int)OdinTokenKind.ColonColon || nk == (int)OdinTokenKind.Colon)
                    return;
            }
            if (k is OdinTokenKind.KwPackage or OdinTokenKind.KwImport or OdinTokenKind.KwForeign or OdinTokenKind.Hash)
                return;
            if (k == OdinTokenKind.LParen && !_r.TrySkipBalanced((int)OdinTokenKind.LParen, (int)OdinTokenKind.RParen))
                return;
            if (k == OdinTokenKind.LBracket && !_r.TrySkipBalanced((int)OdinTokenKind.LBracket, (int)OdinTokenKind.RBracket))
                return;
            _r.Advance();
        }
    }

    private void SkipToSemicolonOrBrace()
    {
        while (!_r.IsEof)
        {
            OdinTokenKind k = (OdinTokenKind)_r.Current.Kind;
            if (k is OdinTokenKind.Semicolon)
            {
                _r.Advance();
                return;
            }
            if (k is OdinTokenKind.LBrace or OdinTokenKind.RBrace)
                return;
            if (k == OdinTokenKind.Identifier)
            {
                int nk = _r.Peek(1).Kind;
                if (nk == (int)OdinTokenKind.ColonColon || nk == (int)OdinTokenKind.Colon)
                    return;
            }
            if (k is OdinTokenKind.KwPackage or OdinTokenKind.KwImport or OdinTokenKind.KwForeign or OdinTokenKind.Hash)
                return;
            if (k == OdinTokenKind.LParen && !_r.TrySkipBalanced((int)OdinTokenKind.LParen, (int)OdinTokenKind.RParen))
                return;
            if (k == OdinTokenKind.LBracket && !_r.TrySkipBalanced((int)OdinTokenKind.LBracket, (int)OdinTokenKind.RBracket))
                return;
            _r.Advance();
        }
    }

    private bool SkipTrash()
    {
        while (!_r.IsEof)
        {
            OdinTokenKind k = (OdinTokenKind)_r.Current.Kind;
            if (_r.Check((int)OdinTokenKind.Identifier) && (_r.Peek(1).Kind == (int)OdinTokenKind.ColonColon || _r.Peek(1).Kind == (int)OdinTokenKind.Colon))
                return true;
            if (k is OdinTokenKind.Hash or OdinTokenKind.KwImport or OdinTokenKind.KwForeign
                or OdinTokenKind.LBrace or OdinTokenKind.RBrace or OdinTokenKind.Semicolon)
                return true;
            _r.Advance();
        }
        return false;
    }

    private void CheckCaps(int start, int end)
    {
        if (_depth >= MaxDepth)
            _diagnostics.Add(new ParseDiagnostic("ODIN005", "Max depth exceeded.", new TextSpan(start, end - start), DiagnosticSeverity.Warning));
        if (_nodeCount >= MaxOutlineNodes)
            _diagnostics.Add(new ParseDiagnostic("ODIN006", "Max outline nodes exceeded.", new TextSpan(start, end - start), DiagnosticSeverity.Warning));
    }

    private OutlineNode MakeOutline(string kind, string label, TextSpan span, int level)
    {
        LinePositionSpan lp = _map.GetLinePositionSpan(span);
        LinePositionSpan nav = new(new LinePosition(lp.Start.Line, 1), new LinePosition(lp.End.Line, 1));
        return new OutlineNode(kind, label, span, nav, level);
    }
}
