using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Python;

/// <summary>
/// Recursive-descent structure parser for Python outline: class / def / async def,
/// nested via INDENT/DEDENT. Decorators attach to the following declaration.
/// </summary>
internal sealed class PyParser
{
    public const int DefaultMaxDepth = 32;
    public const int DefaultMaxNodes = 2000;

    private readonly string _text;
    private readonly LineMap _map;
    private readonly TokenReader _r;
    private readonly List<ParseDiagnostic> _diagnostics;
    private readonly int _maxDepth;
    private readonly int _maxNodes;
    private readonly Stack<string> _parentKinds = new();
    private bool _partial;
    private int _nodeCount;
    private int _depth;

    public PyParser(
        string text,
        IReadOnlyList<Token> tokens,
        List<ParseDiagnostic> diagnostics,
        CancellationToken ct,
        int maxDepth = DefaultMaxDepth,
        int maxNodes = DefaultMaxNodes)
    {
        _text = text;
        _map = new LineMap(text);
        _r = new TokenReader(tokens, ct);
        _diagnostics = diagnostics;
        _maxDepth = maxDepth;
        _maxNodes = maxNodes;
    }

    public bool IsPartial => _partial;

    public List<SyntaxNode> ParseModule(out List<OutlineNode> outlines)
    {
        outlines = [];
        List<SyntaxNode> children = [];
        ParseSuite(children, outlines, level: 1);
        return children;
    }

