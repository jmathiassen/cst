using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.CSharpPreview;

/// <summary>
/// Recursive-descent structure parser for C# Preview outline:
/// namespaces, types (class/struct/interface/record/enum), methods/constructors.
/// </summary>
internal sealed class CsParser
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

    public CsParser(
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

            if (TryParseMember(children, outlines, level: 1, typeName: null))
                continue;

            RecoverOne();
        }

        return children;
    }

    private bool TryParseMember(
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        int level,
        string? typeName)
    {
        int save = _r.Index;

        // skip attributes [ ... ]
        while (_r.Check((int)CsTokenKind.LBracket))
        {
            if (!_r.TrySkipBalanced((int)CsTokenKind.LBracket, (int)CsTokenKind.RBracket))
            {
                _r.Seek(save);
                return false;
            }
        }

        // modifiers
        SkipModifiers();

        // file namespace / namespace
        if (_r.Check((int)CsTokenKind.KwNamespace)
            || (_r.Check((int)CsTokenKind.KwFile) && (CsTokenKind)_r.Peek(1).Kind == CsTokenKind.KwNamespace))
        {
            return ParseNamespace(children, outlines, level, save);
        }

        // type decl
        if (IsTypeKeyword(CurrentKind()))
            return ParseTypeDeclaration(children, outlines, level);

        // method / constructor inside type
        if (typeName is not null)
            return TryParseMethodOrCtor(children, outlines, level, typeName, save);

        // top-level local functions (file-scoped)
        if (TryParseMethodOrCtor(children, outlines, level, typeName: null, save))
            return true;

        _r.Seek(save);
        return false;
    }

    private bool ParseNamespace(
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        int level,
        int save)
    {
        int start = Current().Start;
        if (_r.Check((int)CsTokenKind.KwFile))
            _r.Advance();
        if (!_r.Check((int)CsTokenKind.KwNamespace))
        {
            _r.Seek(save);
            return false;
        }
        _r.Advance();

        string name = ReadDottedName();
        if (name.Length == 0)
            name = "*";

        // file-scoped: namespace X;  or block: namespace X { }
        if (_r.Check((int)CsTokenKind.Semicolon))
        {
            int end = _text.Length; // rest of file
            _r.Advance();
            // parse remaining top-level as nested under namespace for outline?
            // Spec/tests: namespace Demo; then class at same level OR nested.
            // Test accepts class as root or child of namespace.
            TextSpan span = SpanOf(start, end);
            List<SyntaxNode> nsChildren = [];
            List<OutlineNode> nsOutlines = [];
            // continue parsing rest into ns children
            while (!_r.IsEof)
            {
                if (_nodeCount >= _maxNodes)
                {
                    MarkCap();
                    break;
                }
                if (!TryParseMember(nsChildren, nsOutlines, level + 1, typeName: null))
                    RecoverOne();
            }
            children.Add(new SyntaxNode("namespace_declaration", span, name, children: nsChildren));
            outlines.Add(MakeOutline("namespace", name, span, level, nsOutlines));
            _nodeCount++;
            return true;
        }

        if (_r.Check((int)CsTokenKind.LBrace))
        {
            Token open = Current();
            _r.Advance();
            List<SyntaxNode> nsChildren = [];
            List<OutlineNode> nsOutlines = [];
            _depth++;
            while (!_r.IsEof && !_r.Check((int)CsTokenKind.RBrace))
            {
                if (_nodeCount >= _maxNodes)
                {
                    MarkCap();
                    break;
                }
                if (!TryParseMember(nsChildren, nsOutlines, level + 1, typeName: null))
                    RecoverOne();
            }
            _depth--;
            int end;
            if (_r.Check((int)CsTokenKind.RBrace))
            {
                end = Current().End;
                _r.Advance();
            }
            else
            {
                _partial = true;
                end = _text.Length;
                _diagnostics.Add(new ParseDiagnostic(
                    "CS001", "Unclosed namespace.",
                    open.Span, DiagnosticSeverity.Error));
            }
            TextSpan span = SpanOf(start, end);
            children.Add(new SyntaxNode("namespace_declaration", span, name, children: nsChildren));
            outlines.Add(MakeOutline("namespace", name, span, level, nsOutlines));
            _nodeCount++;
            return true;
        }

        // bare
        TextSpan bare = SpanOf(start, Current().Start);
        children.Add(new SyntaxNode("namespace_declaration", bare, name));
        outlines.Add(MakeOutline("namespace", name, bare, level));
        _nodeCount++;
        return true;
    }

    private bool ParseTypeDeclaration(
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        int level)
    {
        if (_depth >= _maxDepth)
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic(
                "CS005", "Max nesting depth exceeded.",
                CurrentSpan(), DiagnosticSeverity.Warning));
            _r.Advance();
            return true;
        }

        int start = Current().Start;
        CsTokenKind kindTok = CurrentKind();
        string outlineKind = kindTok switch
        {
            CsTokenKind.KwClass => "class",
            CsTokenKind.KwStruct => "struct",
            CsTokenKind.KwInterface => "interface",
            CsTokenKind.KwRecord => "record",
            CsTokenKind.KwEnum => "enum",
            _ => "class",
        };
        string nodeKind = outlineKind + "_declaration";
        _r.Advance();

        // record class / record struct
        if (kindTok == CsTokenKind.KwRecord
            && (_r.Check((int)CsTokenKind.KwClass) || _r.Check((int)CsTokenKind.KwStruct)))
        {
            outlineKind = CurrentKind() == CsTokenKind.KwStruct ? "struct" : "class";
            nodeKind = outlineKind + "_declaration";
            _r.Advance();
        }

        string name = "*";
        TextSpan? nameSpan = null;
        if (_r.Check((int)CsTokenKind.Identifier))
        {
            name = Current().Text ?? "*";
            nameSpan = Current().Span;
            _r.Advance();
        }

        SkipTypeHeaderTail();

        // primary ctor: Type( ... ) optional
        if (_r.Check((int)CsTokenKind.LParen))
            _r.TrySkipBalanced((int)CsTokenKind.LParen, (int)CsTokenKind.RParen);

        SkipTypeHeaderTail(); // : base, where

        if (!_r.Check((int)CsTokenKind.LBrace))
        {
            // semicolon body (e.g. empty record;)
            int end = Current().IsEof ? _text.Length : Current().Start;
            if (_r.Check((int)CsTokenKind.Semicolon))
            {
                end = Current().End;
                _r.Advance();
            }
            TextSpan bare = SpanOf(start, end);
            children.Add(new SyntaxNode(nodeKind, bare, name, nameSpan));
            outlines.Add(MakeOutline(outlineKind, name, bare, level));
            _nodeCount++;
            return true;
        }

        Token open = Current();
        _r.Advance(); // {
        _depth++;
        List<SyntaxNode> members = [];
        List<OutlineNode> memberOutlines = [];
        while (!_r.IsEof && !_r.Check((int)CsTokenKind.RBrace))
        {
            _r.ThrowIfCancellationRequested();
            if (_nodeCount >= _maxNodes)
            {
                MarkCap();
                break;
            }

            if (!TryParseMember(members, memberOutlines, level + 1, typeName: name))
                RecoverOne();
        }
        _depth--;

        int bodyEnd;
        if (_r.Check((int)CsTokenKind.RBrace))
        {
            bodyEnd = Current().End;
            _r.Advance();
        }
        else
        {
            _partial = true;
            bodyEnd = _text.Length;
            _diagnostics.Add(new ParseDiagnostic(
                "CS001", "Unclosed type body.",
                open.Span, DiagnosticSeverity.Error));
        }

        TextSpan span = SpanOf(start, bodyEnd);
        children.Add(new SyntaxNode(nodeKind, span, name, nameSpan, members));
        outlines.Add(MakeOutline(outlineKind, name, span, level, memberOutlines));
        _nodeCount++;
        return true;
    }

    private bool TryParseMethodOrCtor(
        List<SyntaxNode> members,
        List<OutlineNode> memberOutlines,
        int level,
        string typeName,
        int save)
    {
        int start = Current().Start;
        SkipModifiers();

        // constructor: TypeName(
        if (_r.Check((int)CsTokenKind.Identifier)
            && string.Equals(Current().Text, typeName, StringComparison.Ordinal)
            && (CsTokenKind)_r.Peek(1).Kind == CsTokenKind.LParen)
        {
            string name = Current().Text ?? typeName;
            TextSpan nameSpan = Current().Span;
            _r.Advance();
            SkipParameterList();
            // : base() / this()
            if (_r.Check((int)CsTokenKind.Colon))
            {
                _r.Advance();
                int g = 0;
                while (!_r.IsEof && !_r.Check((int)CsTokenKind.LBrace) && !_r.Check((int)CsTokenKind.Semicolon) && g++ < 32)
                {
                    if (_r.Check((int)CsTokenKind.LParen))
                        _r.TrySkipBalanced((int)CsTokenKind.LParen, (int)CsTokenKind.RParen);
                    else
                        _r.Advance();
                }
            }
            int end = FinishMethodBody(start);
            TextSpan span = SpanOf(start, end);
            members.Add(new SyntaxNode("constructor", span, name, nameSpan));
            memberOutlines.Add(MakeOutline("constructor", name, span, level));
            _nodeCount++;
            return true;
        }

        // operator declaration: operator X (...)
        if (_r.Check((int)CsTokenKind.KwOperator))
        {
            int opStart = Current().Start;
            _r.Advance(); // operator
            string opName = Current().Text ?? "*";
            TextSpan opNameSpan = Current().Span;
            _r.Advance(); // operator symbol / type name
            SkipParameterList();
            int opEnd = FinishMethodBody(opStart);
            TextSpan opSpan = SpanOf(opStart, opEnd);
            members.Add(new SyntaxNode("method_declaration", opSpan, opName, opNameSpan));
            memberOutlines.Add(MakeOutline("operator", opName, opSpan, level));
            _nodeCount++;
            return true;
        }

        // member: return-type tokens then Ident followed by ( { => or ;
        // also: event / indexer this[
        if (_r.Check((int)CsTokenKind.KwEvent))
        {
            // event Type Name;
            int estart = start;
            _r.Advance();
            int g = 0;
            string ename = "*";
            TextSpan? enameSpan = null;
            while (!_r.IsEof && !_r.Check((int)CsTokenKind.Semicolon) && !_r.Check((int)CsTokenKind.LBrace) && g++ < 24)
            {
                if (_r.Check((int)CsTokenKind.Identifier)
                    && ((CsTokenKind)_r.Peek(1).Kind is CsTokenKind.Semicolon or CsTokenKind.LBrace or CsTokenKind.Eq))
                {
                    ename = Current().Text ?? "*";
                    enameSpan = Current().Span;
                    _r.Advance();
                    break;
                }
                if (_r.Check((int)CsTokenKind.Lt))
                    SkipAngles();
                else
                    _r.Advance();
            }
            int eend = FinishMethodBody(estart);
            TextSpan espan = SpanOf(estart, eend);
            members.Add(new SyntaxNode("event_declaration", espan, ename, enameSpan));
            memberOutlines.Add(MakeOutline("event", ename, espan, level));
            _nodeCount++;
            return true;
        }

        int scan = 0;
        int nameIndex = -1;
        MemberShape shape = MemberShape.None;
        while (scan < 24)
        {
            Token t = _r.Peek(scan);
            if (t.IsEof)
                break;
            CsTokenKind k = (CsTokenKind)t.Kind;
            if (k is CsTokenKind.LBrace or CsTokenKind.RBrace or CsTokenKind.Semicolon)
                break;

            // indexer: this[
            if (k == CsTokenKind.KwThis && (CsTokenKind)_r.Peek(scan + 1).Kind == CsTokenKind.LBracket)
            {
                nameIndex = scan;
                shape = MemberShape.Indexer;
                break;
            }

            // operator overload: operator X (
            if (k == CsTokenKind.KwOperator)
            {
                CsTokenKind after = (CsTokenKind)_r.Peek(scan + 1).Kind;
                if (after is CsTokenKind.LParen)
                {
                    // conversion operator: operator T(
                    nameIndex = scan;
                    shape = MemberShape.Method;
                    break;
                }
                if ((CsTokenKind)_r.Peek(scan + 2).Kind == CsTokenKind.LParen)
                {
                    // operator X (
                    nameIndex = scan + 1;
                    shape = MemberShape.Method;
                    break;
                }
            }

            if (k == CsTokenKind.Identifier)
            {
                CsTokenKind next = (CsTokenKind)_r.Peek(scan + 1).Kind;
                if (next == CsTokenKind.LParen)
                {
                    nameIndex = scan;
                    shape = MemberShape.Method;
                    break;
                }
                if (next is CsTokenKind.LBrace or CsTokenKind.Arrow or CsTokenKind.Eq)
                {
                    nameIndex = scan;
                    shape = MemberShape.Property;
                    break;
                }
                // Ident <...> (  generic method
                if (next == CsTokenKind.Lt)
                {
                    int look = scan + 1;
                    int d = 0;
                    while (look < scan + 48)
                    {
                        CsTokenKind gk = (CsTokenKind)_r.Peek(look).Kind;
                        if (gk == CsTokenKind.Lt) d++;
                        else if (gk == CsTokenKind.Gt) d--;
                        else if (gk == CsTokenKind.GtGt) d -= 2;
                        else if (gk == CsTokenKind.GtGtGt) d -= 3;
                        look++;
                        if (d <= 0) break;
                    }
                    if ((CsTokenKind)_r.Peek(look).Kind == CsTokenKind.LParen)
                    {
                        nameIndex = scan;
                        shape = MemberShape.Method;
                        break;
                    }
                }
            }

            if (k == CsTokenKind.Lt)
            {
                scan++;
                int d = 1;
                while (scan < 48 && d > 0)
                {
                    CsTokenKind gk = (CsTokenKind)_r.Peek(scan).Kind;
                    if (gk == CsTokenKind.Lt)
                        d++;
                    else if (gk == CsTokenKind.Gt)
                        d--;
                    else if (gk == CsTokenKind.GtGt)
                        d -= 2;
                    else if (gk == CsTokenKind.GtGtGt)
                        d -= 3;
                    scan++;
                }
                continue;
            }
            scan++;
        }

        if (nameIndex < 0 || shape == MemberShape.None)
        {
            _r.Seek(save);
            return false;
        }

        bool isOperator = nameIndex > 0 && (CsTokenKind)_r.Peek(nameIndex - 1).Kind == CsTokenKind.KwOperator;

        for (int i = 0; i < nameIndex; i++)
            _r.Advance();

        string mname;
        TextSpan mNameSpan;
        if (shape == MemberShape.Indexer)
        {
            mname = "this";
            mNameSpan = Current().Span;
            _r.Advance(); // this
            if (!_r.TrySkipBalanced((int)CsTokenKind.LBracket, (int)CsTokenKind.RBracket))
            {
                _r.Seek(save);
                return false;
            }
        }
        else
        {
            mname = Current().Text ?? _text.Substring(Current().Start, Current().Span.Length);
            if (!isOperator && mname is "if" or "for" or "while" or "switch" or "using" or "return" or "catch" or "foreach" or "lock")
            {
                _r.Seek(save);
                return false;
            }
            mNameSpan = Current().Span;
            _r.Advance();
        }

        if (shape == MemberShape.Method)
        {
            if (_r.Check((int)CsTokenKind.Lt))
                SkipAngles();
            if (!_r.Check((int)CsTokenKind.LParen))
            {
                _r.Seek(save);
                return false;
            }
            SkipParameterList();
            while (_r.Check((int)CsTokenKind.KwWhere))
            {
                _r.Advance();
                int g = 0;
                while (!_r.IsEof && !_r.Check((int)CsTokenKind.LBrace)
                       && !_r.Check((int)CsTokenKind.Arrow)
                       && !_r.Check((int)CsTokenKind.Semicolon)
                       && !_r.Check((int)CsTokenKind.KwWhere)
                       && g++ < 40)
                {
                    if (_r.Check((int)CsTokenKind.LParen))
                        _r.TrySkipBalanced((int)CsTokenKind.LParen, (int)CsTokenKind.RParen);
                    else
                        _r.Advance();
                }
            }
        }
        else if (shape == MemberShape.Property)
        {
            // optional field initializer junk already handled by next token being { => =
        }

        int mend = FinishMethodBody(start);
        TextSpan mspan = SpanOf(start, mend);
        string outlineKind = (shape, isOperator, typeName) switch
        {
            (MemberShape.Property, _, _) => "property",
            (MemberShape.Indexer, _, _) => "indexer",
            (_, true, _) => "operator",
            (_, _, null) => "function",
            _ => "method",
        };
        string nodeKind = shape switch
        {
            MemberShape.Property => "property_declaration",
            MemberShape.Indexer => "indexer_declaration",
            _ => "method_declaration",
        };
        members.Add(new SyntaxNode(nodeKind, mspan, mname, mNameSpan));
        memberOutlines.Add(MakeOutline(outlineKind, mname, mspan, level));
        _nodeCount++;
        return true;
    }

    private enum MemberShape
    {
        None,
        Method,
        Property,
        Indexer,
    }

    private int FinishMethodBody(int start)
    {
        if (_r.Check((int)CsTokenKind.Arrow))
        {
            // expression-bodied
            _r.Advance();
            while (!_r.IsEof && !_r.Check((int)CsTokenKind.Semicolon))
            {
                if (_r.Check((int)CsTokenKind.LBrace))
                {
                    _r.TrySkipBalanced((int)CsTokenKind.LBrace, (int)CsTokenKind.RBrace);
                    continue;
                }
                if (_r.Check((int)CsTokenKind.LParen))
                {
                    _r.TrySkipBalanced((int)CsTokenKind.LParen, (int)CsTokenKind.RParen);
                    continue;
                }
                _r.Advance();
            }
            if (_r.Check((int)CsTokenKind.Semicolon))
            {
                int end = Current().End;
                _r.Advance();
                return end;
            }
            return Current().IsEof ? _text.Length : Current().Start;
        }

        if (_r.Check((int)CsTokenKind.LBrace))
        {
            Token open = Current();
            if (_r.TrySkipBalanced((int)CsTokenKind.LBrace, (int)CsTokenKind.RBrace))
                return _r.Peek(-1).End;
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic(
                "CS001", "Unclosed method body.",
                open.Span, DiagnosticSeverity.Error));
            return _text.Length;
        }

        if (_r.Check((int)CsTokenKind.Semicolon))
        {
            int end = Current().End;
            _r.Advance();
            return end;
        }

        return Current().IsEof ? _text.Length : Current().Start;
    }

    private void SkipModifiers()
    {
        while (!_r.IsEof && IsModifier(CurrentKind()))
            _r.Advance();
    }

    private void SkipTypeHeaderTail()
    {
        int guard = 0;
        while (!_r.IsEof && guard++ < 64)
        {
            if (_r.Check((int)CsTokenKind.Lt))
            {
                SkipAngles();
                continue;
            }
            if (_r.Check((int)CsTokenKind.Colon))
            {
                _r.Advance();
                // base list
                int g = 0;
                while (!_r.IsEof && !_r.Check((int)CsTokenKind.LBrace)
                       && !_r.Check((int)CsTokenKind.KwWhere)
                       && !_r.Check((int)CsTokenKind.Semicolon)
                       && g++ < 40)
                {
                    if (_r.Check((int)CsTokenKind.Lt))
                        SkipAngles();
                    else if (_r.Check((int)CsTokenKind.LParen))
                        _r.TrySkipBalanced((int)CsTokenKind.LParen, (int)CsTokenKind.RParen);
                    else if (_r.Check((int)CsTokenKind.Comma))
                        _r.Advance();
                    else
                        _r.Advance();
                }
                continue;
            }
            if (_r.Check((int)CsTokenKind.KwWhere))
            {
                _r.Advance();
                int g = 0;
                while (!_r.IsEof && !_r.Check((int)CsTokenKind.LBrace)
                       && !_r.Check((int)CsTokenKind.KwWhere)
                       && !_r.Check((int)CsTokenKind.Semicolon)
                       && g++ < 40)
                {
                    if (_r.Check((int)CsTokenKind.Lt))
                        SkipAngles();
                    else
                        _r.Advance();
                }
                continue;
            }
            break;
        }
    }

    private void SkipParameterList()
    {
        if (!_r.Check((int)CsTokenKind.LParen))
            return;
        Token open = Current();
        if (!_r.TrySkipBalanced((int)CsTokenKind.LParen, (int)CsTokenKind.RParen))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic(
                "CS001", "Unclosed parameter list.",
                open.Span, DiagnosticSeverity.Error));
        }
    }

    private void SkipAngles()
    {
        if (!_r.Check((int)CsTokenKind.Lt))
            return;
        int depth = 0;
        int guard = 0;
        while (!_r.IsEof && guard++ < 200)
        {
            CsTokenKind k = CurrentKind();
            if (k == CsTokenKind.Lt)
            {
                depth++;
                _r.Advance();
            }
            else if (k == CsTokenKind.Gt)
            {
                depth--;
                _r.Advance();
                if (depth == 0)
                    return;
            }
            else if (k == CsTokenKind.GtGt)
            {
                depth -= 2;
                _r.Advance();
                if (depth <= 0)
                    return;
            }
            else if (k == CsTokenKind.GtGtGt)
            {
                depth -= 3;
                _r.Advance();
                if (depth <= 0)
                    return;
            }
            else
                _r.Advance();
        }
        _partial = true;
    }

    private string ReadDottedName()
    {
        if (!_r.Check((int)CsTokenKind.Identifier))
            return "";
        System.Text.StringBuilder sb = new();
        sb.Append(Current().Text ?? "");
        _r.Advance();
        while (_r.Check((int)CsTokenKind.Dot) && (CsTokenKind)_r.Peek(1).Kind == CsTokenKind.Identifier)
        {
            _r.Advance();
            sb.Append('.');
            sb.Append(Current().Text ?? "");
            _r.Advance();
        }
        return sb.ToString();
    }

    private void RecoverOne()
    {
        if (_r.Check((int)CsTokenKind.LBrace))
        {
            if (!_r.TrySkipBalanced((int)CsTokenKind.LBrace, (int)CsTokenKind.RBrace))
                _partial = true;
        }
        else if (_r.Check((int)CsTokenKind.LParen))
        {
            if (!_r.TrySkipBalanced((int)CsTokenKind.LParen, (int)CsTokenKind.RParen))
                _partial = true;
        }
        else if (_r.Check((int)CsTokenKind.LBracket))
        {
            if (!_r.TrySkipBalanced((int)CsTokenKind.LBracket, (int)CsTokenKind.RBracket))
                _partial = true;
        }
        else
            _r.Advance();
    }

    private void MarkCap()
    {
        _partial = true;
        _diagnostics.Add(new ParseDiagnostic(
            "CS006", "Outline node cap reached.",
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

    private static bool IsTypeKeyword(CsTokenKind k) =>
        k is CsTokenKind.KwClass or CsTokenKind.KwStruct or CsTokenKind.KwInterface
            or CsTokenKind.KwRecord or CsTokenKind.KwEnum;

    private static bool IsModifier(CsTokenKind k) =>
        k is CsTokenKind.KwPublic or CsTokenKind.KwPrivate or CsTokenKind.KwProtected
            or CsTokenKind.KwInternal or CsTokenKind.KwStatic or CsTokenKind.KwPartial
            or CsTokenKind.KwAbstract or CsTokenKind.KwSealed or CsTokenKind.KwAsync
            or CsTokenKind.KwVirtual or CsTokenKind.KwOverride or CsTokenKind.KwNew
            or CsTokenKind.KwReadonly or CsTokenKind.KwRequired or CsTokenKind.KwUnsafe
            or CsTokenKind.KwExtern or CsTokenKind.KwFile;

    private Token Current() => _r.Current;
    private TextSpan CurrentSpan() => _r.Current.Span;
    private CsTokenKind CurrentKind() => (CsTokenKind)Current().Kind;

    private static TextSpan SpanOf(int start, int end)
    {
        if (end < start)
            end = start;
        return new TextSpan(start, end - start);
    }
}
