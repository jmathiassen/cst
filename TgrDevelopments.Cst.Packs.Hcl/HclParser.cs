using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Hcl;

internal sealed class HclParser
{
    private readonly string _text;
    private readonly LineMap _map;
    private readonly TokenReader _r;
    private bool _partial;

    public HclParser(string text, IReadOnlyList<Token> tokens, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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
            if (TryParseBlock(children, outlines))
                continue;
            if (_r.Check((int)HclTokenKind.LBrace))
            {
                if (!_r.TrySkipBalanced((int)HclTokenKind.LBrace, (int)HclTokenKind.RBrace))
                    _partial = true;
            }
            else
                _r.Advance();
        }
        return children;
    }

    private bool TryParseBlock(List<SyntaxNode> children, List<OutlineNode> outlines)
    {
        HclTokenKind k = (HclTokenKind)_r.Current.Kind;
        string? typ = k switch
        {
            HclTokenKind.KwResource => "resource",
            HclTokenKind.KwData => "data",
            HclTokenKind.KwModule => "module",
            HclTokenKind.KwVariable => "variable",
            HclTokenKind.KwOutput => "output",
            HclTokenKind.KwProvider => "provider",
            HclTokenKind.KwLocals => "locals",
            HclTokenKind.KwTerraform => "terraform",
            HclTokenKind.KwBackend => "backend",
            _ => null,
        };
        if (typ is null)
            return false;

        int start = _r.Current.Start;
        _r.Advance();

        string l1 = "";
        string l2 = "";
        if (_r.Check((int)HclTokenKind.StringLiteral))
        {
            l1 = _r.Current.Text ?? "";
            _r.Advance();
            if (_r.Check((int)HclTokenKind.StringLiteral))
            {
                l2 = _r.Current.Text ?? "";
                _r.Advance();
            }
        }

        string label = typ;
        if (l1.Length > 0) label += " " + l1;
        if (l2.Length > 0) label += " " + l2;
        label = TextUtil.Truncate(label, 80);

        // skip until {
        int guard = 0;
        while (!_r.IsEof && !_r.Check((int)HclTokenKind.LBrace) && guard++ < 16)
            _r.Advance();

        int end;
        if (_r.Check((int)HclTokenKind.LBrace))
        {
            if (!_r.TrySkipBalanced((int)HclTokenKind.LBrace, (int)HclTokenKind.RBrace))
            {
                _partial = true;
                end = _text.Length;
            }
            else
                end = _r.Peek(-1).End;
        }
        else
            end = _r.Current.IsEof ? _text.Length : _r.Current.Start;

        TextSpan span = new(start, Math.Max(0, end - start));
        children.Add(new SyntaxNode("block", span, label));
        outlines.Add(Make(typ, label, span));
        return true;
    }

    private OutlineNode Make(string kind, string label, TextSpan span)
    {
        LinePositionSpan lp = _map.GetLinePositionSpan(span);
        return new OutlineNode(kind, label, span,
            new LinePositionSpan(new LinePosition(lp.Start.Line, 1), new LinePosition(lp.End.Line, 1)), 1);
    }
}
