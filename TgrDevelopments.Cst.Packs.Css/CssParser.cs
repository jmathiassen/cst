using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Css;

internal sealed class CssParser
{
    private readonly string _text;
    private readonly LineMap _map;
    private readonly TokenReader _r;
    private bool _partial;

    public CssParser(string text, IReadOnlyList<Token> tokens, List<ParseDiagnostic> diagnostics, CancellationToken ct)
    {
        _ = diagnostics;
        _text = text;
        _map = new LineMap(text);
        _r = new TokenReader(tokens, ct);
    }

    public bool IsPartial => _partial;

    public List<SyntaxNode> Parse(out List<OutlineNode> outlines)
    {
        outlines = [];
        List<SyntaxNode> children = [];
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();
            if (_r.Check((int)CssTokenKind.AtKeyword))
            {
                ParseAtRule(children, outlines);
                continue;
            }
            if (TryParseRuleset(children, outlines))
                continue;
            _r.Advance();
        }
        return children;
    }

    private void ParseAtRule(List<SyntaxNode> children, List<OutlineNode> outlines)
    {
        int start = _r.Current.Start;
        string label = _r.Current.Text ?? "@";
        _r.Advance();

        while (!_r.IsEof && !_r.Check((int)CssTokenKind.LBrace) && !_r.Check((int)CssTokenKind.Semicolon))
        {
            if (_r.Check((int)CssTokenKind.LParen))
                _r.TrySkipBalanced((int)CssTokenKind.LParen, (int)CssTokenKind.RParen);
            else
                _r.Advance();
        }

        List<SyntaxNode> nestedChildren = [];
        List<OutlineNode> nestedOutlines = [];
        int end;
        if (_r.Check((int)CssTokenKind.LBrace))
        {
            Token open = _r.Current;
            _r.Advance(); // {
            while (!_r.IsEof && !_r.Check((int)CssTokenKind.RBrace))
            {
                if (_r.Check((int)CssTokenKind.AtKeyword))
                {
                    ParseAtRule(nestedChildren, nestedOutlines);
                    continue;
                }
                if (TryParseRuleset(nestedChildren, nestedOutlines))
                    continue;
                if (_r.Check((int)CssTokenKind.LBrace))
                {
                    if (!_r.TrySkipBalanced((int)CssTokenKind.LBrace, (int)CssTokenKind.RBrace))
                    {
                        _partial = true;
                        break;
                    }
                }
                else
                    _r.Advance();
            }
            if (_r.Check((int)CssTokenKind.RBrace))
            {
                end = _r.Current.End;
                _r.Advance();
            }
            else
            {
                _partial = true;
                end = _text.Length;
                _ = open;
            }
        }
        else if (_r.Check((int)CssTokenKind.Semicolon))
        {
            end = _r.Current.End;
            _r.Advance();
        }
        else
            end = _r.Current.IsEof ? _text.Length : _r.Current.Start;

        string display = TextUtil.Truncate(label, 60);
        TextSpan span = new(start, Math.Max(0, end - start));
        children.Add(new SyntaxNode("at_rule", span, display, children: nestedChildren));
        // bump nested outline levels
        List<OutlineNode> leveled = [];
        foreach (OutlineNode n in nestedOutlines)
            leveled.Add(new OutlineNode(n.Kind, n.Label, n.Span, n.LineSpan, n.Level + 1, n.Children));
        outlines.Add(Make("at-rule", display, span, leveled));
    }

    private bool TryParseRuleset(List<SyntaxNode> children, List<OutlineNode> outlines)
    {
        int save = _r.Index;
        int start = _r.Current.Start;
        StringBuilder sel = new();
        int guard = 0;
        while (!_r.IsEof && !_r.Check((int)CssTokenKind.LBrace) && guard++ < 64)
        {
            CssTokenKind k = (CssTokenKind)_r.Current.Kind;
            if (k is CssTokenKind.AtKeyword or CssTokenKind.RBrace or CssTokenKind.Semicolon)
            {
                _r.Seek(save);
                return false;
            }

            if (k == CssTokenKind.LBracket)
            {
                _r.TrySkipBalanced((int)CssTokenKind.LBracket, (int)CssTokenKind.RBracket);
                continue;
            }
            if (k == CssTokenKind.LParen)
            {
                _r.TrySkipBalanced((int)CssTokenKind.LParen, (int)CssTokenKind.RParen);
                continue;
            }

            if (_r.Current.Text is not null)
            {
                if (k == CssTokenKind.Dot)
                {
                    // handled below — Dot has no text
                }
                else if (k == CssTokenKind.Hash)
                {
                    if (sel.Length > 0) sel.Append(' ');
                    sel.Append(_r.Current.Text);
                }
                else
                {
                    if (sel.Length > 0 && k == CssTokenKind.Ident) sel.Append(' ');
                    sel.Append(_r.Current.Text);
                }
            }
            else if (k == CssTokenKind.Dot)
            {
                sel.Append('.');
                _r.Advance();
                if (_r.Check((int)CssTokenKind.Ident) && _r.Current.Text is not null)
                {
                    sel.Append(_r.Current.Text);
                    _r.Advance();
                }
                continue;
            }
            else if (k == CssTokenKind.Star)
                sel.Append('*');
            else if (k == CssTokenKind.Colon)
                sel.Append(':');
            else if (k == CssTokenKind.Comma)
                sel.Append(", ");
            else if (k == CssTokenKind.Gt)
                sel.Append('>');

            _r.Advance();
        }

        if (!_r.Check((int)CssTokenKind.LBrace) || sel.Length == 0)
        {
            _r.Seek(save);
            return false;
        }

        int end = SkipBrace();
        string label = TextUtil.Truncate(sel.ToString().Trim(), 60);
        TextSpan span = new(start, Math.Max(0, end - start));
        children.Add(new SyntaxNode("ruleset", span, label));
        outlines.Add(Make("rule", label, span));
        return true;
    }

    private int SkipBrace()
    {
        if (!_r.TrySkipBalanced((int)CssTokenKind.LBrace, (int)CssTokenKind.RBrace))
        {
            _partial = true;
            return _text.Length;
        }
        return _r.Peek(-1).End;
    }

    private OutlineNode Make(string kind, string label, TextSpan span, IReadOnlyList<OutlineNode>? children = null)
    {
        LinePositionSpan lp = _map.GetLinePositionSpan(span);
        return new OutlineNode(kind, label, span,
            new LinePositionSpan(new LinePosition(lp.Start.Line, 1), new LinePosition(lp.End.Line, 1)), 1, children);
    }
}
