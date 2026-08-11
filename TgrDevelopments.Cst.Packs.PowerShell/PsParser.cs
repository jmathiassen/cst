using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.PowerShell;

/// <summary>RD parser for PowerShell function/class/enum outline.</summary>
internal sealed class PsParser
{
    private readonly string _text;
    private readonly LineMap _map;
    private readonly TokenReader _r;
    private readonly List<ParseDiagnostic> _diagnostics;
    private bool _partial;
    private int _nodeCount;
    private int _depth;
    private const int MaxNodes = 2000;
    private const int MaxDepth = 32;

    public PsParser(string text, IReadOnlyList<Token> tokens, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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
            if (_nodeCount >= MaxNodes) { _partial = true; break; }

            if (_r.Check((int)PsTokenKind.Newline) || _r.Check((int)PsTokenKind.Semicolon))
            {
                _r.Advance();
                continue;
            }

            if (TryParseDecl(children, outlines, level: 1))
                continue;

            RecoverOne();
        }
        return children;
    }

    private bool TryParseDecl(List<SyntaxNode> children, List<OutlineNode> outlines, int level)
    {
        if (_r.Check((int)PsTokenKind.KwFunction) || _r.Check((int)PsTokenKind.KwFilter))
            return ParseNamedBrace(children, outlines, level, "function", "function_definition");

        if (_r.Check((int)PsTokenKind.KwClass))
            return ParseClass(children, outlines, level);

        if (_r.Check((int)PsTokenKind.KwEnum))
            return ParseNamedBrace(children, outlines, level, "enum", "enum_definition");

        return false;
    }

    private bool ParseNamedBrace(
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        int level,
        string outlineKind,
        string nodeKind)
    {
        int start = _r.Current.Start;
        _r.Advance(); // keyword
        if (!_r.Check((int)PsTokenKind.Identifier))
            return true; // consumed keyword

        string name = _r.Current.Text ?? "*";
        _r.Advance();

        // optional param block ()
        if (_r.Check((int)PsTokenKind.LParen))
            _r.TrySkipBalanced((int)PsTokenKind.LParen, (int)PsTokenKind.RParen);

        while (_r.Check((int)PsTokenKind.Newline))
            _r.Advance();

        int end = FinishBraceBody(start);
        TextSpan span = new(start, Math.Max(0, end - start));
        children.Add(new SyntaxNode(nodeKind, span, name));
        outlines.Add(MakeOutline(outlineKind, name, span, level));
        _nodeCount++;
        return true;
    }

    private bool ParseClass(List<SyntaxNode> children, List<OutlineNode> outlines, int level)
    {
        if (_depth >= MaxDepth)
        {
            _partial = true;
            _r.Advance();
            return true;
        }

        int start = _r.Current.Start;
        _r.Advance(); // class
        if (!_r.Check((int)PsTokenKind.Identifier))
            return true;
        string name = _r.Current.Text ?? "*";
        _r.Advance();

        while (_r.Check((int)PsTokenKind.Newline))
            _r.Advance();

        List<SyntaxNode> members = [];
        List<OutlineNode> memberOutlines = [];

        if (!_r.Check((int)PsTokenKind.LBrace))
        {
            TextSpan bare = new(start, Math.Max(0, _r.Current.Start - start));
            children.Add(new SyntaxNode("class_definition", bare, name));
            outlines.Add(MakeOutline("class", name, bare, level));
            _nodeCount++;
            return true;
        }

        Token open = _r.Current;
        _r.Advance(); // {
        _depth++;
        while (!_r.IsEof && !_r.Check((int)PsTokenKind.RBrace))
        {
            if (_nodeCount >= MaxNodes) { _partial = true; break; }
            if (_r.Check((int)PsTokenKind.Newline) || _r.Check((int)PsTokenKind.Semicolon))
            {
                _r.Advance();
                continue;
            }
            if (!TryParseMethod(members, memberOutlines, level + 1))
                RecoverOne();
        }
        _depth--;

        int end;
        if (_r.Check((int)PsTokenKind.RBrace))
        {
            end = _r.Current.End;
            _r.Advance();
        }
        else
        {
            _partial = true;
            end = _text.Length;
            _diagnostics.Add(new ParseDiagnostic("PS001", "Unclosed class body.", open.Span, DiagnosticSeverity.Error));
        }

        TextSpan span = new(start, Math.Max(0, end - start));
        children.Add(new SyntaxNode("class_definition", span, name, children: members));
        outlines.Add(MakeOutline("class", name, span, level, memberOutlines));
        _nodeCount++;
        return true;
    }

    private bool TryParseMethod(List<SyntaxNode> members, List<OutlineNode> memberOutlines, int level)
    {
        int save = _r.Index;
        int start = _r.Current.Start;

        // optional [ReturnType]
        if (_r.Check((int)PsTokenKind.LBracket))
        {
            if (!_r.TrySkipBalanced((int)PsTokenKind.LBracket, (int)PsTokenKind.RBracket))
            {
                _r.Seek(save);
                return false;
            }
        }

        if (!_r.Check((int)PsTokenKind.Identifier))
        {
            _r.Seek(save);
            return false;
        }

        string name = _r.Current.Text ?? "*";
        if (name is "if" or "for" or "while" or "switch" or "catch" or "foreach")
        {
            _r.Seek(save);
            return false;
        }
        _r.Advance();

        if (!_r.Check((int)PsTokenKind.LParen))
        {
            _r.Seek(save);
            return false;
        }
        _r.TrySkipBalanced((int)PsTokenKind.LParen, (int)PsTokenKind.RParen);

        while (_r.Check((int)PsTokenKind.Newline))
            _r.Advance();

        if (!_r.Check((int)PsTokenKind.LBrace))
        {
            _r.Seek(save);
            return false;
        }

        int end = FinishBraceBody(start);
        TextSpan span = new(start, Math.Max(0, end - start));
        members.Add(new SyntaxNode("method_definition", span, name));
        memberOutlines.Add(MakeOutline("method", name, span, level));
        _nodeCount++;
        return true;
    }

    private int FinishBraceBody(int start)
    {
        if (!_r.Check((int)PsTokenKind.LBrace))
            return _r.Current.IsEof ? _text.Length : _r.Current.Start;
        Token open = _r.Current;
        if (_r.TrySkipBalanced((int)PsTokenKind.LBrace, (int)PsTokenKind.RBrace))
            return _r.Peek(-1).End;
        _partial = true;
        _diagnostics.Add(new ParseDiagnostic("PS001", "Unclosed block.", open.Span, DiagnosticSeverity.Error));
        return _text.Length;
    }

    private void RecoverOne()
    {
        if (_r.Check((int)PsTokenKind.LBrace))
        {
            if (!_r.TrySkipBalanced((int)PsTokenKind.LBrace, (int)PsTokenKind.RBrace))
                _partial = true;
        }
        else if (_r.Check((int)PsTokenKind.LParen))
        {
            if (!_r.TrySkipBalanced((int)PsTokenKind.LParen, (int)PsTokenKind.RParen))
                _partial = true;
        }
        else if (_r.Check((int)PsTokenKind.LBracket))
        {
            if (!_r.TrySkipBalanced((int)PsTokenKind.LBracket, (int)PsTokenKind.RBracket))
                _partial = true;
        }
        else
            _r.Advance();
    }

    private OutlineNode MakeOutline(
        string kind, string label, TextSpan span, int level, IReadOnlyList<OutlineNode>? children = null)
    {
        LinePositionSpan lp = _map.GetLinePositionSpan(span);
        LinePositionSpan nav = new(new LinePosition(lp.Start.Line, 1), new LinePosition(lp.End.Line, 1));
        return new OutlineNode(kind, label, span, nav, level, children);
    }
}
