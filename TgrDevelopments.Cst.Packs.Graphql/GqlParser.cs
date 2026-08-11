using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Graphql;

internal sealed class GqlParser
{
    private readonly string _text;
    private readonly LineMap _map;
    private readonly TokenReader _r;
    private bool _partial;

    public GqlParser(string text, IReadOnlyList<Token> tokens, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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
            if (TryParseDecl(children, outlines))
                continue;
            if (_r.Check((int)GqlTokenKind.LBrace))
            {
                if (!_r.TrySkipBalanced((int)GqlTokenKind.LBrace, (int)GqlTokenKind.RBrace))
                    _partial = true;
            }
            else
                _r.Advance();
        }
        return children;
    }

    private bool TryParseDecl(List<SyntaxNode> children, List<OutlineNode> outlines)
    {
        // skip extend
        if (_r.Check((int)GqlTokenKind.KwExtend))
            _r.Advance();

        GqlTokenKind k = (GqlTokenKind)_r.Current.Kind;
        if (k is GqlTokenKind.KwType or GqlTokenKind.KwInterface or GqlTokenKind.KwInput
            or GqlTokenKind.KwEnum or GqlTokenKind.KwUnion or GqlTokenKind.KwScalar)
            return ParseNamed("type", "object_type", children, outlines, needBrace: k is not GqlTokenKind.KwScalar and not GqlTokenKind.KwUnion);

        if (k is GqlTokenKind.KwQuery or GqlTokenKind.KwMutation or GqlTokenKind.KwSubscription)
            return ParseOp(children, outlines);

        if (k == GqlTokenKind.KwFragment)
            return ParseNamed("fragment", "fragment", children, outlines, needBrace: true);

        return false;
    }

    private bool ParseNamed(
        string outlineKind,
        string nodeKind,
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        bool needBrace)
    {
        int start = _r.Current.Start;
        _r.Advance(); // keyword
        string name = "*";
        if (_r.Check((int)GqlTokenKind.Identifier))
        {
            name = _r.Current.Text ?? "*";
            _r.Advance();
        }

        // skip implements / = union members / args until { or next decl
        int guard = 0;
        while (!_r.IsEof && !_r.Check((int)GqlTokenKind.LBrace) && guard++ < 48)
        {
            GqlTokenKind k = (GqlTokenKind)_r.Current.Kind;
            if (k is GqlTokenKind.KwType or GqlTokenKind.KwInterface or GqlTokenKind.KwInput
                or GqlTokenKind.KwEnum or GqlTokenKind.KwQuery or GqlTokenKind.KwMutation
                or GqlTokenKind.KwSubscription or GqlTokenKind.KwFragment or GqlTokenKind.KwExtend)
                break;
            if (_r.Check((int)GqlTokenKind.LParen))
                _r.TrySkipBalanced((int)GqlTokenKind.LParen, (int)GqlTokenKind.RParen);
            else
                _r.Advance();
        }

        int end;
        if (_r.Check((int)GqlTokenKind.LBrace))
            end = SkipBrace();
        else
            end = _r.Current.IsEof ? _text.Length : _r.Current.Start;

        TextSpan span = new(start, Math.Max(0, end - start));
        children.Add(new SyntaxNode(nodeKind, span, name));
        outlines.Add(Make(outlineKind, name, span));
        _ = needBrace;
        return true;
    }

    private bool ParseOp(List<SyntaxNode> children, List<OutlineNode> outlines)
    {
        int start = _r.Current.Start;
        string opKind = ((GqlTokenKind)_r.Current.Kind) switch
        {
            GqlTokenKind.KwMutation => "mutation",
            GqlTokenKind.KwSubscription => "subscription",
            _ => "query",
        };
        _r.Advance();
        string name = opKind;
        if (_r.Check((int)GqlTokenKind.Identifier))
        {
            name = _r.Current.Text ?? opKind;
            _r.Advance();
        }
        if (_r.Check((int)GqlTokenKind.LParen))
            _r.TrySkipBalanced((int)GqlTokenKind.LParen, (int)GqlTokenKind.RParen);

        int end = _r.Check((int)GqlTokenKind.LBrace) ? SkipBrace() : (_r.Current.IsEof ? _text.Length : _r.Current.Start);
        TextSpan span = new(start, Math.Max(0, end - start));
        children.Add(new SyntaxNode("operation", span, name));
        outlines.Add(Make("query", name, span));
        return true;
    }

    private int SkipBrace()
    {
        if (!_r.TrySkipBalanced((int)GqlTokenKind.LBrace, (int)GqlTokenKind.RBrace))
        {
            _partial = true;
            return _text.Length;
        }
        return _r.Peek(-1).End;
    }

    private OutlineNode Make(string kind, string label, TextSpan span)
    {
        LinePositionSpan lp = _map.GetLinePositionSpan(span);
        return new OutlineNode(kind, label, span,
            new LinePositionSpan(new LinePosition(lp.Start.Line, 1), new LinePosition(lp.End.Line, 1)), 1);
    }
}
