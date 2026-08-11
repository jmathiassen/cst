using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.JavaScript;

/// <summary>
/// Recursive-descent structure parser for JS outline: functions, classes, methods.
/// Non-declaration statements are skipped via balanced brace/paren recovery.
/// </summary>
internal sealed class JsParser
{
    public const int DefaultMaxDepth = 32;
    public const int DefaultMaxNodes = 2000;

    private readonly string _text;
    private readonly LineMap _map;
    private readonly TokenReader _r;
    private readonly List<ParseDiagnostic> _diagnostics;
    private readonly int _maxDepth;
    private readonly int _maxNodes;
    private bool _partial;
    private int _nodeCount;
    private int _depth;

    public JsParser(
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

    public List<SyntaxNode> ParseTopLevel(out List<OutlineNode> outlines)
    {
        outlines = [];
        List<SyntaxNode> children = [];
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();
            if (_nodeCount >= _maxNodes)
            {
                MarkCap();
                break;
            }

            if (TryParseDeclaration(children, outlines, level: 1))
                continue;

            RecoverOne();
        }

        return children;
    }

    private bool TryParseDeclaration(List<SyntaxNode> children, List<OutlineNode> outlines, int level)
    {
        int save = _r.Index;

        if (_r.Check((int)JsTokenKind.KwExport))
        {
            _r.Advance();
            if (_r.Check((int)JsTokenKind.KwDefault))
                _r.Advance();
        }

        bool async = false;
        if (_r.Check((int)JsTokenKind.KwAsync))
        {
            JsTokenKind next = (JsTokenKind)_r.Peek(1).Kind;
            JsTokenKind next2 = (JsTokenKind)_r.Peek(2).Kind;
            if (next == JsTokenKind.KwFunction
                || (next == JsTokenKind.Identifier && next2 == JsTokenKind.LParen))
            {
                async = true;
                _r.Advance();
            }
            else
            {
                _r.Seek(save);
                return false;
            }
        }

        if (_r.Check((int)JsTokenKind.KwFunction))
            return ParseFunctionDeclaration(children, outlines, level, async);

        if (_r.Check((int)JsTokenKind.KwClass))
            return ParseClassDeclaration(children, outlines, level);

        if (_r.Check((int)JsTokenKind.KwConst)
            || _r.Check((int)JsTokenKind.KwLet)
            || _r.Check((int)JsTokenKind.KwVar))
            return ParseVariableFunction(children, outlines, level, save);

        _r.Seek(save);
        return false;
    }

    private bool ParseFunctionDeclaration(
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        int level,
        bool async)
    {
        _ = async;
        int start = Current().Start;
        _r.Advance(); // function
        if (_r.Check((int)JsTokenKind.Star))
            _r.Advance();

        string name = "*";
        TextSpan? nameSpan = null;
        if (_r.Check((int)JsTokenKind.Identifier))
        {
            name = Current().Text ?? "*";
            nameSpan = Current().Span;
            _r.Advance();
        }

        SkipParameterList();
        int end = ParseBodyEnd();
        TextSpan span = SpanOf(start, end);
        children.Add(new SyntaxNode("function_declaration", span, name, nameSpan));
        outlines.Add(MakeOutline("function", name, span, level));
        _nodeCount++;
        return true;
    }

    private bool ParseVariableFunction(
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        int level,
        int save)
    {
        int start = Current().Start;
        _r.Advance(); // const|let|var
        if (!_r.Check((int)JsTokenKind.Identifier))
        {
            _r.Seek(save);
            return false;
        }

        string name = Current().Text ?? "*";
        TextSpan nameSpan = Current().Span;
        _r.Advance();
        if (!_r.TryEat((int)JsTokenKind.Eq))
        {
            _r.Seek(save);
            return false;
        }

        if (_r.Check((int)JsTokenKind.KwAsync))
            _r.Advance();

        // Object literal with methods: const api = { run() {}, #priv: 1 }
        if (_r.Check((int)JsTokenKind.LBrace))
            return ParseObjectBinding(children, outlines, level, start, name, nameSpan);

        bool isFunc = false;
        if (_r.Check((int)JsTokenKind.KwFunction))
        {
            _r.Advance();
            if (_r.Check((int)JsTokenKind.Star))
                _r.Advance();
            SkipParameterList();
            isFunc = true;
        }
        else if (_r.Check((int)JsTokenKind.LParen))
        {
            SkipBalancedParen();
            if (_r.TryEat((int)JsTokenKind.Arrow))
                isFunc = true;
        }
        else if (_r.Check((int)JsTokenKind.Identifier) && (JsTokenKind)_r.Peek(1).Kind == JsTokenKind.Arrow)
        {
            _r.Advance();
            _r.Advance(); // =>
            isFunc = true;
        }

        if (!isFunc)
        {
            _r.Seek(save);
            return false;
        }

        int end = ParseBodyEnd();
        TextSpan span = SpanOf(start, end);
        children.Add(new SyntaxNode("function_declaration", span, name, nameSpan));
        outlines.Add(MakeOutline("function", name, span, level));
        _nodeCount++;
        return true;
    }

