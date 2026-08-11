using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.C;

/// <summary>RD parser for C top-level declarations: functions/prototypes, struct/union/enum, typedefs.</summary>
internal sealed class CParser
{
    private readonly string _text;
    private readonly LineMap _map;
    private readonly TokenReader _r;
    private readonly List<ParseDiagnostic> _diagnostics;
    private bool _partial;
    private int _nodeCount;
    private const int MaxNodes = 2000;

    public CParser(string text, IReadOnlyList<Token> tokens, List<ParseDiagnostic> diagnostics, CancellationToken ct)
    {
        _text = text;
        _map = new LineMap(text);
        _r = new TokenReader(tokens, ct);
        _diagnostics = diagnostics;
    }

    public bool IsPartial => _partial;

    public List<SyntaxNode> Parse(out List<OutlineNode> outlines)
    {
        outlines = [];
        List<SyntaxNode> children = [];
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();
            if (_nodeCount >= MaxNodes)
            {
                _partial = true;
                break;
            }
            if (_r.Check((int)CTokenKind.Semicolon))
            {
                _r.Advance();
                continue;
            }
            if (TryParseTypedef(children, outlines))
                continue;
            if (TryParseComposite(children, outlines))
                continue;
            if (TryParseFunction(children, outlines))
                continue;

            // Recovery: skip balanced groups without double-advancing after a
            // successful skip. The old `if (Check && !TrySkip) ... else Advance`
            // pattern fell into else after a successful skip and ate the next
            // token, which dropped decls after FOO(fake)-style macro calls.
            SkipBalancedOrAdvance();
        }
        return children;
    }

    private void SkipBalancedOrAdvance()
    {
        if (_r.Check((int)CTokenKind.LBrace))
        {
            if (!_r.TrySkipBalanced((int)CTokenKind.LBrace, (int)CTokenKind.RBrace))
                _partial = true;
            return;
        }
        if (_r.Check((int)CTokenKind.LParen))
        {
            if (!_r.TrySkipBalanced((int)CTokenKind.LParen, (int)CTokenKind.RParen))
                _partial = true;
            return;
        }
        if (_r.Check((int)CTokenKind.LBracket))
        {
            if (!_r.TrySkipBalanced((int)CTokenKind.LBracket, (int)CTokenKind.RBracket))
                _partial = true;
            return;
        }
        _r.Advance();
    }

    private bool TryParseTypedef(List<SyntaxNode> children, List<OutlineNode> outlines)
    {
        int save = _r.Index;
        int start = _r.Current.Start;
        if (!_r.Check((int)CTokenKind.KwTypedef))
            return false;
        _r.Advance();
        string? name = null;
        TextSpan nameSpan = default;
        while (!_r.IsEof && !_r.Check((int)CTokenKind.Semicolon))
        {
            _r.ThrowIfCancellationRequested();
            if (_r.Check((int)CTokenKind.KwStruct) || _r.Check((int)CTokenKind.KwUnion) || _r.Check((int)CTokenKind.KwEnum))
            {
                _r.Advance();
                if (_r.Check((int)CTokenKind.Identifier))
                    _r.Advance();
                if (_r.Check((int)CTokenKind.LBrace))
                    _r.TrySkipBalanced((int)CTokenKind.LBrace, (int)CTokenKind.RBrace);
                continue;
            }
            if (IsSpecifierKeyword((CTokenKind)_r.Current.Kind))
            {
                _r.Advance();
                continue;
            }
            if (_r.Check((int)CTokenKind.LParen))
            {
                Token? inner = ScanFirstIdentifierInsideParens();
                if (inner is not null)
                {
                    name = inner.Value.Text ?? "item";
                    nameSpan = inner.Value.Span;
                }
                else if (_r.Peek(-1).Kind == (int)CTokenKind.Identifier)
                {
                    name = _r.Peek(-1).Text ?? "item";
                    nameSpan = _r.Peek(-1).Span;
                }
                _r.TrySkipBalanced((int)CTokenKind.LParen, (int)CTokenKind.RParen);
                continue;
            }
            if (_r.Check((int)CTokenKind.LBracket))
            {
                _r.TrySkipBalanced((int)CTokenKind.LBracket, (int)CTokenKind.RBracket);
                continue;
            }
            if (_r.Check((int)CTokenKind.Identifier))
            {
                name = _r.Current.Text ?? "item";
                nameSpan = _r.Current.Span;
                _r.Advance();
                continue;
            }
            _r.Advance();
        }
        if (_r.Check((int)CTokenKind.Semicolon))
            _r.Advance();
        if (name is null)
            return true;
        int end = _r.Peek(-1).End;
        TextSpan span = new(start, Math.Max(0, end - start));
        children.Add(new SyntaxNode("typedef_declaration", span, name, nameSpan));
        outlines.Add(MakeOutline("typedef", name, span, 1));
        _nodeCount++;
        return true;
    }

    private bool TryParseComposite(List<SyntaxNode> children, List<OutlineNode> outlines)
    {
        int save = _r.Index;
        int start = _r.Current.Start;
        if (!_r.Check((int)CTokenKind.KwStruct) && !_r.Check((int)CTokenKind.KwUnion) && !_r.Check((int)CTokenKind.KwEnum))
            return false;
        string kind = _r.Current.Kind switch
        {
            (int)CTokenKind.KwStruct => "struct",
            (int)CTokenKind.KwUnion => "union",
            _ => "enum",
        };
        _r.Advance();
        string? tag = null;
        TextSpan tagSpan = default;
        if (_r.Check((int)CTokenKind.Identifier))
        {
            tag = _r.Current.Text ?? "item";
            tagSpan = _r.Current.Span;
            _r.Advance();
        }
        if (!_r.Check((int)CTokenKind.LBrace))
        {
            _r.Seek(save);
            return false;
        }
        Token open = _r.Current;
        int end;
        if (!_r.TrySkipBalanced((int)CTokenKind.LBrace, (int)CTokenKind.RBrace))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("C001", $"Unclosed {kind} body.", open.Span, DiagnosticSeverity.Error));
            end = _text.Length;
        }
        else
            end = _r.Peek(-1).End;
        SkipToSemicolon();
        if (tag is null)
            return true;
        TextSpan span = new(start, Math.Max(0, end - start));
        children.Add(new SyntaxNode("struct_specifier", span, tag, tagSpan));
        outlines.Add(MakeOutline(kind, tag, span, 1));
        _nodeCount++;
        return true;
    }

    private bool TryParseFunction(List<SyntaxNode> children, List<OutlineNode> outlines)
    {
        int save = _r.Index;
        int start = _r.Current.Start;
        bool sawSpecifier = false;
        while (true)
        {
            if (IsSpecifierKeyword((CTokenKind)_r.Current.Kind) || _r.Check((int)CTokenKind.Star))
            {
                sawSpecifier = true;
                _r.Advance();
                continue;
            }
            if (!_r.Check((int)CTokenKind.Identifier))
            {
                _r.Seek(save);
                return false;
            }
            CTokenKind next = (CTokenKind)_r.Peek(1).Kind;
            if (next is CTokenKind.Identifier or CTokenKind.Star or CTokenKind.LBracket
                or CTokenKind.KwStruct or CTokenKind.KwUnion or CTokenKind.KwEnum)
            {
                sawSpecifier = true;
                _r.Advance();
                continue;
            }
            break;
        }
        if (!sawSpecifier)
        {
            _r.Seek(save);
            return false;
        }
        string name = _r.Current.Text ?? "*";
        TextSpan nameSpan = _r.Current.Span;
        _r.Advance();
        if (!_r.Check((int)CTokenKind.LParen))
        {
            SkipToSemicolon();
            return true;
        }
        if (!TrySkipParameterList(out bool isKnr))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("C002", "Unclosed parameter list.", _r.Current.Span, DiagnosticSeverity.Error));
            SkipToSemicolon();
            return true;
        }
        while (_r.Check((int)CTokenKind.Identifier) && (CTokenKind)_r.Peek(1).Kind == CTokenKind.LParen)
        {
            _r.Advance();
            _r.TrySkipBalanced((int)CTokenKind.LParen, (int)CTokenKind.RParen);
        }
        Token open = _r.Current;
        int end;
        if (!_r.Check((int)CTokenKind.LBrace))
        {
            SkipToSemicolon();
            end = _r.Peek(-1).End;
            if (isKnr && TryParseKnrBody(out int bodyEnd))
                end = bodyEnd;
        }
        else
        {
            if (!_r.TrySkipBalanced((int)CTokenKind.LBrace, (int)CTokenKind.RBrace))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("C003", "Unclosed function body.", open.Span, DiagnosticSeverity.Error));
                end = _text.Length;
            }
            else
                end = _r.Peek(-1).End;
        }
        TextSpan span = new(start, Math.Max(0, end - start));
        children.Add(new SyntaxNode("function_definition", span, name, nameSpan));
        outlines.Add(MakeOutline("function", name, span, 1));
        _nodeCount++;
        return true;
    }

    private bool TrySkipParameterList(out bool isKnr)
    {
        isKnr = false;
        if (!_r.Check((int)CTokenKind.LParen))
            return false;
        int depth = 0;
        bool sawTypeSpecifier = false;
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();
            CTokenKind kind = (CTokenKind)_r.Current.Kind;
            if (kind == CTokenKind.LParen)
            {
                depth++;
                _r.Advance();
                continue;
            }
            if (kind == CTokenKind.RParen)
            {
                _r.Advance();
                depth--;
                if (depth == 0)
                {
                    isKnr = !sawTypeSpecifier;
                    return true;
                }
                continue;
            }
            if (depth == 1 && IsSpecifierKeyword(kind))
                sawTypeSpecifier = true;
            _r.Advance();
        }
        return false;
    }

    private bool TryParseKnrBody(out int bodyEnd)
    {
        bodyEnd = 0;
        int save = _r.Index;
        while (TryParseKnrParameterDeclaration())
        {
            // declarations consumed
        }
        if (!_r.Check((int)CTokenKind.LBrace))
        {
            _r.Seek(save);
            return false;
        }
        Token open = _r.Current;
        if (!_r.TrySkipBalanced((int)CTokenKind.LBrace, (int)CTokenKind.RBrace))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("C003", "Unclosed function body.", open.Span, DiagnosticSeverity.Error));
            bodyEnd = _text.Length;
            return true;
        }
        bodyEnd = _r.Peek(-1).End;
        return true;
    }

    private bool TryParseKnrParameterDeclaration()
    {
        int save = _r.Index;
        if (!IsSpecifierKeyword((CTokenKind)_r.Current.Kind))
            return false;
        SkipToSemicolon();
        if (_r.Peek(-1).Kind != (int)CTokenKind.Semicolon)
        {
            _r.Seek(save);
            return false;
        }
        return true;
    }

    private void SkipToSemicolon()
    {
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();
            if (_r.Check((int)CTokenKind.Semicolon))
            {
                _r.Advance();
                return;
            }
            if (_r.Check((int)CTokenKind.LBrace))
            {
                if (!_r.TrySkipBalanced((int)CTokenKind.LBrace, (int)CTokenKind.RBrace))
                    _partial = true;
                continue;
            }
            if (_r.Check((int)CTokenKind.LParen))
            {
                if (!_r.TrySkipBalanced((int)CTokenKind.LParen, (int)CTokenKind.RParen))
                    _partial = true;
                continue;
            }
            if (_r.Check((int)CTokenKind.LBracket))
            {
                if (!_r.TrySkipBalanced((int)CTokenKind.LBracket, (int)CTokenKind.RBracket))
                    _partial = true;
                continue;
            }
            _r.Advance();
        }
    }

    private Token? ScanFirstIdentifierInsideParens()
    {
        int depth = 0;
        for (int offset = 1; offset < _r.Count - _r.Index; offset++)
        {
            _r.ThrowIfCancellationRequested();
            CTokenKind kind = (CTokenKind)_r.Peek(offset).Kind;
            if (kind == CTokenKind.LParen)
                depth++;
            else if (kind == CTokenKind.RParen)
            {
                if (depth == 0)
                    return null;
                depth--;
            }
            else if (depth == 0 && kind == CTokenKind.Identifier)
                return _r.Peek(offset);
        }
        return null;
    }

    private static bool IsSpecifierKeyword(CTokenKind kind) => kind is
        CTokenKind.KwStruct or CTokenKind.KwUnion or CTokenKind.KwEnum or CTokenKind.KwTypedef or
        CTokenKind.KwStatic or CTokenKind.KwInline or CTokenKind.KwExtern or CTokenKind.KwConst or
        CTokenKind.KwVolatile or CTokenKind.KwUnsigned or CTokenKind.KwSigned or CTokenKind.KwLong or
        CTokenKind.KwShort or CTokenKind.KwVoid or CTokenKind.KwChar or CTokenKind.KwInt or
        CTokenKind.KwFloat or CTokenKind.KwDouble;

    private OutlineNode MakeOutline(string kind, string label, TextSpan span, int level)
    {
        LinePositionSpan lp = _map.GetLinePositionSpan(span);
        LinePositionSpan nav = new(new LinePosition(lp.Start.Line, 1), new LinePosition(lp.End.Line, 1));
        return new OutlineNode(kind, label, span, nav, level);
    }
}
