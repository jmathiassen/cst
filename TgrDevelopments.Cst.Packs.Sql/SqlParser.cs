using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Sql;

internal sealed class SqlParser
{
    private readonly string _text;
    private readonly LineMap _map;
    private readonly TokenReader _r;
    private readonly List<ParseDiagnostic> _diagnostics;
    private bool _partial;

    public SqlParser(string text, IReadOnlyList<Token> tokens, List<ParseDiagnostic> diagnostics, CancellationToken ct)
    {
        _diagnostics = diagnostics;
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
            if (TryParseCreate(children, outlines))
                continue;
            _r.Advance();
        }
        return children;
    }

    private bool TryParseCreate(List<SyntaxNode> children, List<OutlineNode> outlines)
    {
        if (!_r.Check((int)SqlTokenKind.KwCreate))
            return false;

        int start = _r.Current.Start;
        _r.Advance(); // CREATE
        if (_r.Check((int)SqlTokenKind.KwOr))
        {
            _r.Advance();
            if (_r.Check((int)SqlTokenKind.KwReplace))
                _r.Advance();
        }

        SqlTokenKind kindTok = (SqlTokenKind)_r.Current.Kind;
        string outlineKind = kindTok switch
        {
            SqlTokenKind.KwTable => "table",
            SqlTokenKind.KwView => "view",
            SqlTokenKind.KwProcedure or SqlTokenKind.KwProc => "procedure",
            SqlTokenKind.KwFunction => "function",
            SqlTokenKind.KwTrigger => "trigger",
            SqlTokenKind.KwIndex => "index",
            SqlTokenKind.KwType => "type",
            SqlTokenKind.KwSchema => "schema",
            _ => "",
        };
        if (outlineKind.Length == 0)
            return true; // consumed CREATE

        _r.Advance(); // kind

        // IF NOT EXISTS
        if (_r.Check((int)SqlTokenKind.KwIf))
        {
            _r.Advance();
            if (_r.Check((int)SqlTokenKind.KwNot)) _r.Advance();
            if (_r.Check((int)SqlTokenKind.KwExists)) _r.Advance();
        }

        string name = ReadName();
        int end = ConsumeUntilNextCreateOrGoOrSemi(start);

        TextSpan span = new(start, Math.Max(0, end - start));
        children.Add(new SyntaxNode("create_" + outlineKind, span, name));
        outlines.Add(Make(outlineKind, name, span));
        return true;
    }

    private string ReadName()
    {
        if (!_r.Check((int)SqlTokenKind.Identifier))
            return "*";
        string name = _r.Current.Text ?? "*";
        _r.Advance();
        while (_r.Check((int)SqlTokenKind.Dot) && (SqlTokenKind)_r.Peek(1).Kind == SqlTokenKind.Identifier)
        {
            _r.Advance();
            name = _r.Current.Text ?? name;
            _r.Advance();
        }
        // strip quotes/brackets already handled in lexer text
        name = name.Trim('`', '"', '\'');
        return name.Length == 0 ? "*" : name;
    }

    private int ConsumeUntilNextCreateOrGoOrSemi(int start)
    {
        int lastEnd = start;
        int paren = 0;
        while (!_r.IsEof)
        {
            SqlTokenKind k = (SqlTokenKind)_r.Current.Kind;
            if (k == SqlTokenKind.KwGo && paren == 0)
            {
                lastEnd = _r.Current.Start;
                _r.Advance();
                break;
            }
            if (k == SqlTokenKind.KwCreate && paren == 0)
            {
                lastEnd = _r.Current.Start;
                break;
            }
            if (k == SqlTokenKind.LParen) paren++;
            else if (k == SqlTokenKind.RParen) paren = Math.Max(0, paren - 1);
            else if (k == SqlTokenKind.Semicolon && paren == 0)
            {
                lastEnd = _r.Current.End;
                _r.Advance();
                break;
            }
            lastEnd = _r.Current.End;
            _r.Advance();
        }
        if (_r.IsEof)
        {
            if (paren > 0)
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic(
                    "SQL001", "Unclosed parentheses in CREATE statement.",
                    new TextSpan(start, _text.Length - start), DiagnosticSeverity.Error));
            }
            lastEnd = _text.Length;
        }
        return lastEnd;
    }

    private OutlineNode Make(string kind, string label, TextSpan span)
    {
        LinePositionSpan lp = _map.GetLinePositionSpan(span);
        return new OutlineNode(kind, label, span,
            new LinePositionSpan(new LinePosition(lp.Start.Line, 1), new LinePosition(lp.End.Line, 1)), 1);
    }
}