    private bool ParseObjectBinding(
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        int level,
        int start,
        string name,
        TextSpan nameSpan)
    {
        Token open = Current();
        _r.Advance(); // {
        List<SyntaxNode> members = [];
        List<OutlineNode> memberOutlines = [];
        while (!_r.IsEof && !_r.Check((int)JsTokenKind.RBrace))
        {
            _r.ThrowIfCancellationRequested();
            if (_nodeCount >= _maxNodes)
            {
                MarkCap();
                break;
            }

            if (TryParseMethod(members, memberOutlines, level + 1))
            {
                _r.TryEat((int)JsTokenKind.Comma);
                continue;
            }

            if (TryParseField(members, memberOutlines, level + 1))
            {
                _r.TryEat((int)JsTokenKind.Comma);
                continue;
            }

            if (_r.Check((int)JsTokenKind.LBrace) || _r.Check((int)JsTokenKind.LBracket)
                || _r.Check((int)JsTokenKind.LParen))
            {
                int openK = Current().Kind;
                int closeK = openK == (int)JsTokenKind.LBrace ? (int)JsTokenKind.RBrace
                    : openK == (int)JsTokenKind.LBracket ? (int)JsTokenKind.RBracket
                    : (int)JsTokenKind.RParen;
                if (!_r.TrySkipBalanced(openK, closeK))
                {
                    _partial = true;
                    break;
                }
                _r.TryEat((int)JsTokenKind.Comma);
                continue;
            }

            if (_r.Check((int)JsTokenKind.Comma) || _r.Check((int)JsTokenKind.Semicolon))
            {
                _r.Advance();
                continue;
            }

            _r.Advance();
        }

        int end;
        if (_r.Check((int)JsTokenKind.RBrace))
        {
            end = Current().End;
            _r.Advance();
        }
        else
        {
            _partial = true;
            end = _text.Length;
            _diagnostics.Add(new ParseDiagnostic(
                "JS001", "Unclosed object literal.",
                open.Span, DiagnosticSeverity.Error));
        }

        // trailing ; optional
        _r.TryEat((int)JsTokenKind.Semicolon);

        TextSpan span = SpanOf(start, end);
        children.Add(new SyntaxNode("object_binding", span, name, nameSpan, members));
        // Only outline object if it has methods/fields worth navigating
        if (memberOutlines.Count > 0)
            outlines.Add(MakeOutline("object", name, span, level, memberOutlines));
        _nodeCount++;
        return true;
    }