    private void ParseSuite(
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        int level)
    {
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();
            if (_nodeCount >= _maxNodes)
            {
                MarkCap();
                break;
            }

            if (_r.Check((int)PyTokenKind.Dedent))
                break;

            if (_r.Check((int)PyTokenKind.Newline))
            {
                _r.Advance();
                continue;
            }

            if (_r.Check((int)PyTokenKind.Indent))
            {
                _r.Advance();
                SkipUntilDedent();
                continue;
            }

            int decoStart = -1;
            while (_r.Check((int)PyTokenKind.At))
            {
                if (decoStart < 0)
                    decoStart = Current().Start;
                SkipDecoratorLine();
            }

            if (_r.Check((int)PyTokenKind.KwClass))
            {
                ParseClassOrDef(children, outlines, level, isClass: true, decoStart);
                continue;
            }

            if (_r.Check((int)PyTokenKind.KwAsync)
                && (PyTokenKind)_r.Peek(1).Kind == PyTokenKind.KwDef)
            {
                ParseClassOrDef(children, outlines, level, isClass: false, decoStart);
                continue;
            }

            if (_r.Check((int)PyTokenKind.KwDef))
            {
                ParseClassOrDef(children, outlines, level, isClass: false, decoStart);
                continue;
            }

            SkipSimpleStatement();
        }
    }

    private void ParseClassOrDef(
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        int level,
        bool isClass,
        int decoStart)
    {
        if (_depth >= _maxDepth)
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic(
                "PY005", "Max nesting depth exceeded.",
                CurrentSpan(), DiagnosticSeverity.Warning));
            SkipSimpleStatement();
            return;
        }

        int start = decoStart >= 0 ? decoStart : Current().Start;

        if (_r.Check((int)PyTokenKind.KwAsync))
            _r.Advance();

        _r.Advance(); // class | def

        string name = "*";
        TextSpan? nameSpan = null;
        if (_r.Check((int)PyTokenKind.Identifier))
        {
            name = Current().Text ?? "*";
            nameSpan = Current().Span;
            _r.Advance();
        }

        if (_r.Check((int)PyTokenKind.LParen))
        {
            if (!_r.TrySkipBalanced((int)PyTokenKind.LParen, (int)PyTokenKind.RParen))
                _partial = true;
        }

        int guard = 0;
        while (!_r.IsEof
               && !_r.Check((int)PyTokenKind.Colon)
               && !_r.Check((int)PyTokenKind.Newline)
               && guard++ < 64)
        {
            if (_r.Check((int)PyTokenKind.LBracket))
                _r.TrySkipBalanced((int)PyTokenKind.LBracket, (int)PyTokenKind.RBracket);
            else if (_r.Check((int)PyTokenKind.LParen))
                _r.TrySkipBalanced((int)PyTokenKind.LParen, (int)PyTokenKind.RParen);
            else
                _r.Advance();
        }

        if (_r.Check((int)PyTokenKind.Colon))
            _r.Advance();

        while (!_r.IsEof && !_r.Check((int)PyTokenKind.Newline) && !_r.Check((int)PyTokenKind.Indent))
            _r.Advance();

        if (_r.Check((int)PyTokenKind.Newline))
            _r.Advance();

        string outlineKind;
        if (isClass)
            outlineKind = "class";
        else if (_parentKinds.Count > 0 && _parentKinds.Peek() == "class")
            outlineKind = "method";
        else
            outlineKind = "function";

        List<SyntaxNode> members = [];
        List<OutlineNode> memberOutlines = [];
        int end = Current().IsEof ? _text.Length : Current().Start;

        if (_r.Check((int)PyTokenKind.Indent))
        {
            _r.Advance();
            _depth++;
            _parentKinds.Push(outlineKind);
            ParseSuite(members, memberOutlines, level + 1);
            _parentKinds.Pop();
            _depth--;

            if (_r.Check((int)PyTokenKind.Dedent))
            {
                // Dedent sits at the start of the next less-indented line.
                // Body ends just before that line.
                end = Current().Start;
                if (end > 0)
                {
                    // trim trailing newlines in span for cleaner line ends
                    int e = end;
                    while (e > start && (_text[e - 1] == '\n' || _text[e - 1] == '\r'))
                        e--;
                    // keep at least through last content line's newline for line map end
                    end = Current().Start;
                }
                _r.Advance();
            }
            else
            {
                _partial = true;
                end = _text.Length;
            }
        }

        if (members.Count > 0)
        {
            int childEnd = members[^1].Span.End;
            if (childEnd > end)
                end = childEnd;
        }
        if (memberOutlines.Count > 0)
        {
            int childEnd = memberOutlines[^1].Span.End;
            if (childEnd > end)
                end = childEnd;
        }

        if (end <= start)
            end = Math.Min(_text.Length, start + 1);
        if (end > _text.Length)
            end = _text.Length;

        TextSpan span = new(start, end - start);
        string nodeKind = isClass ? "class_def" : "function_def";
        children.Add(new SyntaxNode(nodeKind, span, name, nameSpan, members));
        outlines.Add(MakeOutline(outlineKind, name, span, level, memberOutlines));
        _nodeCount++;
    }

    private void SkipDecoratorLine()
    {
        _r.Advance(); // @
        while (!_r.IsEof && !_r.Check((int)PyTokenKind.Newline))
        {
            if (_r.Check((int)PyTokenKind.LParen))
                _r.TrySkipBalanced((int)PyTokenKind.LParen, (int)PyTokenKind.RParen);
            else if (_r.Check((int)PyTokenKind.LBracket))
                _r.TrySkipBalanced((int)PyTokenKind.LBracket, (int)PyTokenKind.RBracket);
            else
                _r.Advance();
        }
        if (_r.Check((int)PyTokenKind.Newline))
            _r.Advance();
    }

    private void SkipSimpleStatement()
    {
        int guard = 0;
        while (!_r.IsEof && guard++ < 256)
        {
            if (_r.Check((int)PyTokenKind.Newline))
            {
                _r.Advance();
                if (_r.Check((int)PyTokenKind.Indent))
                {
                    _r.Advance();
                    SkipUntilDedent();
                }
                return;
            }
            if (_r.Check((int)PyTokenKind.Indent))
            {
                _r.Advance();
                SkipUntilDedent();
                return;
            }
            if (_r.Check((int)PyTokenKind.Dedent))
                return;
            if (_r.Check((int)PyTokenKind.LParen))
                _r.TrySkipBalanced((int)PyTokenKind.LParen, (int)PyTokenKind.RParen);
            else if (_r.Check((int)PyTokenKind.LBracket))
                _r.TrySkipBalanced((int)PyTokenKind.LBracket, (int)PyTokenKind.RBracket);
            else if (_r.Check((int)PyTokenKind.LBrace))
                _r.TrySkipBalanced((int)PyTokenKind.LBrace, (int)PyTokenKind.RBrace);
            else
                _r.Advance();
        }
    }

    private void SkipUntilDedent()
    {
        int depth = 1;
        while (!_r.IsEof && depth > 0)
        {
            if (_r.Check((int)PyTokenKind.Indent))
            {
                depth++;
                _r.Advance();
            }
            else if (_r.Check((int)PyTokenKind.Dedent))
            {
                depth--;
                _r.Advance();
            }
            else
                _r.Advance();
        }
    }

    private void MarkCap()
    {
        _partial = true;
        _diagnostics.Add(new ParseDiagnostic(
            "PY006", "Outline node cap reached.",
            CurrentSpan(), DiagnosticSeverity.Warning));
    }

    private OutlineNode MakeOutline(
        string kind,
        string label,
        TextSpan span,
        int level,
        IReadOnlyList<OutlineNode>? children = null)
    {
        LinePositionSpan lineSpan = _map.GetLinePositionSpan(span);
        LinePositionSpan nav = new(
            new LinePosition(lineSpan.Start.Line, 1),
            new LinePosition(lineSpan.End.Line, 1));
        return new OutlineNode(kind, label, span, nav, level, children);
    }

    private Token Current() => _r.Current;
    private TextSpan CurrentSpan() => _r.Current.Span;
}
