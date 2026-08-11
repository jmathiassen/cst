using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Shell;

/// <summary>RD parser for shell functions: <c>name()</c> / <c>function name</c>.</summary>
internal sealed class ShParser
{
    private readonly string _text;
    private readonly LineMap _map;
    private readonly TokenReader _r;
    private readonly List<ParseDiagnostic> _diagnostics;
    private bool _partial;
    private int _nodeCount;
    private const int MaxNodes = 2000;

    public ShParser(string text, IReadOnlyList<Token> tokens, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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

            if (_r.Check((int)ShTokenKind.Newline) || _r.Check((int)ShTokenKind.Semicolon))
            {
                _r.Advance();
                continue;
            }

            if (TryParseFunction(children, outlines))
                continue;

            // skip one token / balanced group
            if (_r.Check((int)ShTokenKind.LBrace))
            {
                if (!_r.TrySkipBalanced((int)ShTokenKind.LBrace, (int)ShTokenKind.RBrace))
                    _partial = true;
            }
            else if (_r.Check((int)ShTokenKind.LParen))
            {
                if (!_r.TrySkipBalanced((int)ShTokenKind.LParen, (int)ShTokenKind.RParen))
                    _partial = true;
            }
            else
                _r.Advance();
        }
        return children;
    }

    private bool TryParseFunction(List<SyntaxNode> children, List<OutlineNode> outlines)
    {
        int save = _r.Index;
        int start = _r.Current.Start;
        string name;

        if (_r.Check((int)ShTokenKind.KwFunction))
        {
            _r.Advance();
            if (!_r.Check((int)ShTokenKind.Identifier))
            {
                _r.Seek(save);
                return false;
            }
            name = _r.Current.Text ?? "*";
            _r.Advance();
            // optional ()
            if (_r.Check((int)ShTokenKind.LParen))
                _r.TrySkipBalanced((int)ShTokenKind.LParen, (int)ShTokenKind.RParen);
        }
        else if (_r.Check((int)ShTokenKind.Identifier)
                 && (ShTokenKind)_r.Peek(1).Kind == ShTokenKind.LParen
                 && (ShTokenKind)_r.Peek(2).Kind == ShTokenKind.RParen)
        {
            name = _r.Current.Text ?? "*";
            _r.Advance();
            _r.Advance(); // (
            _r.Advance(); // )
        }
        else
        {
            _r.Seek(save);
            return false;
        }

        // skip newlines before body
        while (_r.Check((int)ShTokenKind.Newline))
            _r.Advance();

        int end;
        if (_r.Check((int)ShTokenKind.LBrace))
        {
            Token open = _r.Current;
            if (_r.TrySkipBalanced((int)ShTokenKind.LBrace, (int)ShTokenKind.RBrace))
                end = _r.Peek(-1).End;
            else
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic(
                    "SH001", "Unclosed function body.",
                    open.Span, DiagnosticSeverity.Error));
                end = _text.Length;
            }
        }
        else
            end = _r.Current.IsEof ? _text.Length : _r.Current.Start;

        TextSpan span = new(start, Math.Max(0, end - start));
        children.Add(new SyntaxNode("function_definition", span, name));
        outlines.Add(MakeOutline("function", name, span, 1));
        _nodeCount++;
        return true;
    }

    private OutlineNode MakeOutline(string kind, string label, TextSpan span, int level)
    {
        LinePositionSpan lp = _map.GetLinePositionSpan(span);
        LinePositionSpan nav = new(new LinePosition(lp.Start.Line, 1), new LinePosition(lp.End.Line, 1));
        return new OutlineNode(kind, label, span, nav, level);
    }
}