    private bool ParseClassDeclaration(
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        int level)
    {
        if (_depth >= _maxDepth)
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic(
                "JS005", "Max nesting depth exceeded.",
                CurrentSpan(), DiagnosticSeverity.Warning));
            _r.Advance();
            return true;
        }

        int start = Current().Start;
        _r.Advance(); // class
        string name = "*";
        TextSpan? nameSpan = null;
        if (_r.Check((int)JsTokenKind.Identifier))
        {
            name = Current().Text ?? "*";
            nameSpan = Current().Span;
            _r.Advance();
        }

        if (_r.Check((int)JsTokenKind.KwExtends))
        {
            _r.Advance();
            SkipExpressionish();
        }

        if (!_r.Check((int)JsTokenKind.LBrace))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic(
                "JS001", "Expected class body '{'.",
                CurrentSpan(), DiagnosticSeverity.Error));
            TextSpan bare = SpanOf(start, Current().Start);
            children.Add(new SyntaxNode("class_declaration", bare, name, nameSpan));
            outlines.Add(MakeOutline("class", name, bare, level));
            _nodeCount++;
            return true;
        }

        Token open = Current();
        _r.Advance(); // {
        _depth++;
        List<SyntaxNode> members = [];
        List<OutlineNode> memberOutlines = [];
        while (!_r.IsEof && !_r.Check((int)JsTokenKind.RBrace))
        {
            _r.ThrowIfCancellationRequested();
            if (_nodeCount >= _maxNodes)
            {
                MarkCap();
                break;
            }

            if (TryParseMethod(members, memberOutlines, level + 1))
                continue;

            if (TryParseField(members, memberOutlines, level + 1))
                continue;

            if (_r.Check((int)JsTokenKind.LBrace))
            {
                if (!_r.TrySkipBalanced((int)JsTokenKind.LBrace, (int)JsTokenKind.RBrace))
                {
                    _partial = true;
                    break;
                }
            }
            else if (_r.Check((int)JsTokenKind.Semicolon))
                _r.Advance();
            else
                _r.Advance();
        }

        _depth--;
        int end;
        if (_r.Check((int)JsTokenKind.RBrace))
        {
            end = Current().End;
            _r.Advance();
        }
        else
        {
            _partial = true;
            end = _text.Length;
            _diagnostics.Add(new ParseDiagnostic(
                "JS001", "Unclosed class body.",
                open.Span, DiagnosticSeverity.Error));
        }

        TextSpan span = SpanOf(start, end);
        children.Add(new SyntaxNode("class_declaration", span, name, nameSpan, members));
        outlines.Add(MakeOutline("class", name, span, level, memberOutlines));
        _nodeCount++;
        return true;
    }

    private bool TryParseMethod(
        List<SyntaxNode> members,
        List<OutlineNode> memberOutlines,
        int level)
    {
        int save = _r.Index;
        int start = Current().Start;

        while (_r.Check((int)JsTokenKind.KwAsync) || _r.Check((int)JsTokenKind.KwStatic))
            _r.Advance();

        bool isGetSet = false;
        if (_r.Check((int)JsTokenKind.KwGet) || _r.Check((int)JsTokenKind.KwSet))
        {
            JsTokenKind after = (JsTokenKind)_r.Peek(1).Kind;
            if (after is JsTokenKind.Identifier or JsTokenKind.PrivateIdentifier
                or JsTokenKind.StringLiteral or JsTokenKind.LBracket)
            {
                isGetSet = true;
                _r.Advance();
            }
            else
            {
                _r.Seek(save);
                return false;
            }
        }

        string kind = "method";
        string name;
        TextSpan? nameSpan;

        if (_r.Check((int)JsTokenKind.KwConstructor))
        {
            kind = "constructor";
            name = "constructor";
            nameSpan = Current().Span;
            _r.Advance();
        }
        else if (_r.Check((int)JsTokenKind.Identifier)
            || _r.Check((int)JsTokenKind.PrivateIdentifier)
            || _r.Check((int)JsTokenKind.StringLiteral))
        {
            name = Current().Text ?? _text.Substring(Current().Start, Current().Span.Length);
            nameSpan = Current().Span;
            _r.Advance();
        }
        else if (_r.Check((int)JsTokenKind.LBracket))
        {
            name = "[]";
            nameSpan = Current().Span;
            if (!_r.TrySkipBalanced((int)JsTokenKind.LBracket, (int)JsTokenKind.RBracket))
            {
                _r.Seek(save);
                return false;
            }
        }
        else
        {
            _r.Seek(save);
            return false;
        }

        if (kind != "constructor" && name is "if" or "for" or "while" or "switch" or "catch")
        {
            _r.Seek(save);
            return false;
        }

        if (!_r.Check((int)JsTokenKind.LParen))
        {
            _r.Seek(save);
            return false;
        }

        SkipParameterList();
        if (!_r.Check((int)JsTokenKind.LBrace))
        {
            _r.Seek(save);
            return false;
        }

        int end = ParseBodyEnd();
        TextSpan span = SpanOf(start, end);
        string nodeKind = kind == "constructor" ? "constructor" : "method_definition";
        string outlineKind = kind == "constructor" ? "constructor" : "method";
        members.Add(new SyntaxNode(nodeKind, span, name, nameSpan));
        memberOutlines.Add(MakeOutline(outlineKind, name, span, level));
        _nodeCount++;
        _ = isGetSet;
        return true;
    }

    private bool TryParseField(
        List<SyntaxNode> members,
        List<OutlineNode> memberOutlines,
        int level)
    {
        int save = _r.Index;
        int start = Current().Start;

        while (_r.Check((int)JsTokenKind.KwStatic) || _r.Check((int)JsTokenKind.KwAsync))
            _r.Advance();

        if (!_r.Check((int)JsTokenKind.Identifier) && !_r.Check((int)JsTokenKind.PrivateIdentifier))
        {
            _r.Seek(save);
            return false;
        }

        // Method shape (name() or name = function/=>) handled elsewhere
        string name = Current().Text ?? "*";
        TextSpan nameSpan = Current().Span;
        _r.Advance();

        if (_r.Check((int)JsTokenKind.LParen))
        {
            _r.Seek(save);
            return false;
        }

        // field: name = expr;  or  name;  or  #name = expr
        if (_r.Check((int)JsTokenKind.Eq))
        {
            _r.Advance();
            SkipExpressionish();
        }
        else if (!_r.Check((int)JsTokenKind.Semicolon)
            && !_r.Check((int)JsTokenKind.Comma)
            && !_r.Check((int)JsTokenKind.RBrace))
        {
            _r.Seek(save);
            return false;
        }

        _r.TryEat((int)JsTokenKind.Semicolon);
        int end = _r.Index > 0 ? _r.Peek(-1).End : start + 1;
        if (!_r.IsEof && Current().Start > end)
            end = Current().Start;
        // Prefer end of last consumed token
        Token prev = _r.Peek(-1);
        if (!prev.IsEof)
            end = prev.End;

        TextSpan span = SpanOf(start, end);
        members.Add(new SyntaxNode("field_definition", span, name, nameSpan));
        memberOutlines.Add(MakeOutline("field", name, span, level));
        _nodeCount++;
        return true;
    }

    private void SkipParameterList()
    {
        if (!_r.Check((int)JsTokenKind.LParen))
            return;
        Token open = Current();
        if (!_r.TrySkipBalanced((int)JsTokenKind.LParen, (int)JsTokenKind.RParen))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic(
                "JS001", "Unclosed parameter list.",
                open.Span, DiagnosticSeverity.Error));
        }
    }

    private void SkipBalancedParen()
    {
        if (!_r.TrySkipBalanced((int)JsTokenKind.LParen, (int)JsTokenKind.RParen))
            _partial = true;
    }

    private void SkipExpressionish()
    {
        int guard = 0;
        while (!_r.IsEof && guard++ < 32)
        {
            if (_r.Check((int)JsTokenKind.Identifier)
                || _r.Check((int)JsTokenKind.KwThis)
                || _r.Check((int)JsTokenKind.KwSuper))
            {
                _r.Advance();
                if (_r.Check((int)JsTokenKind.Dot) || _r.Check((int)JsTokenKind.QuestionDot))
                {
                    _r.Advance();
                    continue;
                }

                break;
            }

            break;
        }
    }

    /// <summary>Consumes function/method body or expression; returns exclusive end offset.</summary>
    private int ParseBodyEnd()
    {
        if (_r.Check((int)JsTokenKind.LBrace))
        {
            Token open = Current();
            if (_r.TrySkipBalanced((int)JsTokenKind.LBrace, (int)JsTokenKind.RBrace))
                return _r.Peek(-1).End;

            _partial = true;
            _diagnostics.Add(new ParseDiagnostic(
                "JS001", "Unclosed block.",
                open.Span, DiagnosticSeverity.Error));
            return _text.Length;
        }

        while (!_r.IsEof
               && !_r.Check((int)JsTokenKind.Semicolon)
               && !_r.Check((int)JsTokenKind.RBrace)
               && !_r.Check((int)JsTokenKind.Comma))
        {
            if (_r.Check((int)JsTokenKind.LBrace))
            {
                if (!_r.TrySkipBalanced((int)JsTokenKind.LBrace, (int)JsTokenKind.RBrace))
                    _partial = true;
                break;
            }

            if (_r.Check((int)JsTokenKind.LParen))
            {
                if (!_r.TrySkipBalanced((int)JsTokenKind.LParen, (int)JsTokenKind.RParen))
                    _partial = true;
                continue;
            }

            if (_r.Check((int)JsTokenKind.LBracket))
            {
                if (!_r.TrySkipBalanced((int)JsTokenKind.LBracket, (int)JsTokenKind.RBracket))
                    _partial = true;
                continue;
            }

            _r.Advance();
        }

        if (_r.Check((int)JsTokenKind.Semicolon))
        {
            int end = Current().End;
            _r.Advance();
            return end;
        }

        return Current().IsEof ? _text.Length : Current().Start;
    }

    private void RecoverOne()
    {
        if (_r.Check((int)JsTokenKind.LBrace))
        {
            if (!_r.TrySkipBalanced((int)JsTokenKind.LBrace, (int)JsTokenKind.RBrace))
                _partial = true;
        }
        else if (_r.Check((int)JsTokenKind.LParen))
        {
            if (!_r.TrySkipBalanced((int)JsTokenKind.LParen, (int)JsTokenKind.RParen))
                _partial = true;
        }
        else if (_r.Check((int)JsTokenKind.LBracket))
        {
            if (!_r.TrySkipBalanced((int)JsTokenKind.LBracket, (int)JsTokenKind.RBracket))
                _partial = true;
        }
        else
            _r.Advance();
    }

    private void MarkCap()
    {
        _partial = true;
        _diagnostics.Add(new ParseDiagnostic(
            "JS006", "Outline node cap reached.",
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

    private static TextSpan SpanOf(int start, int end)
    {
        if (end < start)
            end = start;
        return new TextSpan(start, end - start);
    }
}
