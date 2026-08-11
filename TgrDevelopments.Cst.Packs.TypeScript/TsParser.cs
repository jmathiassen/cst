using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.TypeScript;

/// <summary>
/// Recursive-descent structure parser for TS outline: functions, classes, interfaces, types, enums, namespaces.
/// Non-declaration statements are skipped via balanced brace/paren recovery.
/// </summary>
internal sealed class TsParser
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

    public TsParser(
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

        if (_r.Check((int)TsTokenKind.KwExport) || _r.Check((int)TsTokenKind.KwDeclare))
        {
            _r.Advance();
            if (_r.Check((int)TsTokenKind.KwDefault))
                _r.Advance();
            if (_r.Check((int)TsTokenKind.KwDeclare))
                _r.Advance();
        }

        // abstract class
        if (_r.Check((int)TsTokenKind.KwAbstract) && (TsTokenKind)_r.Peek(1).Kind == TsTokenKind.KwClass)
            _r.Advance();

        bool async = false;
        if (_r.Check((int)TsTokenKind.KwAsync))
        {
            TsTokenKind next = (TsTokenKind)_r.Peek(1).Kind;
            TsTokenKind next2 = (TsTokenKind)_r.Peek(2).Kind;
            if (next == TsTokenKind.KwFunction
                || (next == TsTokenKind.Identifier && next2 == TsTokenKind.LParen))
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

        if (_r.Check((int)TsTokenKind.KwFunction))
            return ParseFunctionDeclaration(children, outlines, level, async);

        if (_r.Check((int)TsTokenKind.KwClass))
            return ParseClassDeclaration(children, outlines, level);

        if (_r.Check((int)TsTokenKind.KwInterface))
            return ParseNamedBraceDecl(children, outlines, level, TsTokenKind.KwInterface, "interface", "interface_declaration");

        if (_r.Check((int)TsTokenKind.KwEnum))
            return ParseNamedBraceDecl(children, outlines, level, TsTokenKind.KwEnum, "enum", "enum_declaration");

        if (_r.Check((int)TsTokenKind.KwNamespace) || _r.Check((int)TsTokenKind.KwModule))
            return ParseNamedBraceDecl(children, outlines, level, CurrentKind(), "namespace", "namespace_declaration");

        if (_r.Check((int)TsTokenKind.KwType))
            return ParseTypeAlias(children, outlines, level, save);

        if (_r.Check((int)TsTokenKind.KwConst)
            || _r.Check((int)TsTokenKind.KwLet)
            || _r.Check((int)TsTokenKind.KwVar))
            return ParseVariableFunction(children, outlines, level, save);

        _r.Seek(save);
        return false;
    }

    private bool ParseNamedBraceDecl(
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        int level,
        TsTokenKind keyword,
        string outlineKind,
        string nodeKind)
    {
        int start = Current().Start;
        if (!_r.Check((int)keyword))
            return false;
        _r.Advance();

        string name = "*";
        TextSpan? nameSpan = null;
        if (_r.Check((int)TsTokenKind.Identifier))
        {
            name = Current().Text ?? "*";
            nameSpan = Current().Span;
            _r.Advance();
        }

        // skip generic params / extends / implements-ish until { or ;
        int guard = 0;
        while (!_r.IsEof && !_r.Check((int)TsTokenKind.LBrace) && !_r.Check((int)TsTokenKind.Semicolon) && guard++ < 64)
        {
            if (_r.Check((int)TsTokenKind.Lt))
            {
                if (!_r.TrySkipBalanced((int)TsTokenKind.Lt, (int)TsTokenKind.Gt))
                    break;
                continue;
            }
            _r.Advance();
        }

        int end;
        List<SyntaxNode>? memberNodes = null;
        List<OutlineNode>? memberOutlines = null;

        if (_r.Check((int)TsTokenKind.LBrace))
        {
            bool isNamespace = keyword is TsTokenKind.KwNamespace or TsTokenKind.KwModule;
            if (isNamespace)
            {
                Token open = Current();
                _r.Advance(); // {
                _depth++;
                memberNodes = [];
                memberOutlines = [];
                while (!_r.IsEof && !_r.Check((int)TsTokenKind.RBrace))
                {
                    _r.ThrowIfCancellationRequested();
                    if (_nodeCount >= _maxNodes)
                    {
                        MarkCap();
                        break;
                    }

                    TryParseDeclaration(memberNodes, memberOutlines, level + 1);

                    if (_r.Check((int)TsTokenKind.Semicolon))
                        _r.Advance();
                    else if (!_r.Check((int)TsTokenKind.RBrace))
                        _r.Advance();
                }

                _depth--;
                if (_r.Check((int)TsTokenKind.RBrace))
                {
                    end = Current().End;
                    _r.Advance();
                }
                else
                {
                    _partial = true;
                    end = _text.Length;
                    _diagnostics.Add(new ParseDiagnostic(
                        "TS001", "Unclosed namespace body.",
                        open.Span, DiagnosticSeverity.Error));
                }
            }
            else
            {
                end = ParseBodyEnd();
            }
        }
        else if (_r.Check((int)TsTokenKind.Semicolon))
        {
            end = Current().End;
            _r.Advance();
        }
        else
            end = Current().IsEof ? _text.Length : Current().Start;

        TextSpan span = SpanOf(start, end);
        children.Add(new SyntaxNode(nodeKind, span, name, nameSpan, memberNodes));
        outlines.Add(MakeOutline(outlineKind, name, span, level, memberOutlines));
        _nodeCount++;
        return true;
    }

    private bool ParseTypeAlias(
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        int level,
        int save)
    {
        int start = Current().Start;
        _r.Advance(); // type
        if (!_r.Check((int)TsTokenKind.Identifier))
        {
            _r.Seek(save);
            return false;
        }

        string name = Current().Text ?? "*";
        TextSpan nameSpan = Current().Span;
        _r.Advance();

        // skip generics
        if (_r.Check((int)TsTokenKind.Lt))
            _r.TrySkipBalanced((int)TsTokenKind.Lt, (int)TsTokenKind.Gt);

        if (!_r.TryEat((int)TsTokenKind.Eq))
        {
            // not a type alias
            _r.Seek(save);
            return false;
        }

        // consume until semicolon or next top-level-ish keyword after balanced braces
        while (!_r.IsEof && !_r.Check((int)TsTokenKind.Semicolon))
        {
            if (_r.Check((int)TsTokenKind.LBrace))
            {
                if (!_r.TrySkipBalanced((int)TsTokenKind.LBrace, (int)TsTokenKind.RBrace))
                    _partial = true;
                continue;
            }
            if (_r.Check((int)TsTokenKind.LParen))
            {
                if (!_r.TrySkipBalanced((int)TsTokenKind.LParen, (int)TsTokenKind.RParen))
                    _partial = true;
                continue;
            }
            if (_r.Check((int)TsTokenKind.Lt))
            {
                if (!_r.TrySkipBalanced((int)TsTokenKind.Lt, (int)TsTokenKind.Gt))
                    _partial = true;
                continue;
            }
            // stop before next declaration keyword at depth 0
            TsTokenKind k = CurrentKind();
            if (k is TsTokenKind.KwExport or TsTokenKind.KwFunction or TsTokenKind.KwClass
                or TsTokenKind.KwInterface or TsTokenKind.KwType or TsTokenKind.KwEnum
                or TsTokenKind.KwNamespace or TsTokenKind.KwModule or TsTokenKind.KwConst
                or TsTokenKind.KwLet or TsTokenKind.KwVar or TsTokenKind.KwDeclare)
                break;
            _r.Advance();
        }

        int end;
        if (_r.Check((int)TsTokenKind.Semicolon))
        {
            end = Current().End;
            _r.Advance();
        }
        else
            end = Current().IsEof ? _text.Length : Current().Start;

        TextSpan span = SpanOf(start, end);
        children.Add(new SyntaxNode("type_alias", span, name, nameSpan));
        outlines.Add(MakeOutline("type", name, span, level));
        _nodeCount++;
        return true;
    }

    private TsTokenKind CurrentKind() => (TsTokenKind)Current().Kind;

    private bool ParseFunctionDeclaration(
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        int level,
        bool async)
    {
        _ = async;
        int start = Current().Start;
        _r.Advance(); // function
        if (_r.Check((int)TsTokenKind.Star))
            _r.Advance();

        string name = "*";
        TextSpan? nameSpan = null;
        if (_r.Check((int)TsTokenKind.Identifier))
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
        if (!_r.Check((int)TsTokenKind.Identifier))
        {
            _r.Seek(save);
            return false;
        }

        string name = Current().Text ?? "*";
        TextSpan nameSpan = Current().Span;
        _r.Advance();
        if (!_r.TryEat((int)TsTokenKind.Eq))
        {
            _r.Seek(save);
            return false;
        }

        if (_r.Check((int)TsTokenKind.KwAsync))
            _r.Advance();

        bool isFunc = false;
        if (_r.Check((int)TsTokenKind.KwFunction))
        {
            _r.Advance();
            if (_r.Check((int)TsTokenKind.Star))
                _r.Advance();
            SkipParameterList();
            isFunc = true;
        }
        else if (_r.Check((int)TsTokenKind.LParen))
        {
            SkipBalancedParen();
            if (_r.TryEat((int)TsTokenKind.Arrow))
                isFunc = true;
        }
        else if (_r.Check((int)TsTokenKind.Identifier) && (TsTokenKind)_r.Peek(1).Kind == TsTokenKind.Arrow)
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

    private bool ParseClassDeclaration(
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        int level)
    {
        if (_depth >= _maxDepth)
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic(
                "TS005", "Max nesting depth exceeded.",
                CurrentSpan(), DiagnosticSeverity.Warning));
            _r.Advance();
            return true;
        }

        int start = Current().Start;
        _r.Advance(); // class
        string name = "*";
        TextSpan? nameSpan = null;
        if (_r.Check((int)TsTokenKind.Identifier))
        {
            name = Current().Text ?? "*";
            nameSpan = Current().Span;
            _r.Advance();
        }

        // generics
        if (_r.Check((int)TsTokenKind.Lt))
            _r.TrySkipBalanced((int)TsTokenKind.Lt, (int)TsTokenKind.Gt);

        if (_r.Check((int)TsTokenKind.KwExtends) || _r.Check((int)TsTokenKind.KwImplements))
        {
            while (_r.Check((int)TsTokenKind.KwExtends) || _r.Check((int)TsTokenKind.KwImplements))
            {
                _r.Advance();
                int g = 0;
                while (!_r.IsEof && !_r.Check((int)TsTokenKind.LBrace)
                       && !_r.Check((int)TsTokenKind.KwImplements)
                       && !_r.Check((int)TsTokenKind.KwExtends)
                       && g++ < 32)
                {
                    if (_r.Check((int)TsTokenKind.Lt))
                    {
                        _r.TrySkipBalanced((int)TsTokenKind.Lt, (int)TsTokenKind.Gt);
                        continue;
                    }
                    if (_r.Check((int)TsTokenKind.Comma))
                    {
                        _r.Advance();
                        continue;
                    }
                    SkipExpressionish();
                    if (!_r.Check((int)TsTokenKind.Comma))
                        break;
                }
            }
        }

        if (!_r.Check((int)TsTokenKind.LBrace))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic(
                "TS001", "Expected class body '{'.",
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
        while (!_r.IsEof && !_r.Check((int)TsTokenKind.RBrace))
        {
            _r.ThrowIfCancellationRequested();
            if (_nodeCount >= _maxNodes)
            {
                MarkCap();
                break;
            }

            if (!TryParseMethod(members, memberOutlines, level + 1))
            {
                if (_r.Check((int)TsTokenKind.LBrace))
                {
                    if (!_r.TrySkipBalanced((int)TsTokenKind.LBrace, (int)TsTokenKind.RBrace))
                    {
                        _partial = true;
                        break;
                    }
                }
                else if (_r.Check((int)TsTokenKind.Semicolon))
                    _r.Advance();
                else
                    _r.Advance();
            }
        }

        _depth--;
        int end;
        if (_r.Check((int)TsTokenKind.RBrace))
        {
            end = Current().End;
            _r.Advance();
        }
        else
        {
            _partial = true;
            end = _text.Length;
            _diagnostics.Add(new ParseDiagnostic(
                "TS001", "Unclosed class body.",
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

        while (_r.Check((int)TsTokenKind.KwAsync)
               || _r.Check((int)TsTokenKind.KwStatic)
               || _r.Check((int)TsTokenKind.KwPublic)
               || _r.Check((int)TsTokenKind.KwPrivate)
               || _r.Check((int)TsTokenKind.KwProtected)
               || _r.Check((int)TsTokenKind.KwReadonly)
               || _r.Check((int)TsTokenKind.KwAbstract)
               || _r.Check((int)TsTokenKind.KwOverride))
            _r.Advance();

        bool isGetSet = false;
        if (_r.Check((int)TsTokenKind.KwGet) || _r.Check((int)TsTokenKind.KwSet))
        {
            TsTokenKind after = (TsTokenKind)_r.Peek(1).Kind;
            if (after is TsTokenKind.Identifier or TsTokenKind.StringLiteral or TsTokenKind.LBracket)
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

        if (_r.Check((int)TsTokenKind.KwConstructor))
        {
            kind = "constructor";
            name = "constructor";
            nameSpan = Current().Span;
            _r.Advance();
        }
        else if (_r.Check((int)TsTokenKind.Identifier) || _r.Check((int)TsTokenKind.StringLiteral))
        {
            name = Current().Text ?? _text.Substring(Current().Start, Current().Span.Length);
            nameSpan = Current().Span;
            _r.Advance();
        }
        else if (_r.Check((int)TsTokenKind.LBracket))
        {
            name = "[]";
            nameSpan = Current().Span;
            if (!_r.TrySkipBalanced((int)TsTokenKind.LBracket, (int)TsTokenKind.RBracket))
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

        // optional generics on method
        if (_r.Check((int)TsTokenKind.Lt))
            _r.TrySkipBalanced((int)TsTokenKind.Lt, (int)TsTokenKind.Gt);

        if (!_r.Check((int)TsTokenKind.LParen))
        {
            _r.Seek(save);
            return false;
        }

        SkipParameterList();

        // optional return type : T
        if (_r.Check((int)TsTokenKind.Colon))
        {
            _r.Advance();
            int g = 0;
            while (!_r.IsEof && !_r.Check((int)TsTokenKind.LBrace) && !_r.Check((int)TsTokenKind.Semicolon) && g++ < 48)
            {
                if (_r.Check((int)TsTokenKind.Lt))
                {
                    _r.TrySkipBalanced((int)TsTokenKind.Lt, (int)TsTokenKind.Gt);
                    continue;
                }
                if (_r.Check((int)TsTokenKind.LParen))
                {
                    _r.TrySkipBalanced((int)TsTokenKind.LParen, (int)TsTokenKind.RParen);
                    continue;
                }
                if (_r.Check((int)TsTokenKind.LBrace))
                    break;
                _r.Advance();
            }
        }

        // method with body, or signature ending with ;
        if (_r.Check((int)TsTokenKind.Semicolon))
        {
            int endSig = Current().End;
            _r.Advance();
            TextSpan sigSpan = SpanOf(start, endSig);
            string nk = kind == "constructor" ? "constructor" : "method_definition";
            string ok = kind == "constructor" ? "constructor" : "method";
            members.Add(new SyntaxNode(nk, sigSpan, name, nameSpan));
            memberOutlines.Add(MakeOutline(ok, name, sigSpan, level));
            _nodeCount++;
            _ = isGetSet;
            return true;
        }

        if (!_r.Check((int)TsTokenKind.LBrace))
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

    private void SkipParameterList()
    {
        if (!_r.Check((int)TsTokenKind.LParen))
            return;
        Token open = Current();
        if (!_r.TrySkipBalanced((int)TsTokenKind.LParen, (int)TsTokenKind.RParen))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic(
                "TS001", "Unclosed parameter list.",
                open.Span, DiagnosticSeverity.Error));
        }
    }

    private void SkipBalancedParen()
    {
        if (!_r.TrySkipBalanced((int)TsTokenKind.LParen, (int)TsTokenKind.RParen))
            _partial = true;
    }

    private void SkipExpressionish()
    {
        int guard = 0;
        while (!_r.IsEof && guard++ < 32)
        {
            if (_r.Check((int)TsTokenKind.Identifier)
                || _r.Check((int)TsTokenKind.KwThis)
                || _r.Check((int)TsTokenKind.KwSuper))
            {
                _r.Advance();
                if (_r.Check((int)TsTokenKind.Dot) || _r.Check((int)TsTokenKind.QuestionDot))
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
        if (_r.Check((int)TsTokenKind.LBrace))
        {
            Token open = Current();
            if (_r.TrySkipBalanced((int)TsTokenKind.LBrace, (int)TsTokenKind.RBrace))
                return _r.Peek(-1).End;

            _partial = true;
            _diagnostics.Add(new ParseDiagnostic(
                "TS001", "Unclosed block.",
                open.Span, DiagnosticSeverity.Error));
            return _text.Length;
        }

        while (!_r.IsEof
               && !_r.Check((int)TsTokenKind.Semicolon)
               && !_r.Check((int)TsTokenKind.RBrace)
               && !_r.Check((int)TsTokenKind.Comma))
        {
            if (_r.Check((int)TsTokenKind.LBrace))
            {
                if (!_r.TrySkipBalanced((int)TsTokenKind.LBrace, (int)TsTokenKind.RBrace))
                    _partial = true;
                break;
            }

            if (_r.Check((int)TsTokenKind.LParen))
            {
                if (!_r.TrySkipBalanced((int)TsTokenKind.LParen, (int)TsTokenKind.RParen))
                    _partial = true;
                continue;
            }

            if (_r.Check((int)TsTokenKind.LBracket))
            {
                if (!_r.TrySkipBalanced((int)TsTokenKind.LBracket, (int)TsTokenKind.RBracket))
                    _partial = true;
                continue;
            }

            _r.Advance();
        }

        if (_r.Check((int)TsTokenKind.Semicolon))
        {
            int end = Current().End;
            _r.Advance();
            return end;
        }

        return Current().IsEof ? _text.Length : Current().Start;
    }

    private void RecoverOne()
    {
        if (_r.Check((int)TsTokenKind.LBrace))
        {
            if (!_r.TrySkipBalanced((int)TsTokenKind.LBrace, (int)TsTokenKind.RBrace))
                _partial = true;
        }
        else if (_r.Check((int)TsTokenKind.LParen))
        {
            if (!_r.TrySkipBalanced((int)TsTokenKind.LParen, (int)TsTokenKind.RParen))
                _partial = true;
        }
        else if (_r.Check((int)TsTokenKind.LBracket))
        {
            if (!_r.TrySkipBalanced((int)TsTokenKind.LBracket, (int)TsTokenKind.RBracket))
                _partial = true;
        }
        else
            _r.Advance();
    }

    private void MarkCap()
    {
        _partial = true;
        _diagnostics.Add(new ParseDiagnostic(
            "TS006", "Outline node cap reached.",
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
