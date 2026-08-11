using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Lua;

internal sealed class LuaParser
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

    public LuaParser(string text, IReadOnlyList<Token> tokens, LineMap map, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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

    private void ParseItems(List<SyntaxNode> parentChildren, List<OutlineNode> parentOutlines, int baseLevel, bool insideFunction, bool stopAtEnd = false)
    {
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();

            if (_r.Check((int)LuaTokenKind.KwEnd))
            {
                if (stopAtEnd)
                    return;
                _r.Advance();
                continue;
            }
            if (_r.Check((int)LuaTokenKind.KwElseIf) || _r.Check((int)LuaTokenKind.KwElse)
                || _r.Check((int)LuaTokenKind.KwUntil))
            {
                if (stopAtEnd)
                    return;
                _r.Advance();
                continue;
            }
            if (_r.Check((int)LuaTokenKind.Semicolon))
            {
                _r.Advance();
                continue;
            }

            bool local = _r.Check((int)LuaTokenKind.KwLocal);
            if (local)
                _r.Advance();

            if (_r.Check((int)LuaTokenKind.KwFunction))
            {
                TryParseFunction(parentChildren, parentOutlines, baseLevel, local);
                continue;
            }

            if (local)
                continue;

            SkipTrash();
        }
    }

    private bool TryParseFunction(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel, bool local)
    {
        int start = _r.Current.Start;
        _r.Advance();

        string name = "";
        TextSpan nameSpan = default;

        if (!_r.Check((int)LuaTokenKind.Identifier))
        {
            if (_r.Check((int)LuaTokenKind.LParen))
            {
                _r.TrySkipBalanced((int)LuaTokenKind.LParen, (int)LuaTokenKind.RParen);
            }
            SkipToEnd();
            return true;
        }

        name = _r.Current.Text ?? "function";
        nameSpan = _r.Current.Span;
        _r.Advance();

        while (_r.Check((int)LuaTokenKind.Period) || _r.Check((int)LuaTokenKind.Colon))
        {
            bool isColon = _r.Check((int)LuaTokenKind.Colon);
            _r.Advance();
            if (_r.Check((int)LuaTokenKind.Identifier))
            {
                name += (isColon ? ":" : ".") + _r.Current.Text;
                nameSpan = _r.Current.Span;
                _r.Advance();
            }
            else break;
        }

        if (_r.Check((int)LuaTokenKind.Colon))
        {
            _r.Advance();
            if (_r.Check((int)LuaTokenKind.Identifier))
            {
                name += ":" + _r.Current.Text;
                nameSpan = _r.Current.Span;
                _r.Advance();
            }
        }

        if (_r.Check((int)LuaTokenKind.LParen))
        {
            if (!_r.TrySkipBalanced((int)LuaTokenKind.LParen, (int)LuaTokenKind.RParen))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("LUA001", "Unclosed parameter list.", _r.Current.Span, DiagnosticSeverity.Error));
                SkipToEnd();
                AddFunction(children, outlines, name, nameSpan, start);
                return true;
            }
        }

        if (baseLevel + 1 > MaxDepth)
        {
            _diagnostics.Add(new ParseDiagnostic("LUA005", "Max depth exceeded.", new TextSpan(start, _r.Current.End - start), DiagnosticSeverity.Warning));
            SkipToEnd();
            AddFunction(children, outlines, name, nameSpan, start);
            return true;
        }

        _depth++;
        List<SyntaxNode> innerChildren = [];
        List<OutlineNode> innerOutlines = [];
        if (_nodeCount < MaxOutlineNodes)
            ParseItems(innerChildren, innerOutlines, baseLevel + 1, true, stopAtEnd: true);

        if (_r.Check((int)LuaTokenKind.KwEnd))
        {
            _r.Advance();
        }
        else
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("LUA001", "Unclosed function body.", new TextSpan(start, 0), DiagnosticSeverity.Error));
        }

        int end = _r.Peek(-1).End;
        CheckCaps(start, end);
        TextSpan span = new(start, end - start);
        children.Add(new SyntaxNode("function", span, name, nameSpan, innerChildren.Count > 0 ? innerChildren : null));
        outlines.Add(MakeOutline("function", name, span, baseLevel, innerOutlines.Count > 0 ? innerOutlines : null));
        _nodeCount++;
        _depth--;
        return true;
    }

    private void AddFunction(List<SyntaxNode> children, List<OutlineNode> outlines, string name, TextSpan nameSpan, int start)
    {
        int end = _r.Peek(-1).End;
        CheckCaps(start, end);
        TextSpan span = new(start, end - start);
        children.Add(new SyntaxNode("function", span, name, nameSpan));
        outlines.Add(MakeOutline("function", name, span, _depth));
        _nodeCount++;
    }

    private void SkipToEnd()
    {
        int depth = 1;
        while (!_r.IsEof && depth > 0)
        {
            _r.ThrowIfCancellationRequested();
            if (_r.Check((int)LuaTokenKind.KwFunction))
                depth++;
            else if (_r.Check((int)LuaTokenKind.KwEnd))
                depth--;
            if (depth > 0)
                _r.Advance();
        }
        if (_r.Check((int)LuaTokenKind.KwEnd))
            _r.Advance();
    }

    private void SkipTrash()
    {
        while (!_r.IsEof)
        {
            LuaTokenKind k = (LuaTokenKind)_r.Current.Kind;
            if (k is LuaTokenKind.KwFunction or LuaTokenKind.KwLocal
                or LuaTokenKind.KwEnd or LuaTokenKind.KwElseIf or LuaTokenKind.KwElse
                or LuaTokenKind.KwUntil or LuaTokenKind.Semicolon)
                return;
            _r.Advance();
        }
    }

    private void CheckCaps(int start, int end)
    {
        if (_depth >= MaxDepth)
            _diagnostics.Add(new ParseDiagnostic("LUA005", "Max depth exceeded.", new TextSpan(start, end - start), DiagnosticSeverity.Warning));
        if (_nodeCount >= MaxOutlineNodes)
            _diagnostics.Add(new ParseDiagnostic("LUA006", "Max outline nodes exceeded.", new TextSpan(start, end - start), DiagnosticSeverity.Warning));
    }

    private OutlineNode MakeOutline(string kind, string label, TextSpan span, int level, IReadOnlyList<OutlineNode>? children = null)
    {
        LinePositionSpan lp = _map.GetLinePositionSpan(span);
        LinePositionSpan nav = new(new LinePosition(lp.Start.Line, 1), new LinePosition(lp.End.Line, 1));
        return new OutlineNode(kind, label, span, nav, level, children);
    }
}
